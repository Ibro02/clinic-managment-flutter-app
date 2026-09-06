using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Notifications;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Payments;

public class PaymentService : IPaymentService
{
    private const string ReturnUrl = "https://clinicnow.local/payment-return";
    private const string CancelUrl = "https://clinicnow.local/payment-cancel";

    /// <summary>
    /// How long a created-but-uncaptured PayPal order is treated as still in
    /// play. Inside this window a second order for the same appointment is
    /// refused (review item C12); outside it the stale attempt is superseded
    /// instead, so a patient whose app died mid-payment is never permanently
    /// locked out of paying.
    ///
    /// Time-bounded on purpose: nothing clears a `Pending` row on its own. The
    /// client reports the ordinary "closed the PayPal screen" case through
    /// <see cref="AbandonAsync"/>, which is what makes an immediate retry work;
    /// this window only has to cover the cases where that call never arrives
    /// (app killed, connection dropped). Deliberately well under PayPal's own
    /// order lifetime.
    ///
    /// Short on purpose. Someone sitting in a PayPal sheet finishes in a minute
    /// or two, and a patient who hits this wall has no action available to clear
    /// it - the PayPal screen they'd have to "finish" is already gone - so every
    /// extra minute here is dead time on a paying customer.
    /// </summary>
    private static readonly TimeSpan ActivePendingWindow = TimeSpan.FromMinutes(5);

    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IPayPalClient _payPalClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ClinicNowContext context,
        IMapper mapper,
        IPayPalClient payPalClient,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notificationService,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _mapper = mapper;
        _payPalClient = payPalClient;
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actingUserId = CurrentUserId(CurrentUser());
        var ownPatientId = await GetOwnPatientIdAsync(actingUserId, cancellationToken);

        var appointment = await _context.Appointments
            .Include(a => a.MedicalService)
            .SingleOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Database.Entities.Appointment), request.AppointmentId);

        if (appointment.PatientId != ownPatientId)
        {
            throw new ForbiddenException("Ne možete platiti tuđi termin.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            throw new ValidationException("appointmentId", "Otkazan termin se ne može platiti.");
        }

        // Deliberately Paid/PartiallyRefunded only - NOT Refunded. This guard
        // has to agree exactly with AppointmentService.MapToDto's
        // `IsPaid = currentPayment.Status != PaymentStatus.Refunded`: a fully
        // refunded appointment is reported to the client as unpaid, so the
        // patient is offered a "Plati" button for it. Blocking Refunded here
        // too would make that button always fail with "već plaćen".
        var alreadyPaid = await _context.Payments
            .AnyAsync(p => p.AppointmentId == appointment.Id
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded), cancellationToken);
        if (alreadyPaid)
        {
            throw new BusinessException("Ovaj termin je već plaćen.");
        }

        // C12: an attempt that is still in play must not be able to spawn a
        // second PayPal order for the same appointment. Checked here, before
        // CreateOrderAsync - the point of the item is that the guard sits ahead
        // of the provider call, not at the DB write after it.
        var pendingAttempts = await _context.Payments
            .Where(p => p.AppointmentId == appointment.Id && p.Status == PaymentStatus.Pending)
            .ToListAsync(cancellationToken);

        var activeSince = DateTime.UtcNow - ActivePendingWindow;
        if (pendingAttempts.Any(p => p.CreatedAtUtc >= activeSince))
        {
            // Deliberately does not tell the patient to "finish" the open
            // attempt: by the time they see this the PayPal screen is usually
            // already closed, so that would be advice they cannot act on.
            throw new BusinessException(
                "Plaćanje za ovaj termin je već u toku. Ako ste odustali, pokušajte ponovo za nekoliko minuta.");
        }

        // Anything older than the window is superseded rather than left
        // Pending. A stale row that stays capturable is exactly how one
        // appointment ends up charged twice: the patient's client may still
        // hold its paymentId and call capture on it long after starting a
        // newer attempt.
        foreach (var stale in pendingAttempts)
        {
            stale.Status = PaymentStatus.Cancelled;
        }

        var amountEur = CurrencyConverter.ConvertKmToEur(appointment.MedicalService.Price);
        var (orderId, approveUrl) = await _payPalClient.CreateOrderAsync(amountEur, ReturnUrl, CancelUrl, cancellationToken);

        var payment = new Payment
        {
            AppointmentId = appointment.Id,
            AmountEur = amountEur,
            Status = PaymentStatus.Pending,
            PayPalOrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow
        };
        payment.Items.Add(new PaymentItem
        {
            MedicalServiceId = appointment.MedicalServiceId,
            Description = appointment.MedicalService.Name,
            AmountEur = amountEur
        });

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<PaymentDto>(payment);
        dto.ApproveUrl = approveUrl;
        return dto;
    }

    public async Task<PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadTrackedAsync(paymentId, cancellationToken);
        await EnsureOwnershipAsync(payment, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            // Abandoned or superseded (C12) - deliberately an error rather than
            // the idempotent no-op below, because returning "fine" here would
            // tell a client its payment went through when nothing was charged.
            throw new BusinessException("Ovaj pokušaj plaćanja je otkazan. Pokrenite novo plaćanje.");
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            // Idempotent: a repeated capture call on an already-Paid payment
            // is a no-op success, never a second PayPal call or notification.
            return _mapper.Map<PaymentDto>(payment);
        }

        // C12: re-verify that no *other* payment on this appointment has
        // already succeeded, immediately before the capture call. The row's own
        // status (checked above) only makes a repeat capture of the *same*
        // attempt idempotent; without this, two attempts on one appointment can
        // each be captured - PayPal charges twice, and only then does the
        // second SaveChangesAsync trip the filtered unique index, by which point
        // the money has already moved. This is the guard the item means by
        // "before the external financial call, not at the later DB write".
        var settledElsewhere = await _context.Payments.AnyAsync(
            p => p.AppointmentId == payment.AppointmentId
                && p.Id != payment.Id
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded),
            cancellationToken);

        if (settledElsewhere)
        {
            // Retire this attempt as well as refusing it. Left Pending it would
            // stay capturable, and a later full refund of the winning payment
            // flips the appointment back to "unpaid" - at which point this stale
            // order could still take money for an appointment that was just
            // refunded.
            payment.Status = PaymentStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);

            throw new BusinessException("Ovaj termin je već plaćen.");
        }

        var (captureId, capturedAmountEur, success) = await _payPalClient.CaptureOrderAsync(payment.PayPalOrderId, cancellationToken);

        if (!success || captureId is null)
        {
            // PayPal answered and refused the capture (never approved, expired,
            // declined, compliance hold...). Retire the attempt rather than
            // leaving it Pending: this row can no longer produce a payment, and
            // a dead row that still counts as "in play" is what made a failed
            // capture block every later attempt for the whole staleness window -
            // with no way for the patient to clear it, since the PayPal screen
            // has already closed by then.
            //
            // Only refusals reach this branch. A transport failure (timeout,
            // dropped connection) throws out of CaptureOrderAsync instead, so
            // an attempt whose real outcome is unknown is never retired here.
            payment.Status = PaymentStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);

            throw new BusinessException("Plaćanje nije odobreno na PayPal-u. Pokušajte ponovo.");
        }

        if (Math.Abs(capturedAmountEur - payment.AmountEur) > 0.01m)
        {
            // PayPal enforces the exact order amount for a plain CAPTURE
            // intent, so this should never actually differ - but "never
            // trust the client" extends to "never silently trust a third
            // party either": log it loudly rather than ignore a captured
            // variable, since a mismatch here would be exactly the kind of
            // bug that's invisible until it costs real money.
            _logger.LogWarning("PayPal captured {CapturedAmountEur} EUR for payment {PaymentId} but expected {ExpectedAmountEur} EUR.",
                capturedAmountEur, payment.Id, payment.AmountEur);
        }

        // Real money has now moved, but the row recording it isn't persisted
        // until the SaveChangesAsync below. If that save fails (concurrency,
        // dropped connection, constraint violation) the capture goes
        // unrecorded: PayPal charged the patient while the Payment row stays
        // Pending, so the UI keeps offering "Plati". Logging the actual PayPal
        // capture id here leaves a reconcilable trail (a real capture id with
        // no matching DB row) instead of a silent gap - the same mitigation,
        // and the same trade-off, as the refund path below.
        _logger.LogInformation("PayPal capture {PayPalCaptureId} of {CapturedAmountEur} EUR succeeded for payment {PaymentId}; persisting the record next.",
            captureId, capturedAmountEur, payment.Id);

        payment.PayPalCaptureId = captureId;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var appointment = await _context.Appointments.Include(a => a.Patient).SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.Patient?.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(patientUserId, "Plaćanje uspješno",
                $"Vaša uplata od {payment.AmountEur:F2} EUR je uspješno evidentirana.", cancellationToken);
        }

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto> AbandonAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadTrackedAsync(paymentId, cancellationToken);
        await EnsureOwnershipAsync(payment, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            // Idempotent: the client calls this from a "user closed the PayPal
            // screen" handler, which can plausibly fire twice.
            return _mapper.Map<PaymentDto>(payment);
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            throw new BusinessException("Ova uplata je već obrađena i ne može se otkazati.");
        }

        payment.Status = PaymentStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        // Ownership first, *then* the payment lookup: this endpoint is open to
        // every authenticated role (a Patient checking their own appointment,
        // Staff/Admin checking any), so without this a Patient could pass
        // someone else's appointmentId and read their amount, status and
        // refund history. Checking before the query also keeps "not your
        // appointment" and "no payment yet" from being distinguishable by
        // their different response shapes.
        await EnsureAppointmentAccessAsync(appointmentId, cancellationToken);

        // Neither an in-flight attempt (Pending) nor an abandoned one
        // (Cancelled, C12) is "this appointment's payment" - only a settled row
        // is, so an abandoned attempt can never be mistaken for one.
        var payment = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.AppointmentId == appointmentId
                && p.Status != PaymentStatus.Pending
                && p.Status != PaymentStatus.Cancelled)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return payment is null ? null : _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto> RefundAsync(int paymentId, PaymentRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await LoadTrackedAsync(paymentId, cancellationToken);
        await RefundCoreAsync(payment, request.Amount, request.Reason, CurrentUserId(CurrentUser()), cancellationToken);

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default)
    {
        // The *entire* body sits inside the try, not just the RefundCoreAsync
        // call: IPaymentService documents this method as never throwing, so a
        // DB failure during the lookup below must not propagate into
        // AppointmentService.CancelAsync and fail a cancellation that already
        // succeeded (design doc §4 item 4).
        int? paymentId = null;
        try
        {
            var payment = await _context.Payments
                .Include(p => p.Refunds)
                .Where(p => p.AppointmentId == appointmentId && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded))
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                return; // nothing paid on this appointment - nothing to refund
            }

            paymentId = payment.Id;

            var remaining = payment.AmountEur - payment.Refunds.Sum(r => r.AmountEur);
            if (remaining <= 0)
            {
                return;
            }

            await RefundCoreAsync(payment, remaining, "Termin otkazan.", actingUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The cancellation itself must not fail because a refund attempt
            // did - log it clearly so staff can retry the refund manually
            // (design doc §4 item 4).
            _logger.LogWarning(ex, "Automatic refund failed for cancelled appointment {AppointmentId}, payment {PaymentId} - staff must refund manually.", appointmentId, paymentId);
        }
    }

    /// <summary>
    /// Serializes every refund attempt against the same payment. Two overlapping
    /// refunds on one payment (staff clicking Refund while another staff member
    /// cancels the same appointment, which auto-refunds the remaining balance)
    /// would otherwise each read the same remaining balance before either
    /// committed, and each issue a REAL PayPal refund - a genuine double refund
    /// of real money.
    ///
    /// <c>static</c> for the same reason RecommenderService's own model-train
    /// lock is: this service is <c>Scoped</c>, so an instance field would
    /// give every concurrent request its own uncontended semaphore. In-process
    /// (rather than a PayPal idempotency key or a DB lock) is sufficient here
    /// because the API runs as a single container - see docker-compose.yml; it
    /// is not horizontally scaled.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _refundLocks = new();

    /// <summary>The one code path both the manual and automatic refund triggers share (design doc §4 item 4) - validates against the real remaining balance, calls PayPal, records the refund, and recomputes Status.</summary>
    private async Task RefundCoreAsync(Payment payment, decimal amount, string reason, int refundedByUserId, CancellationToken cancellationToken)
    {
        var paymentLock = _refundLocks.GetOrAdd(payment.Id, _ => new SemaphoreSlim(1, 1));
        await paymentLock.WaitAsync(cancellationToken);
        try
        {
            if (payment.Status != PaymentStatus.Paid && payment.Status != PaymentStatus.PartiallyRefunded)
            {
                throw new ValidationException("paymentId", "Ova uplata se ne može vratiti u ovom statusu.");
            }

            // Rounded before validation so the amount checked against the
            // remaining balance is exactly the amount stored in the
            // decimal(8,2) column and sent to PayPal - same convention as
            // CurrencyConverter.ConvertKmToEur.
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

            // Read the refund total FRESH from the database rather than from
            // payment.Refunds: the caller loaded that navigation collection
            // before this lock was acquired, so for a caller that queued behind
            // a refund that has since committed it is a stale pre-lock snapshot
            // and would report the full balance as still refundable.
            var alreadyRefunded = await _context.PaymentRefunds
                .Where(r => r.PaymentId == payment.Id)
                .SumAsync(r => (decimal?)r.AmountEur, cancellationToken) ?? 0m;
            var remaining = payment.AmountEur - alreadyRefunded;

            if (amount <= 0 || amount > remaining)
            {
                throw new ValidationException("amount", $"Iznos povrata mora biti između 0 i {remaining:F2} EUR.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ValidationException("reason", "Razlog povrata je obavezan.");
            }

            var payPalRefundId = await _payPalClient.RefundCaptureAsync(payment.PayPalCaptureId!, amount, reason, cancellationToken);

            // Real money has now moved, but the row recording it isn't
            // persisted until the SaveChangesAsync below. If that save fails
            // (concurrency, dropped connection, constraint violation) the refund
            // goes unrecorded, and a later attempt would see the full balance
            // still available. Logging the actual PayPal refund id here leaves a
            // reconcilable trail (a real refund id with no matching DB row)
            // instead of a silent gap. The concurrent case is handled
            // structurally by _refundLocks above; this log covers the remaining
            // crash/save-failure window, which no in-process lock can close.
            _logger.LogInformation("PayPal refund {PayPalRefundId} of {AmountEur} EUR succeeded for payment {PaymentId}; persisting the record next.",
                payPalRefundId, amount, payment.Id);

            payment.Refunds.Add(new PaymentRefund
            {
                AmountEur = amount,
                PayPalRefundId = payPalRefundId,
                Reason = reason.Trim(),
                RefundedByUserId = refundedByUserId,
                RefundedAtUtc = DateTime.UtcNow
            });

            var totalRefunded = alreadyRefunded + amount;
            payment.Status = totalRefunded >= payment.AmountEur ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;

            await _context.SaveChangesAsync(cancellationToken);

            // The refund is committed at this point, so a failure to *notify*
            // about it must never surface as a failed refund. Without this local
            // catch, RefundForCancelledAppointmentAsync's blanket handler would
            // log "staff must refund manually" for a refund that succeeded.
            try
            {
                var appointment = await _context.Appointments.Include(a => a.Patient).SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
                if (appointment.Patient?.UserId is int patientUserId)
                {
                    await _notificationService.CreateAsync(patientUserId, "Povrat sredstava",
                        $"Izvršen je povrat od {amount:F2} EUR za vaš termin. Razlog: {reason}", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refund of {AmountEur} EUR for payment {PaymentId} succeeded, but notifying the patient failed.", amount, payment.Id);
            }
        }
        finally
        {
            paymentLock.Release();
        }
    }

    private Task EnsureOwnershipAsync(Payment payment, CancellationToken cancellationToken) =>
        EnsureAppointmentAccessAsync(payment.AppointmentId, "Nemate pristup ovoj uplati.", cancellationToken);

    private Task EnsureAppointmentAccessAsync(int appointmentId, CancellationToken cancellationToken) =>
        EnsureAppointmentAccessAsync(appointmentId, "Nemate pristup ovom terminu.", cancellationToken);

    /// <summary>
    /// Admin/Staff may look at any appointment's payments; anyone else must own
    /// the appointment. A Doctor falls through to the patient branch and is
    /// rejected by <see cref="GetOwnPatientIdAsync"/> - payments are not part
    /// of the doctor-facing surface.
    /// </summary>
    private async Task EnsureAppointmentAccessAsync(int appointmentId, string forbiddenMessage, CancellationToken cancellationToken)
    {
        var principal = CurrentUser();
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff)) return;

        var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
        var appointment = await _context.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Database.Entities.Appointment), appointmentId);
        if (appointment.PatientId != ownPatientId)
        {
            throw new ForbiddenException(forbiddenMessage);
        }
    }

    // --- helpers -----------------------------------------------------------------

    private async Task<Payment> LoadTrackedAsync(int paymentId, CancellationToken cancellationToken) =>
        await _context.Payments.Include(p => p.Refunds).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Payment), paymentId);

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<int> GetOwnPatientIdAsync(int userId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");
        return patient.Id;
    }
}

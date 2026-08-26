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

        var alreadyPaid = await _context.Payments
            .AnyAsync(p => p.AppointmentId == appointment.Id && p.Status != PaymentStatus.Pending, cancellationToken);
        if (alreadyPaid)
        {
            throw new BusinessException("Ovaj termin je već plaćen.");
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

        if (payment.Status != PaymentStatus.Pending)
        {
            // Idempotent: a repeated capture call on an already-Paid payment
            // is a no-op success, never a second PayPal call or notification.
            return _mapper.Map<PaymentDto>(payment);
        }

        var (captureId, capturedAmountEur, success) = await _payPalClient.CaptureOrderAsync(payment.PayPalOrderId, cancellationToken);

        if (!success || captureId is null)
        {
            // Expected outcome if the buyer never approved - the row simply
            // stays Pending, so the client can offer a retry.
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

    public async Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.AppointmentId == appointmentId && p.Status != PaymentStatus.Pending)
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
        var payment = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.AppointmentId == appointmentId && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded))
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return; // nothing paid on this appointment - nothing to refund
        }

        var remaining = payment.AmountEur - payment.Refunds.Sum(r => r.AmountEur);
        if (remaining <= 0)
        {
            return;
        }

        try
        {
            await RefundCoreAsync(payment, remaining, "Termin otkazan.", actingUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The cancellation itself must not fail because a refund attempt
            // did - log it clearly so staff can retry the refund manually
            // (design doc §4 item 4).
            _logger.LogWarning(ex, "Automatic refund failed for cancelled appointment {AppointmentId}, payment {PaymentId} - staff must refund manually.", appointmentId, payment.Id);
        }
    }

    /// <summary>The one code path both the manual and automatic refund triggers share (design doc §4 item 4) - validates against the real remaining balance, calls PayPal, records the refund, and recomputes Status.</summary>
    private async Task RefundCoreAsync(Payment payment, decimal amount, string reason, int refundedByUserId, CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentStatus.Paid && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new ValidationException("paymentId", "Ova uplata se ne može vratiti u ovom statusu.");
        }

        var alreadyRefunded = payment.Refunds.Sum(r => r.AmountEur);
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

        var appointment = await _context.Appointments.Include(a => a.Patient).SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.Patient?.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(patientUserId, "Povrat sredstava",
                $"Izvršen je povrat od {amount:F2} EUR za vaš termin. Razlog: {reason}", cancellationToken);
        }
    }

    private async Task EnsureOwnershipAsync(Payment payment, CancellationToken cancellationToken)
    {
        var principal = CurrentUser();
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff)) return;

        var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
        var appointment = await _context.Appointments.SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.PatientId != ownPatientId)
        {
            throw new ForbiddenException("Nemate pristup ovoj uplati.");
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

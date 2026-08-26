using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// Denormalizes display names onto <see cref="AppointmentDto"/> (rulebook Part II
/// §K: never show raw IDs). <c>AllowedActions</c> is deliberately NOT set here -
/// computing it requires resolving the right state-machine class via DI, which a
/// static Mapster config can't do - <see cref="AppointmentService"/> fills it in
/// as a post-mapping step instead.
/// </summary>
public class AppointmentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Appointment, AppointmentDto>()
            // Patient is an optional navigation (IsRequired(false) in
            // PatientConfiguration) specifically so a soft-deleted patient's
            // appointments still show up instead of disappearing from every
            // list - Patient comes back null for them, so this must not
            // assume it's always present (root cause of a real 500 hit live
            // on this exact endpoint).
            .Map(dest => dest.PatientName, src => src.Patient == null ? "Obrisani pacijent" : src.Patient.FirstName + " " + src.Patient.LastName)
            .Map(dest => dest.DoctorName, src => src.Doctor.User.FirstName + " " + src.Doctor.User.LastName)
            .Map(dest => dest.MedicalServiceName, src => src.MedicalService.Name)
            .Map(dest => dest.LocationName, src => src.Location.Name)
            .Map(dest => dest.StatusName, src => src.Status.ToDisplayName())
            .Ignore(dest => dest.AllowedActions)
            .Ignore(dest => dest.IsPaid)
            // PaymentStatus/PaymentId are nullable (string?/int?) - the null-forgiving
            // operator avoids CS8603 when Mapster boxes them to object internally, same
            // pattern as PaymentMappingConfig's ApproveUrl.
            .Ignore(dest => dest.PaymentStatus!)
            .Ignore(dest => dest.PaymentId!)
            .Ignore(dest => dest.CanRefund);
    }
}

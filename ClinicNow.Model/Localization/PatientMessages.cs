namespace ClinicNow.Model.Localization;

/// <summary>
/// A notification's title and body together, in whichever language was asked
/// for. Reused as-is for the matching email: <c>Title</c> becomes the subject
/// (prefixed with "ClinicNow - " by the caller) and <c>Body</c> the email text -
/// the two have always carried the identical sentence in this codebase, so
/// there is nothing for a separate email-specific string to add.
/// </summary>
public readonly record struct LocalizedMessage(string Title, string Body);

/// <summary>
/// Every patient-facing notification/email text, in Bosnian and English (review
/// item 7: the mobile "jezik aplikacije" setting from the prijava must actually
/// change something the patient receives, not just persist a flag no one reads).
///
/// Deliberately scoped to <em>patient</em>-facing messages only - Doctor/Staff/
/// Administrator accounts have no language preference to read (the setting is a
/// mobile-only, patient-only screen), so their notifications
/// (<see cref="ClinicNow.Services.Appointments.AppointmentService"/>'s
/// doctor-facing "Novi termin zakazan"/"Termin otkazan"/"Termin premješten")
/// stay Bosnian, unchanged.
/// </summary>
public static class PatientMessages
{
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm";

    public static LocalizedMessage PasswordResetCode(string language, string code, double lifetimeMinutes) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "ClinicNow - password reset code",
                $"Your password reset code is: {code}\n\n"
                    + $"The code is valid for {lifetimeMinutes:0} minutes and can be used only once.\n"
                    + "If you did not request a password reset, ignore this message - your password has not been changed.")
            : new LocalizedMessage(
                "ClinicNow - kod za resetovanje lozinke",
                $"Vaš kod za resetovanje lozinke je: {code}\n\n"
                    + $"Kod vrijedi {lifetimeMinutes:0} minuta i može se iskoristiti samo jednom.\n"
                    + "Ako niste tražili resetovanje lozinke, zanemarite ovu poruku - vaša lozinka nije promijenjena.");

    public static LocalizedMessage AppointmentConfirmed(string language, string doctorName, DateTime startUtc) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Appointment confirmed",
                $"Your appointment with dr. {doctorName} on {startUtc.ToString(DateTimeFormat)} UTC is confirmed.")
            : new LocalizedMessage(
                "Termin potvrđen",
                $"Vaš termin kod dr. {doctorName} za {startUtc.ToString(DateTimeFormat)} UTC je potvrđen.");

    public static LocalizedMessage AppointmentCompleted(string language, string doctorName) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Appointment completed",
                $"Your appointment with dr. {doctorName} has been marked as completed.")
            : new LocalizedMessage(
                "Termin završen",
                $"Vaš termin kod dr. {doctorName} je označen kao završen.");

    public static LocalizedMessage AppointmentCancelled(string language, string doctorName, DateTime startUtc, string reason) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Appointment cancelled",
                $"Your appointment with dr. {doctorName} on {startUtc.ToString(DateTimeFormat)} UTC has been cancelled. Reason: {reason}")
            : new LocalizedMessage(
                "Termin otkazan",
                $"Vaš termin kod dr. {doctorName} za {startUtc.ToString(DateTimeFormat)} UTC je otkazan. Razlog: {reason}");

    public static LocalizedMessage AppointmentRescheduled(string language, string doctorName, DateTime startUtc) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Appointment rescheduled",
                $"Your appointment with dr. {doctorName} has been moved to {startUtc.ToString(DateTimeFormat)} UTC.")
            : new LocalizedMessage(
                "Termin premješten",
                $"Vaš termin kod dr. {doctorName} je premješten na {startUtc.ToString(DateTimeFormat)} UTC.");

    public static LocalizedMessage AppointmentReminder(string language, string doctorName, DateTime startUtc) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Appointment reminder",
                $"Reminder: you have an appointment with dr. {doctorName} on {startUtc.ToString(DateTimeFormat)} UTC.")
            : new LocalizedMessage(
                "Podsjetnik za termin",
                $"Podsjetnik: imate zakazan termin kod dr. {doctorName} za {startUtc.ToString(DateTimeFormat)} UTC.");

    public static LocalizedMessage PaymentSuccessful(string language, decimal capturedAmountEur) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Payment successful",
                $"Your payment of {capturedAmountEur:F2} EUR has been recorded.")
            : new LocalizedMessage(
                "Plaćanje uspješno",
                $"Vaša uplata od {capturedAmountEur:F2} EUR je uspješno evidentirana.");

    public static LocalizedMessage RefundPending(string language) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Refund pending",
                "Your appointment has been cancelled, but the automatic refund did not go through. The clinic will process it manually as soon as possible.")
            : new LocalizedMessage(
                "Povrat sredstava u obradi",
                "Vaš termin je otkazan, ali automatski povrat sredstava nije uspio. Klinika će povrat izvršiti ručno u najkraćem roku.");

    public static LocalizedMessage RefundCompleted(string language, decimal amountEur, string reason) =>
        PatientLanguage.Normalize(language) == PatientLanguage.English
            ? new LocalizedMessage(
                "Refund completed",
                $"A refund of {amountEur:F2} EUR was issued for your appointment. Reason: {reason}")
            : new LocalizedMessage(
                "Povrat sredstava",
                $"Izvršen je povrat od {amountEur:F2} EUR za vaš termin. Razlog: {reason}");
}

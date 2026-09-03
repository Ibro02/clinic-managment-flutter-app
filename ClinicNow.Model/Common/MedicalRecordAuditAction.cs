namespace ClinicNow.Model.Common;

/// <summary>
/// What kind of mutation a <c>MedicalRecordAuditLog</c> row records - review
/// item C11: every write to a patient's karton (notes, or a treatment-history
/// entry) must leave a who/when/what trail, not just bump the parent's
/// <c>UpdatedAtUtc</c>. Same role as <see cref="AppointmentAction"/> tagging
/// an <c>AppointmentAuditLog</c> row, but for the karton instead of the
/// booking state machine.
/// </summary>
public enum MedicalRecordAuditAction
{
    NotesAppended,
    NotesReplaced,
    EntryAdded,
    EntryUpdated,
    EntryDeleted
}

namespace ClinicNow.Model.SearchObjects;

public class ReferralSearchObject : BaseSearchObject
{
    /// <summary>Scopes the list to one patient's referral history. Ignored/forced for the Patient role - see <c>ReferralService</c>.</summary>
    public int? PatientId { get; set; }

    /// <summary>
    /// When true, list archived referrals instead of active ones - bypasses
    /// the global <c>ISoftDelete</c> query filter deliberately, for the
    /// "Arhiva" view both Flutter clients show alongside the active list
    /// (same pattern as <c>PatientSearchObject.OnlyDeleted</c>, review item
    /// C3's archive/restore feature).
    /// </summary>
    public bool OnlyArchived { get; set; }

    /// <summary>Case-insensitive partial match against the reason/diagnosis or the target specialist's specialization name (rulebook §2.2: every list needs at least one search parameter).</summary>
    public string? Search { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "CreatedAtUtc" };
}

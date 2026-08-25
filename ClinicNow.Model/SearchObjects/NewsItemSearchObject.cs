namespace ClinicNow.Model.SearchObjects;

public class NewsItemSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against the title.</summary>
    public string? Title { get; set; }
}

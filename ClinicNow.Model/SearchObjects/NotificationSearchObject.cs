namespace ClinicNow.Model.SearchObjects;

public class NotificationSearchObject : BaseSearchObject
{
    /// <summary>When set, filters to only read (true) or only unread (false) notifications.</summary>
    public bool? IsRead { get; set; }
}

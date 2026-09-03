namespace MvcApp.Models.ViewModels;

/// <summary>One alert surfaced in the header notification bell. Computed live from
/// Action Center and Early Warning data — nothing is persisted, so there's no
/// read/unread state to manage and it's always current as of the last fetch.</summary>
public class NotificationItem
{
    public string Type { get; set; } = ""; // "critical" | "stalled" | "overdue" | "high-risk"
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Store name, when this notification is about one specific store —
    /// used to deep-link to its Action Center detail page. Null for the
    /// company-wide "high-risk employees" item.</summary>
    public string? Store { get; set; }
}

namespace MvcApp.Models.ViewModels;

/// <summary>One editable recommendation sentence in the Action Center detection
/// catalog — identified by (SignalCode, Category, Index), the same key scheme
/// already used by the SAP_Rec_* resx entries this replaces as the source of
/// truth for display text. English/Arabic text lives here in the database
/// (via the app_settings key/value table) so an admin can edit it from
/// Settings without a code change; the hardcoded defaults in
/// RecommendationTemplateService are the fallback until first edited.</summary>
public class RecommendationTemplate
{
    public string SignalCode { get; set; } = "";
    public string Category { get; set; } = "";
    public int Index { get; set; }
    public string TextEn { get; set; } = "";
    public string TextAr { get; set; } = "";
    /// <summary>Null until an admin has edited this entry from Settings.</summary>
    public DateTime? UpdatedAtUtc { get; set; }
}

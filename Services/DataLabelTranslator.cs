using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Services;

/// <summary>
/// Gender and Payroll Group are free-text columns from the uploaded Excel
/// sheets — not app-controlled UI strings — so they can't be localized the
/// normal way (a resx key baked into a Razor view or a hardcoded C# label).
/// This maps the small set of values the business actually uses to a
/// localized display label; any other/unexpected value is shown as-is
/// (untranslated) rather than hidden, so a typo'd or new category in the
/// source data never silently disappears from a chart.
/// </summary>
public static class DataLabelTranslator
{
    public static string Gender(string? raw, IStringLocalizer<SharedResource> l)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
        return raw.Trim().ToLowerInvariant() switch
        {
            "male" => l["Common_Gender_Male"],
            "female" => l["Common_Gender_Female"],
            _ => raw,
        };
    }

    // "Manfoods Company" / "Hourly Paid" are this business's own payroll-group
    // vocabulary for full-time vs. hourly-paid staff — a literal Arabic
    // translation of "Manfoods Company" would be meaningless, so this maps to
    // what the categories actually represent instead.
    public static string PayrollGroup(string? raw, IStringLocalizer<SharedResource> l)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
        return raw.Trim().ToLowerInvariant() switch
        {
            "manfoods company" => l["Common_Payroll_Manfoods"],
            "hourly paid" => l["Common_Payroll_HourlyPaid"],
            _ => raw,
        };
    }
}

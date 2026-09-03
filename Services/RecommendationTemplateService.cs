using System.Text.Json;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Admin-editable catalog of the fixed recommendation sentences StoreActionPlanService
/// attaches when a detection signal fires. Same storage pattern as ColorRulesService —
/// JSON in the generic app_settings key/value table — so no schema migration is needed.
/// The hardcoded Defaults below are the English/Arabic pair a signal was originally
/// written with; StoreActionPlanService always creates rows with the English default
/// text (see AddRecommendations), and this service maps that text back to its
/// (SignalCode, Category, Index) slot to resolve the CURRENT (possibly edited) text.
/// </summary>
public class RecommendationTemplateService : IRecommendationTemplateService
{
    private const string SettingsKey = "recommendation_templates";

    // Order matches the SAP_Rec_* resx keys this replaces as the live source of
    // truth for display text (those keys remain in the resx as a historical
    // record but are no longer read at render time).
    private static readonly List<RecommendationTemplate> Defaults = new()
    {
        new() { SignalCode = "HIGH_OVERALL_TURNOVER", Category = "Retention", Index = 0, TextEn = "Review overall staffing stability and scheduling fairness.", TextAr = "راجع استقرار الطاقم البشري وعدالة جداول العمل." },
        new() { SignalCode = "HIGH_OVERALL_TURNOVER", Category = "Retention", Index = 1, TextEn = "Conduct stay-interviews with current team members.", TextAr = "أجرِ مقابلات بقاء مع أعضاء الفريق الحاليين." },
        new() { SignalCode = "HIGH_OVERALL_TURNOVER", Category = "Retention", Index = 2, TextEn = "Assess workload distribution relative to store volume.", TextAr = "قيّم توزيع عبء العمل نسبةً لحجم المبيعات." },

        new() { SignalCode = "EARLY_TURNOVER_90D", Category = "Onboarding", Index = 0, TextEn = "Improve onboarding process for new hires.", TextAr = "طوّر برنامج الاستقبال والتهيئة للموظفين الجدد." },
        new() { SignalCode = "EARLY_TURNOVER_90D", Category = "Onboarding", Index = 1, TextEn = "Assign a mentor/buddy and follow up with new hires during their first 90 days.", TextAr = "خصّص مرافقاً للموظف الجديد وتابعه خلال أول 90 يوماً." },
        new() { SignalCode = "EARLY_TURNOVER_90D", Category = "Onboarding", Index = 2, TextEn = "Review Store Leader coaching on new-hire integration.", TextAr = "راجع دور قائد الفرع في دعم الموظفين الجدد." },

        new() { SignalCode = "LOW_RETENTION_6M", Category = "Retention", Index = 0, TextEn = "Review engagement for employees in the 3-6 month tenure range.", TextAr = "راجع مستوى الانخراط الوظيفي للموظفين في مرحلة 3-6 أشهر." },
        new() { SignalCode = "LOW_RETENTION_6M", Category = "Retention", Index = 1, TextEn = "Check for consistent scheduling and growth opportunities before the 1-year mark.", TextAr = "تأكد من ثبات جداول العمل وتوافر فرص النمو قبل إتمام السنة الأولى." },

        new() { SignalCode = "LEADERSHIP_INSTABILITY", Category = "Leadership", Index = 0, TextEn = "Review Store Leader coaching and support.", TextAr = "راجع دعم قائد الفرع الحالي وخطط تطويره." },
        new() { SignalCode = "LEADERSHIP_INSTABILITY", Category = "Leadership", Index = 1, TextEn = "Ensure operational continuity during leadership transitions.", TextAr = "احرص على الاستمرارية التشغيلية خلال فترات التغيير القيادي." },
        new() { SignalCode = "LEADERSHIP_INSTABILITY", Category = "Leadership", Index = 2, TextEn = "Assess whether additional Head Manager oversight is needed.", TextAr = "قيّم ما إذا كانت الحاجة تستدعي متابعة إضافية من مدير المنطقة." },

        new() { SignalCode = "LOW_EXIT_SENTIMENT", Category = "Culture", Index = 0, TextEn = "Conduct stay-interviews with current team to catch issues early.", TextAr = "أجرِ مقابلات بقاء مع الفريق الحالي للكشف المبكر عن المشكلات." },
        new() { SignalCode = "LOW_EXIT_SENTIMENT", Category = "Culture", Index = 1, TextEn = "Review recent management/culture incidents reported at exit.", TextAr = "راجع الحوادث المتعلقة بالإدارة والبيئة الوظيفية التي ذُكرت في مقابلات الخروج." },

        new() { SignalCode = "REASON_CONCENTRATION", Category = "Compensation", Index = 0, TextEn = "Review pay competitiveness against the local market.", TextAr = "راجع تنافسية الرواتب في السوق المحلي." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Compensation", Index = 1, TextEn = "Check for pay-equity issues within the store.", TextAr = "افحص عدالة الرواتب داخل الفرع." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Workload", Index = 0, TextEn = "Review staffing levels against sales volume.", TextAr = "راجع مستويات التوظيف نسبةً لحجم المبيعات." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Workload", Index = 1, TextEn = "Audit shift scheduling fairness.", TextAr = "افحص عدالة توزيع الورديات." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Management", Index = 0, TextEn = "Coach the Store Leader on people management.", TextAr = "درّب قائد الفرع على إدارة الأفراد." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Management", Index = 1, TextEn = "Review recent complaints or incidents.", TextAr = "راجع الشكاوى والحوادث الأخيرة." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Onboarding", Index = 0, TextEn = "Improve onboarding process for new hires.", TextAr = "طوّر برنامج الاستقبال والتهيئة للموظفين الجدد." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Onboarding", Index = 1, TextEn = "Review Store Leader coaching on new-hire integration.", TextAr = "راجع دور قائد الفرع في دعم الموظفين الجدد." },
        new() { SignalCode = "REASON_CONCENTRATION", Category = "Other", Index = 0, TextEn = "Review the dominant resignation reason with the Store Leader and plan a targeted response.", TextAr = "راجع سبب الاستقالة الأكثر شيوعاً مع قائد الفرع وضع استجابة مستهدفة." },

        new() { SignalCode = "EARLY_WARNING_WATCHLIST", Category = "Monitoring", Index = 0, TextEn = "Proactively check in with employees flagged as at-risk.", TextAr = "تواصل باستباقية مع الموظفين المصنّفين كمخاطر عالية." },
        new() { SignalCode = "EARLY_WARNING_WATCHLIST", Category = "Monitoring", Index = 1, TextEn = "Review the early-warning watchlist with the Store Leader.", TextAr = "راجع قائمة الإنذار المبكر مع قائد الفرع." },

        new() { SignalCode = "NO_DOMINANT_DRIVER", Category = "General", Index = 0, TextEn = "No single cause stands out — recommend a manual review of recent exits with the Store Leader.", TextAr = "لا يوجد سبب محدد بارز — يُنصح بمراجعة يدوية لحالات الخروج الأخيرة مع قائد الفرع." },
    };

    private readonly AppDbContext _db;
    public RecommendationTemplateService(AppDbContext db) { _db = db; }

    private static string Key(string signalCode, string category, int index) => $"{signalCode}|{category}|{index}";

    private async Task<Dictionary<string, RecommendationTemplate>> LoadOverridesAsync()
    {
        var setting = await _db.AppSettings.FindAsync(SettingsKey);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value)) return new();
        try
        {
            var overrides = JsonSerializer.Deserialize<List<RecommendationTemplate>>(setting.Value);
            return overrides?.ToDictionary(t => Key(t.SignalCode, t.Category, t.Index)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public async Task<List<RecommendationTemplate>> GetAllAsync()
    {
        var overrides = await LoadOverridesAsync();
        return Defaults.Select(d =>
        {
            if (overrides.TryGetValue(Key(d.SignalCode, d.Category, d.Index), out var o))
                return new RecommendationTemplate { SignalCode = d.SignalCode, Category = d.Category, Index = d.Index, TextEn = o.TextEn, TextAr = o.TextAr, UpdatedAtUtc = o.UpdatedAtUtc };
            return d;
        }).ToList();
    }

    public async Task SaveAsync(string signalCode, string category, int index, string textEn, string textAr)
    {
        if (!Defaults.Any(d => d.SignalCode == signalCode && d.Category == category && d.Index == index))
            throw new ArgumentException("Unknown recommendation template.");
        if (string.IsNullOrWhiteSpace(textEn) || string.IsNullOrWhiteSpace(textAr))
            throw new ArgumentException("Both English and Arabic text are required.");

        var overrides = await LoadOverridesAsync();
        overrides[Key(signalCode, category, index)] = new RecommendationTemplate
        {
            SignalCode = signalCode, Category = category, Index = index,
            TextEn = textEn.Trim(), TextAr = textAr.Trim(), UpdatedAtUtc = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(overrides.Values.ToList());
        var setting = await _db.AppSettings.FindAsync(SettingsKey);
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = SettingsKey, Value = json });
        else setting.Value = json;
        await _db.SaveChangesAsync();
    }

    /// <summary>Synchronous resolver over an already-loaded template list (from
    /// GetAllAsync) — lets callers resolve many stored rows in a loop without a
    /// database round trip per row.</summary>
    public static string Resolve(List<RecommendationTemplate> all, string signalCode, string category, string storedDefaultText, bool arabic)
    {
        var defaultMatch = Defaults.FirstOrDefault(d => d.SignalCode == signalCode && d.Category == category && d.TextEn == storedDefaultText);
        if (defaultMatch == null) return storedDefaultText;
        var current = all.FirstOrDefault(t => t.SignalCode == signalCode && t.Category == category && t.Index == defaultMatch.Index);
        if (current == null) return storedDefaultText;
        return arabic ? current.TextAr : current.TextEn;
    }
}

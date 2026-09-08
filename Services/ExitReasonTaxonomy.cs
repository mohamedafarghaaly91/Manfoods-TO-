using System.Globalization;
using System.Text;

namespace MvcApp.Services;

/// <summary>
/// Normalizes the free-form-looking Microsoft Forms choice value into a
/// stable analytical taxonomy without changing the raw imported answer.
/// </summary>
public static class ExitReasonTaxonomy
{
    public const string Manager = "manager";
    public const string Compensation = "compensation";
    public const string Schedule = "schedule";
    public const string Workload = "workload";
    public const string CareerGrowth = "career-growth";
    public const string Culture = "culture";
    public const string Transportation = "transportation";
    public const string Education = "education";
    public const string Personal = "personal";
    public const string Relocation = "relocation";
    public const string Other = "other";

    private static readonly IReadOnlyDictionary<string, (string En, string Ar, int Order)> Categories =
        new Dictionary<string, (string En, string Ar, int Order)>
        {
            [Manager] = ("Manager", "المدير والإدارة", 1),
            [Compensation] = ("Compensation", "الراتب والمزايا", 2),
            [Schedule] = ("Schedule", "مواعيد العمل", 3),
            [Workload] = ("Workload", "ضغط وحجم العمل", 4),
            [CareerGrowth] = ("Career Growth", "النمو الوظيفي", 5),
            [Culture] = ("Culture", "ثقافة وبيئة العمل", 6),
            [Transportation] = ("Transportation", "المواصلات وبُعد المسافة", 7),
            [Education] = ("Education / Continuing Studies", "استكمال الدراسة", 8),
            [Personal] = ("Personal / Family", "أسباب شخصية أو عائلية", 9),
            [Relocation] = ("Relocation", "الانتقال", 10),
            [Other] = ("Other / Unclassified", "أخرى / غير مصنف", 99),
        };

    public static string Classify(string? rawReason)
    {
        var value = Normalize(rawReason);
        if (value.Length == 0) return Other;

        // These checks intentionally run before the broader Personal and
        // Transportation checks so study and relocation remain actionable.
        if (HasAny(value, "دراس", "تعليم", "جامعة", "كلية", "امتحان", "school", "study", "education"))
            return Education;

        if (HasAny(value, "انتقل", "انتقال السكن", "نقل السكن", "هجرة", "سافر", "relocation", "moved"))
            return Relocation;

        if (HasAny(value, "مدير", "اداره", "إداره", "مشرف", "معامل", "احترام", "manager", "management", "supervisor"))
            return Manager;

        if (HasAny(value, "مواعيد", "وردي", "شيفت", "دوام", "جدول", "schedule", "shift", "hours"))
            return Schedule;

        if (HasAny(value, "ضغط", "عبء", "مهام", "حمل العمل", "تشغيل", "workload", "work load", "overwork"))
            return Workload;

        if (HasAny(value, "ترقي", "تطور", "مستقبل وظيف", "نمو وظيف", "career", "growth", "promotion"))
            return CareerGrowth;

        if (HasAny(value, "بيئ", "ثقاف", "زمل", "فريق", "تعاون", "culture", "environment", "team"))
            return Culture;

        if (HasAny(value, "مواصل", "مساف", "مسافة", "بعيد", "انتقالات", "بدل الانتقالات", "transport", "distance", "commute"))
            return Transportation;

        if (HasAny(value, "راتب", "مرتب", "اجر", "أجر", "بدل", "حوافز", "مزايا", "salary", "pay", "compensation", "benefit"))
            return Compensation;

        if (HasAny(value, "شخص", "عائل", "أسري", "اسري", "زواج", "ظروف", "personal", "family"))
            return Personal;

        return Other;
    }

    public static string Label(string code)
    {
        if (!Categories.TryGetValue(code, out var category))
            category = Categories[Other];

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar"
            ? category.Ar
            : category.En;
    }

    public static int Order(string code) =>
        Categories.TryGetValue(code, out var category) ? category.Order : Categories[Other].Order;

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('أ', 'ا')
            .Replace('إ', 'ا')
            .Replace('آ', 'ا')
            .Replace('ى', 'ي');
    }

    private static bool HasAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
using System.Reflection;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>
/// Fix #1: StoreActionPlanService.MapReasonToRecommendations must classify the
/// Arabic ReasonForLeaving values the platform actually stores (verbatim from
/// the Microsoft Forms column "برجاء اختيار سبب ترك العمل" — see
/// UploadService.cs), not English substrings that never appear in real data.
/// The method is intentionally left private (no production accessibility
/// change was needed to fix the bug), so these tests invoke it via reflection.
/// </summary>
public class StoreActionPlanReasonMappingTests
{
    private static (string Category, List<string> Recommendations) MapReason(string reason)
    {
        var method = typeof(StoreActionPlanService).GetMethod(
            "MapReasonToRecommendations", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { reason });
        Assert.NotNull(result);

        var tupleType = result!.GetType();
        var category = (string)tupleType.GetField("Item1")!.GetValue(result)!;
        var recommendations = (List<string>)tupleType.GetField("Item2")!.GetValue(result)!;
        return (category, recommendations);
    }

    [Theory]
    [InlineData("الراتب غير مناسب")]
    [InlineData("المرتب قليل مقارنة بالسوق")]
    [InlineData("بدل الانتقالات غير كافي")]
    public void ArabicCompensationReason_MapsToCompensation(string reason)
    {
        var (category, recs) = MapReason(reason);
        Assert.Equal("Compensation", category);
        Assert.NotEmpty(recs);
    }

    [Theory]
    [InlineData("ضغط العمل الشديد")]
    [InlineData("ساعات العمل طويلة جدا")]
    [InlineData("عدم وجود جدول واضح للورديات")]
    public void ArabicWorkloadReason_MapsToWorkload(string reason)
    {
        var (category, recs) = MapReason(reason);
        Assert.Equal("Workload", category);
        Assert.NotEmpty(recs);
    }

    [Theory]
    [InlineData("سوء المعاملة من الإدارة")]
    [InlineData("عدم وجود احترام من المدير")]
    [InlineData("المعاملة غير عادلة بين الموظفين")]
    public void ArabicManagementReason_MapsToManagement(string reason)
    {
        var (category, recs) = MapReason(reason);
        Assert.Equal("Management", category);
        Assert.NotEmpty(recs);
    }

    [Theory]
    [InlineData("عدم وجود تدريب كافي")]
    [InlineData("لا يوجد تأهيل مناسب للموظف الجديد")]
    public void ArabicTrainingReason_MapsToOnboarding(string reason)
    {
        var (category, recs) = MapReason(reason);
        Assert.Equal("Onboarding", category);
        Assert.NotEmpty(recs);
    }

    [Theory]
    [InlineData("ظروف شخصية")]
    [InlineData("انتقلت للسكن في مكان بعيد")]
    [InlineData("")]
    public void UnrelatedReason_FallsBackToOther(string reason)
    {
        var (category, recs) = MapReason(reason);
        Assert.Equal("Other", category);
        Assert.NotEmpty(recs);
    }

    [Fact]
    public void EnglishText_NoLongerMatchesAnyCategory_ConfirmsLanguageWasTheBug()
    {
        // The pre-fix implementation matched English substrings like "pay" that
        // never occur in the platform's Arabic-only ReasonForLeaving data. This
        // confirms the fix changed the matching *language*, not just added
        // Arabic keywords on top of a still-live (but unreachable) English path.
        var (category, _) = MapReason("pay issue / workload / management / training");
        Assert.Equal("Other", category);
    }
}

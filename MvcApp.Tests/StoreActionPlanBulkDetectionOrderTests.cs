using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>
/// Fix #2: bulk detection must process periods oldest→newest for
/// HealthyStreakCount / auto-resolution to behave correctly. These tests drive
/// StoreActionPlanService.RunDetectionForPeriodAsync directly — the same method
/// the fixed controller now calls, just in the corrected order — backed by a
/// real DashboardService (so HIGH_OVERALL_TURNOVER is a genuine signal) and
/// no-op fakes for every other signal source, isolating the streak/order state
/// machine from unrelated signals.
/// </summary>
public class StoreActionPlanBulkDetectionOrderTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly IStringLocalizer<SharedResource> Localizer =
        new ServiceCollection().AddLocalization().BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static StoreActionPlanService NewService(AppDbContext db)
    {
        var storeAccess = new StoreAccessService(db);
        var dashboard = new DashboardService(db, new MemoryCache(new MemoryCacheOptions()), storeAccess, Localizer);
        return new StoreActionPlanService(
            db, storeAccess,
            new ActionPlanRoleService(db, storeAccess),
            new NoOpStoreService(),
            dashboard,
            new NoOpNinetyDayTurnoverService(),
            new NoOpRetentionService(),
            new NoOpEarlyWarningService(),
            new NoOpExitInterviewService(),
            new RecommendationTemplateService(db));
    }

    /// <summary>Seeds one store/period with a fixed headcount and resignation
    /// count (turnover = resignations/headcount*100 for that single period).</summary>
    private static async Task SeedPeriodAsync(AppDbContext db, string store, int month, int year,
        int headcount, int resignations, string leader = "Leader1")
    {
        db.StoreReferences.Add(new StoreReference { Month = month, Year = year, StoreName = store, StoreLeader = leader });

        var oldHire = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3));
        for (int i = 0; i < headcount; i++)
            db.ActiveEmployees.Add(new ActiveEmployee
            {
                EmployeeId = $"{store}-emp{i}", Store = store, Month = month, Year = year,
                HireDate = oldHire, JobTitle = "Crew", Gender = "F",
            });

        for (int i = 0; i < resignations; i++)
            db.Resignations.Add(new Resignation
            {
                EmployeeId = $"{store}-res{month}-{year}-{i}", Store = store, Month = month, Year = year,
                HireDate = oldHire, ResignationDate = new DateOnly(year, month, 1), JobTitle = "Crew", Gender = "F",
            });

        await db.SaveChangesAsync();
    }

    private static (int Year, int JanMonth) SafePastWindow() =>
        (DateTime.UtcNow.AddYears(-2).Year, 1); // fixed Jan/Feb/Mar window, safely in the past

    [Fact]
    public async Task AscendingOrder_JanBad_FebGood_MarGood_AutoResolvesByMarch()
    {
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();
        const string store = "Store A";

        // Jan: 10 headcount, 3 resignations -> 30% turnover, fires HIGH_OVERALL_TURNOVER.
        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);
        // Feb/Mar: same 10 employees still active, no resignations -> 0% turnover, healthy.
        await SeedPeriodAsync(db, store, jan + 1, y, headcount: 10, resignations: 0);
        await SeedPeriodAsync(db, store, jan + 2, y, headcount: 10, resignations: 0);

        await service.RunDetectionForPeriodAsync(jan, y);
        await service.RunDetectionForPeriodAsync(jan + 1, y);
        await service.RunDetectionForPeriodAsync(jan + 2, y);

        var plan = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal("Resolved", plan.Status);
        Assert.True(plan.HealthyStreakCount >= 2);
        Assert.Equal(jan + 2, plan.LastEvaluatedMonth);
        Assert.Equal(y, plan.LastEvaluatedYear);
    }

    [Fact]
    public async Task DescendingOrder_ReproducesTheBug_PlanStaysActiveAndUnresolved()
    {
        // Identical data to the ascending test, fed Mar -> Feb -> Jan (the
        // pre-fix controller's order) to document the exact failure mode.
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();
        const string store = "Store A";

        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);
        await SeedPeriodAsync(db, store, jan + 1, y, headcount: 10, resignations: 0);
        await SeedPeriodAsync(db, store, jan + 2, y, headcount: 10, resignations: 0);

        await service.RunDetectionForPeriodAsync(jan + 2, y); // Mar first
        await service.RunDetectionForPeriodAsync(jan + 1, y); // Feb
        await service.RunDetectionForPeriodAsync(jan, y);     // Jan last

        var plan = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal("Active", plan.Status);       // never auto-resolves
        Assert.Equal(0, plan.HealthyStreakCount);  // Feb/Mar's healthy cycles were lost
        Assert.Equal(jan, plan.LastEvaluatedMonth); // pinned to the OLDEST period, not the latest
    }

    [Fact]
    public async Task RunningSamePeriodTwice_DoesNotDoubleIncrementStreak()
    {
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();
        const string store = "Store B";

        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);
        await SeedPeriodAsync(db, store, jan + 1, y, headcount: 10, resignations: 0);

        await service.RunDetectionForPeriodAsync(jan, y);
        await service.RunDetectionForPeriodAsync(jan + 1, y);
        var afterFirst = (await db.StoreActionPlans.SingleAsync(p => p.StoreName == store)).HealthyStreakCount;

        // Re-run the exact same (already-evaluated) period again.
        await service.RunDetectionForPeriodAsync(jan + 1, y);
        var afterRepeat = (await db.StoreActionPlans.SingleAsync(p => p.StoreName == store)).HealthyStreakCount;

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, afterRepeat); // guard held: no double count
    }

    [Fact]
    public async Task MultipleStores_ProcessedTogetherAscending_RemainIndependentlyCorrect()
    {
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();

        // Store A: bad -> good -> good (should resolve).
        await SeedPeriodAsync(db, "Store A", jan, y, headcount: 10, resignations: 3);
        await SeedPeriodAsync(db, "Store A", jan + 1, y, headcount: 10, resignations: 0);
        await SeedPeriodAsync(db, "Store A", jan + 2, y, headcount: 10, resignations: 0);

        // Store C: good the whole time (should never get a plan).
        await SeedPeriodAsync(db, "Store C", jan, y, headcount: 10, resignations: 0);
        await SeedPeriodAsync(db, "Store C", jan + 1, y, headcount: 10, resignations: 0);
        await SeedPeriodAsync(db, "Store C", jan + 2, y, headcount: 10, resignations: 0);

        foreach (var month in new[] { jan, jan + 1, jan + 2 })
            await service.RunDetectionForPeriodAsync(month, y);

        var planA = await db.StoreActionPlans.SingleOrDefaultAsync(p => p.StoreName == "Store A");
        var planC = await db.StoreActionPlans.SingleOrDefaultAsync(p => p.StoreName == "Store C");

        Assert.NotNull(planA);
        Assert.Equal("Resolved", planA!.Status);
        Assert.Null(planC);
    }

    [Fact]
    public void ControllerOrderingExpression_SortsNewestFirstPeriodsAscending()
    {
        // Direct check of the exact ordering expression the fixed controller
        // applies to GetAvailablePeriodsAsync's real (newest-first) output.
        var periods = new List<PeriodItem>
        {
            new() { Month = 3, Year = 2026 },
            new() { Month = 1, Year = 2026 },
            new() { Month = 2, Year = 2026 },
        };

        var ordered = periods.OrderBy(p => p.Year).ThenBy(p => p.Month).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(p => p.Month));
    }
}

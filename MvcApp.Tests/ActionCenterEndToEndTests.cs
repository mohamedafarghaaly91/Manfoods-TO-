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
/// End-to-end validation of the full Action Center lifecycle after the
/// configurable-severity / signal_occurrences / target-window / signal-history
/// changes: Signal -> Persistence -> Plan Creation -> Severity -> Target
/// Resolution Date -> Signal History -> Progress/Healthy Streak -> Closure ->
/// Outcome. Drives the real StoreActionPlanService against an in-memory DB,
/// with the same real-DashboardService / no-op-everything-else isolation
/// StoreActionPlanBulkDetectionOrderTests already uses, so only
/// HIGH_OVERALL_TURNOVER can fire. No business logic under test is modified —
/// this file only exercises the existing implementation.
/// </summary>
public class ActionCenterEndToEndTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly IStringLocalizer<SharedResource> Localizer =
        new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static StoreActionPlanService NewService(AppDbContext db, IEarlyWarningService? earlyWarning = null)
    {
        var storeAccess = new StoreAccessService(db);
        var dashboard = new DashboardService(db, new MemoryCache(new MemoryCacheOptions()), storeAccess, Localizer);
        return new StoreActionPlanService(
            db, storeAccess,
            new ActionPlanRoleService(db, storeAccess),
            new ActionPlanSeverityConfigService(db),
            new NoOpStoreService(),
            dashboard,
            new NoOpNinetyDayTurnoverService(),
            new NoOpRetentionService(),
            earlyWarning ?? new NoOpEarlyWarningService(),
            new NoOpExitInterviewService(),
            new RecommendationTemplateService(db));
    }

    /// <summary>Always reports 5 high-risk employees, so EARLY_WARNING_WATCHLIST
    /// genuinely fires — used only to prove the historical backfill explicitly
    /// excludes this signal rather than it simply never having fired.</summary>
    private class AlwaysHighRiskEarlyWarningService : IEarlyWarningService
    {
        public Task<List<string>> GetStoreListAsync(string role, string? assignedName) => Task.FromResult(new List<string>());
        public Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store, string role, string? assignedName, string? months = null, int? year = null) =>
            Task.FromResult(new List<EarlyWarningItem>());
        public Task<EarlyWarningSummary> GetSummaryAsync(string? store, string role, string? assignedName, string? months = null, int? year = null) =>
            Task.FromResult(new EarlyWarningSummary { HighRiskCount = 5 });
    }

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
        (DateTime.UtcNow.AddYears(-2).Year, 1);

    [Fact]
    public async Task FullLifecycle_SignalThroughPersistenceSeverityTargetDateHistoryAndAutoResolve()
    {
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();
        const string store = "Store E2E Lifecycle";

        // Jan: bad (30% turnover) -> fires HIGH_OVERALL_TURNOVER, creates the plan.
        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);
        await service.RunDetectionForPeriodAsync(jan, y);

        var plan = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal("Active", plan.Status);
        Assert.Equal(jan, plan.CreatedMonth);
        Assert.Equal(y, plan.CreatedYear);

        // ── Severity: HIGH_OVERALL_TURNOVER with no other specific driver also
        // fires the pre-existing NO_DOMINANT_DRIVER fallback note (unrelated to
        // this session's changes — MapReasonToRecommendations/ComputeSignalsAsync's
        // own honest-fallback logic), so 2 distinct signals -> High under the
        // default bands (Medium >= 1, High >= 2, Critical >= 3). ──
        var detail1 = await service.GetActionCenterDetailAsync(store, "Admin", null);
        Assert.NotNull(detail1);
        Assert.Equal(2, detail1!.Recommendations.Select(r => r.SignalCode).Distinct().Count());
        Assert.Equal("High", detail1.Severity);

        // ── Target Resolution Date: Standard (90-day) window, since not Critical ──
        Assert.NotNull(detail1.TargetResolutionDate);
        Assert.Equal("Standard", detail1.TargetWindowType);
        var daysToTarget = detail1.TargetResolutionDate!.Value.DayNumber - DateOnly.FromDateTime(detail1.CreatedAt).DayNumber;
        Assert.InRange(daysToTarget, 88, 92);

        // ── Reporting quarter is derived from CreatedAt ──
        Assert.Equal((jan - 1) / 3 + 1, detail1.ReportingQuarter);
        Assert.Equal(y, detail1.ReportingYear);

        // Feb: no upload at all for this store (simulates a missed monthly upload) —
        // deliberately never call RunDetectionForPeriodAsync for it.

        // Mar: bad again -> same signal re-fires; persistence should now be satisfied
        // using only the 2 data-available periods (Jan, Mar), Feb correctly skipped
        // rather than treated as a clean period.
        await SeedPeriodAsync(db, store, jan + 2, y, headcount: 10, resignations: 3);
        await service.RunDetectionForPeriodAsync(jan + 2, y);

        var isPersistent = await service.IsSignalPersistentAsync(store, "HIGH_OVERALL_TURNOVER", jan + 2, y);
        Assert.True(isPersistent);

        // ── Signal History: Jan has data + fired, Feb has no data at all (never
        // implied clean), Mar has data + fired. Newest period first. ──
        var history = await service.GetSignalHistoryAsync(store, "Admin", null);
        Assert.NotNull(history);
        Assert.Equal(3, history!.Periods.Count);

        var febPeriod = history.Periods.Single(p => p.Month == jan + 1 && p.Year == y);
        Assert.False(febPeriod.HasData);
        Assert.Empty(febPeriod.Signals);

        var janPeriod = history.Periods.Single(p => p.Month == jan && p.Year == y);
        Assert.True(janPeriod.HasData);
        Assert.Contains("HIGH_OVERALL_TURNOVER", janPeriod.Signals);

        var marPeriod = history.Periods.Single(p => p.Month == jan + 2 && p.Year == y);
        Assert.True(marPeriod.HasData);
        Assert.Contains("HIGH_OVERALL_TURNOVER", marPeriod.Signals);

        var persistenceEntry = history.Persistence.Single(p => p.SignalCode == "HIGH_OVERALL_TURNOVER");
        Assert.True(persistenceEntry.IsPersistent);

        // ── Backfill must be idempotent: live detection already logged both
        // signals (HIGH_OVERALL_TURNOVER + NO_DOMINANT_DRIVER) for Jan and Mar,
        // so re-running the historical backfill must not duplicate those rows. ──
        var backfillWritten = await service.RunHistoricalSignalBackfillAsync();
        Assert.Equal(0, backfillWritten);
        Assert.Equal(4, await db.SignalOccurrences.CountAsync(o => o.StoreName == store));

        // ── Progress / Healthy Streak: 2 consecutive clean cycles auto-resolve ──
        await SeedPeriodAsync(db, store, jan + 3, y, headcount: 10, resignations: 0); // Apr: clean
        await service.RunDetectionForPeriodAsync(jan + 3, y);
        var afterApr = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal(1, afterApr.HealthyStreakCount);
        Assert.Equal("Active", afterApr.Status);

        await SeedPeriodAsync(db, store, jan + 4, y, headcount: 10, resignations: 0); // May: clean again
        await service.RunDetectionForPeriodAsync(jan + 4, y);

        // ── Closure / Outcome ──
        var final = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal("Resolved", final.Status);
        Assert.Equal("Auto_Improvement", final.ResolvedReason);
        Assert.NotNull(final.ResolvedAt);

        var detailFinal = await service.GetActionCenterDetailAsync(store, "Admin", null);
        Assert.Equal("Resolved", detailFinal!.Status);
    }

    [Fact]
    public async Task Backfill_PopulatesHistoryForStoreNeverLiveDetected_AndSkipsEarlyWarning()
    {
        var db = NewDb();
        // Deliberately wired so EARLY_WARNING_WATCHLIST genuinely fires too —
        // proves the backfill explicitly excludes it rather than it simply
        // never having occurred.
        var service = NewService(db, new AlwaysHighRiskEarlyWarningService());
        var (y, jan) = SafePastWindow();
        const string store = "Store E2E Backfill";

        // Seed a bad period's source data directly, without ever running live
        // detection (RunDetectionForPeriodAsync) — simulates historical data that
        // predates this feature and was never evaluated at the time.
        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);

        Assert.Empty(await db.SignalOccurrences.Where(o => o.StoreName == store).ToListAsync());

        var written = await service.RunHistoricalSignalBackfillAsync();

        // HIGH_OVERALL_TURNOVER and its NO_DOMINANT_DRIVER fallback (no other
        // specific driver fired) should be backfilled — EARLY_WARNING_WATCHLIST
        // fired too (per the fake above) but must be excluded from the backfill.
        Assert.Equal(2, written);
        var codes = (await db.SignalOccurrences.Where(o => o.StoreName == store).ToListAsync())
            .Select(o => o.SignalCode).ToList();
        Assert.Contains("HIGH_OVERALL_TURNOVER", codes);
        Assert.Contains("NO_DOMINANT_DRIVER", codes);
        Assert.DoesNotContain("EARLY_WARNING_WATCHLIST", codes);
        Assert.All(await db.SignalOccurrences.Where(o => o.StoreName == store).ToListAsync(), o => Assert.True(o.IsBackfilled));

        // Re-running is a no-op — idempotent.
        var writtenAgain = await service.RunHistoricalSignalBackfillAsync();
        Assert.Equal(0, writtenAgain);
        Assert.Equal(2, await db.SignalOccurrences.CountAsync(o => o.StoreName == store));
    }

    [Fact]
    public async Task ManualClose_ByAdmin_ProducesResolvedOutcomeWithReason()
    {
        var db = NewDb();
        var service = NewService(db);
        var (y, jan) = SafePastWindow();
        const string store = "Store E2E ManualClose";

        await SeedPeriodAsync(db, store, jan, y, headcount: 10, resignations: 3);
        await service.RunDetectionForPeriodAsync(jan, y);
        Assert.Equal("Active", (await db.StoreActionPlans.SingleAsync(p => p.StoreName == store)).Status);

        var (success, _) = await service.ManualCloseAsync(store, "Fixed on-site by Head Manager.", "Admin", "Test Admin");
        Assert.True(success);

        var plan = await db.StoreActionPlans.SingleAsync(p => p.StoreName == store);
        Assert.Equal("Resolved", plan.Status);
        Assert.Equal("Manual_Override", plan.ResolvedReason);
        Assert.Equal("Test Admin", plan.ClosedByName);
        Assert.Equal("Fixed on-site by Head Manager.", plan.ManualCloseReason);
    }
}

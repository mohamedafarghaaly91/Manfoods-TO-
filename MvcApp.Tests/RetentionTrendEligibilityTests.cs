using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>
/// Fix #3: RetentionService.GetTrendAsync must gate each milestone's numerator
/// and denominator with the same MetricsCalculationService.IsEligibleForMilestone
/// rule GetMilestonesAsync/GetSurvivalCurveAsync already use, instead of treating
/// every still-active cohort member as "retained" at every milestone regardless
/// of how little time has actually passed.
/// </summary>
public class RetentionTrendEligibilityTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static RetentionService NewService(AppDbContext db) =>
        new(db, new StoreAccessService(db), new MemoryCache(new MemoryCacheOptions()));

    private static void AddActive(AppDbContext db, string employeeId, string store, DateOnly hireDate, int month, int year) =>
        db.ActiveEmployees.Add(new ActiveEmployee
        {
            EmployeeId = employeeId, Store = store, HireDate = hireDate, Month = month, Year = year,
        });

    private static void AddResigned(AppDbContext db, string employeeId, string store, DateOnly hireDate, DateOnly resignDate, int month, int year) =>
        db.Resignations.Add(new Resignation
        {
            EmployeeId = employeeId, Store = store, HireDate = hireDate, ResignationDate = resignDate, Month = month, Year = year,
        });

    [Fact]
    public async Task ImmatureCohort_TwoWeeksOld_FiveYearMilestone_IsNullNotFabricated100Percent()
    {
        var db = NewDb();
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        var (month, year) = (hireDate.Month, hireDate.Year);

        for (int i = 0; i < 100; i++)
            AddActive(db, $"E{i}", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        await db.SaveChangesAsync();

        var trend = await NewService(db).GetTrendAsync(store: null, role: "Admin", assignedName: null);
        var point = Assert.Single(trend, p => p.Label == new DateOnly(year, month, 1).ToString("MMM yy"));

        // Before the fix this was 100.0 (every still-active member counted as
        // "retained" at a milestone the cohort had no chance to reach yet).
        Assert.Null(point.Rates["5 Years"]);
        Assert.True(point.Provisional["5 Years"]);
    }

    [Fact]
    public async Task MatureCohort_OverFiveYearsOld_RatesUnchangedFromPreFixBehavior()
    {
        var db = NewDb();
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2200)); // ~6 years, past every milestone
        var (month, year) = (hireDate.Month, hireDate.Year);

        // 2 still active (retained everywhere) + 2 resigned early (tenure 100 days,
        // a fully decided outcome, not retained at any milestone from 6mo up).
        AddActive(db, "A1", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        AddActive(db, "A2", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        AddResigned(db, "R1", "Alpha", hireDate, hireDate.AddDays(100), hireDate.Month, hireDate.Year);
        AddResigned(db, "R2", "Alpha", hireDate, hireDate.AddDays(100), hireDate.Month, hireDate.Year);
        await db.SaveChangesAsync();

        var trend = await NewService(db).GetTrendAsync(store: null, role: "Admin", assignedName: null);
        var point = Assert.Single(trend, p => p.Label == new DateOnly(year, month, 1).ToString("MMM yy"));

        // Every member's outcome is decided (2 resigned, 2 still active but old
        // enough to count as "retained" at every milestone) -> eligible == total,
        // identical to the pre-fix, unguarded calculation.
        foreach (var label in new[] { "6 Months", "1 Year", "2 Years", "3 Years", "4 Years", "5 Years" })
        {
            Assert.Equal(50.0, point.Rates[label]);
            Assert.False(point.Provisional[label]);
        }
    }

    [Fact]
    public async Task PartiallyEligibleCohort_TwoHundredDaysOld_MilestonesDivergeAsExpected()
    {
        var db = NewDb();
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-200));
        var (month, year) = (hireDate.Month, hireDate.Year);

        // 1 resigned early (decided, tenure 50 days) + 3 still active (undecided
        // beyond 200 elapsed days).
        AddResigned(db, "R1", "Alpha", hireDate, hireDate.AddDays(50), hireDate.Month, hireDate.Year);
        AddActive(db, "A1", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        AddActive(db, "A2", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        AddActive(db, "A3", "Alpha", hireDate, DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        await db.SaveChangesAsync();

        var trend = await NewService(db).GetTrendAsync(store: null, role: "Admin", assignedName: null);
        var point = Assert.Single(trend, p => p.Label == new DateOnly(year, month, 1).ToString("MMM yy"));

        // 6 Months (180d): 200 elapsed >= 180, so the 3 active members ARE
        // eligible too -> denominator 4, 3 retained (the actives) -> 75%.
        // Unchanged from pre-fix behavior for this specific milestone.
        Assert.Equal(75.0, point.Rates["6 Months"]);

        // 1 Year (365d): 200 elapsed < 365, so only the resigned employee has a
        // decided outcome -> denominator 1 (not 4), and they were NOT retained
        // -> 0%. Pre-fix this incorrectly read 75% (3 of 4 "retained") because
        // the 3 undecided active members were wrongly counted as retained.
        Assert.Equal(0.0, point.Rates["1 Year"]);
    }
}

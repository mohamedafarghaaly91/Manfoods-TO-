using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>
/// Admin-configurable severity-band cutoffs (Settings page) — how many
/// distinct fired signals make an Active plan Medium/High/Critical. Single
/// row (Id = 1), seeded lazily with the same values that used to be
/// hardcoded in StoreActionPlanService.ComputeSeverity so behavior doesn't
/// change until an Admin explicitly edits it. Every save is also appended to
/// action_plan_severity_band_history for audit purposes. Severity is always
/// computed live from whatever config is current (see ComputeSeverity), so a
/// change here immediately re-classifies every open and historical plan's
/// displayed severity — it never rewrites stored data.
/// </summary>
public class ActionPlanSeverityConfigService : IActionPlanSeverityConfigService
{
    private readonly AppDbContext _db;
    public ActionPlanSeverityConfigService(AppDbContext db) { _db = db; }

    public async Task<ActionPlanSeverityBandConfig> GetAsync()
    {
        var config = await _db.ActionPlanSeverityBandConfigs.FindAsync(1);
        if (config != null) return config;

        // Lazily seed with the original hardcoded defaults on first read.
        config = new ActionPlanSeverityBandConfig
        {
            Id = 1,
            MediumMinSignals = 1,
            HighMinSignals = 2,
            CriticalMinSignals = 3,
        };
        _db.ActionPlanSeverityBandConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    public async Task<(bool success, string message)> SaveAsync(int mediumMinSignals, int highMinSignals, int criticalMinSignals, string updatedByName)
    {
        if (mediumMinSignals < 1 || highMinSignals < 1 || criticalMinSignals < 1)
            return (false, "Cutoffs must be at least 1.");
        if (!(mediumMinSignals <= highMinSignals && highMinSignals <= criticalMinSignals))
            return (false, "Cutoffs must be non-decreasing: Medium ≤ High ≤ Critical.");

        var config = await GetAsync();
        config.MediumMinSignals = mediumMinSignals;
        config.HighMinSignals = highMinSignals;
        config.CriticalMinSignals = criticalMinSignals;
        config.UpdatedByName = updatedByName;
        config.UpdatedAt = DateTime.UtcNow;

        _db.ActionPlanSeverityBandHistories.Add(new ActionPlanSeverityBandHistory
        {
            MediumMinSignals = mediumMinSignals,
            HighMinSignals = highMinSignals,
            CriticalMinSignals = criticalMinSignals,
            ChangedByName = updatedByName,
            ChangedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();
        return (true, "Saved.");
    }

    public async Task<List<ActionPlanSeverityBandHistory>> GetHistoryAsync() =>
        await _db.ActionPlanSeverityBandHistories.OrderByDescending(h => h.ChangedAt).Take(50).ToListAsync();
}

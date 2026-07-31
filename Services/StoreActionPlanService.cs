using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Detects turnover problems per store from the existing analytics services,
/// creates/updates a single active StoreActionPlan per store with fixed-template
/// recommendations, and auto-resolves it once healthy for 2 consecutive monthly
/// cycles. No tasks, no approvals — managers only add notes on top of what the
/// system already generated.
/// </summary>
public class StoreActionPlanService : IStoreActionPlanService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IStoreService _stores;
    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly IExitInterviewService _exitInterview;

    // Detection runs system-wide across every store regardless of who's logged
    // in, so it always calls the underlying analytics services unrestricted.
    private const string SystemRole = "Admin";

    // ── Signal thresholds — first-pass defaults, tune after real data review ──
    private const int MinHeadcountForRateSignals = 5;
    private const double OverallTurnoverRateThreshold = 15.0;
    private const int MinHiresForEarlyLeaverSignal = 3;
    private const double EarlyLeaverRateThreshold = 30.0;
    private const int MinHiresForRetentionSignal = 3;
    private const double SixMonthRetentionThreshold = 70.0;
    private const int MinExitResponsesForSentimentSignal = 2;
    private const double ExitSentimentPositiveThreshold = 50.0;
    private const int MinExitResponsesForReasonSignal = 2;
    private const double ReasonConcentrationShareThreshold = 0.5;
    private const int LeadershipChangeLookbackMonths = 6;
    private const int LeadershipChangeCountThreshold = 2;
    private const int EarlyWarningHighRiskThreshold = 2;
    private const int HealthyCyclesRequiredToResolve = 2;

    public StoreActionPlanService(
        AppDbContext db,
        IStoreAccessService storeAccess,
        IStoreService stores,
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IEarlyWarningService earlyWarning,
        IExitInterviewService exitInterview)
    {
        _db = db;
        _storeAccess = storeAccess;
        _stores = stores;
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _earlyWarning = earlyWarning;
        _exitInterview = exitInterview;
    }

    // ────────────────────────────── Read APIs ──────────────────────────────

    public async Task<StoreActionPlanDto?> GetForStoreAsync(string storeName, string role, string? email)
    {
        if (!await _storeAccess.CanAccessStoreAsync(role, email, storeName)) return null;

        var plan = await _db.StoreActionPlans
            .Where(p => p.StoreName == storeName)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var responsible = await _storeAccess.GetResponsiblePartyAsync(storeName);
        var canAddNotes = role is "Head_Manager" or "Operation_Consultant";

        if (plan == null)
        {
            return new StoreActionPlanDto
            {
                StoreName = storeName,
                Status = "None",
                ResponsibleParty = responsible,
                CanAddNotes = canAddNotes,
            };
        }

        var recommendations = await _db.ActionPlanRecommendations
            .Where(r => r.StoreActionPlanId == plan.Id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        var notes = await _db.ActionPlanNotes
            .Where(n => n.StoreActionPlanId == plan.Id)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

        return new StoreActionPlanDto
        {
            Id = plan.Id,
            StoreName = plan.StoreName,
            Status = plan.Status,
            CreatedAt = plan.CreatedAt,
            CreatedMonth = plan.CreatedMonth,
            CreatedYear = plan.CreatedYear,
            ResolvedAt = plan.ResolvedAt,
            ResolvedReason = plan.ResolvedReason,
            BaselineTurnoverRate = plan.BaselineTurnoverRate,
            BaselineEarlyLeaverRate = plan.BaselineEarlyLeaverRate,
            BaselineRetentionRate = plan.BaselineRetentionRate,
            DetectedIssuesSummary = plan.DetectedIssuesSummary,
            HealthyStreakCount = plan.HealthyStreakCount,
            ResponsibleParty = responsible,
            CanAddNotes = canAddNotes,
            Recommendations = recommendations.Select(r => new ActionPlanRecommendationDto
            {
                SignalCode = r.SignalCode,
                Category = r.Category,
                RecommendationText = r.RecommendationText,
                CreatedAt = r.CreatedAt,
            }).ToList(),
            Notes = notes.Select(n => new ActionPlanNoteDto
            {
                AuthorName = n.AuthorName,
                AuthorRole = n.AuthorRole,
                NoteText = n.NoteText,
                CreatedAt = n.CreatedAt,
            }).ToList(),
        };
    }

    public async Task<List<StoreActionPlanSummaryDto>> GetAccessibleStoresWithStatusAsync(string role, string? email)
    {
        var storeRefs = await _stores.GetStoresAsync(null, null, role, email);
        var storeNames = storeRefs.Select(s => s.StoreName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (storeNames.Count == 0) return new List<StoreActionPlanSummaryDto>();

        var latestPeriod = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();

        var latestRefsByStore = latestPeriod == null
            ? new Dictionary<string, StoreReference>(StringComparer.OrdinalIgnoreCase)
            : (await _db.StoreReferences
                .Where(s => s.Month == latestPeriod.Month && s.Year == latestPeriod.Year && storeNames.Contains(s.StoreName))
                .ToListAsync())
                .GroupBy(s => s.StoreName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var plansByStore = (await _db.StoreActionPlans
                .Where(p => storeNames.Contains(p.StoreName))
                .ToListAsync())
            .GroupBy(p => p.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<StoreActionPlanSummaryDto>();
        foreach (var storeName in storeNames)
        {
            plansByStore.TryGetValue(storeName, out var plan);
            latestRefsByStore.TryGetValue(storeName, out var reference);
            var responsible = reference != null ? _storeAccess.ResolveResponsible(reference) : null;

            result.Add(new StoreActionPlanSummaryDto
            {
                StoreName = storeName,
                PlanStatus = plan?.Status ?? "None",
                PlanId = plan?.Id,
                ResponsibleName = responsible?.Name,
                ResponsibleRole = responsible?.Role,
            });
        }
        return result;
    }

    // ────────────────────────────── Notes (write) ──────────────────────────────

    public async Task<(bool success, string message, ActionPlanNoteDto? note)> AddNoteAsync(
        string storeName, string role, string? email, int authorUserId, string authorName, string noteText)
    {
        if (string.IsNullOrWhiteSpace(noteText)) return (false, "Note text is required.", null);
        if (role is not ("Head_Manager" or "Operation_Consultant")) return (false, "Not permitted to add notes.", null);
        if (!await _storeAccess.CanAccessStoreAsync(role, email, storeName)) return (false, "Not permitted to add notes.", null);

        var plan = await _db.StoreActionPlans
            .Where(p => p.StoreName == storeName)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (plan == null) return (false, "No action plan exists for this store yet.", null);

        var note = new ActionPlanNote
        {
            StoreActionPlanId = plan.Id,
            AuthorUserId = authorUserId,
            AuthorName = authorName,
            AuthorRole = role,
            NoteText = noteText.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.ActionPlanNotes.Add(note);
        await _db.SaveChangesAsync();

        return (true, "Note added.", new ActionPlanNoteDto
        {
            AuthorName = note.AuthorName,
            AuthorRole = note.AuthorRole,
            NoteText = note.NoteText,
            CreatedAt = note.CreatedAt,
        });
    }

    // ────────────────────────────── Detection ──────────────────────────────

    public async Task RunDetectionForPeriodAsync(int month, int year)
    {
        var storeNames = await _db.StoreReferences
            .Where(s => s.Month == month && s.Year == year)
            .Select(s => s.StoreName)
            .Distinct()
            .ToListAsync();

        foreach (var storeName in storeNames)
        {
            await EvaluateStoreAsync(storeName, month, year);
        }
    }

    private async Task EvaluateStoreAsync(string storeName, int month, int year)
    {
        var existing = await _db.StoreActionPlans
            .FirstOrDefaultAsync(p => p.StoreName == storeName && p.Status == "Active");

        // Already evaluated this exact period for this plan — re-running detection
        // (e.g. after a corrective single-file re-upload) must not double-count the
        // monthly cycle toward auto-resolution or re-add the same signals.
        if (existing != null && existing.LastEvaluatedMonth == month && existing.LastEvaluatedYear == year)
        {
            return;
        }

        var metrics = await ComputeSignalsAsync(storeName, month, year);

        if (existing == null)
        {
            if (metrics.Signals.Count == 0) return;

            var plan = new StoreActionPlan
            {
                StoreName = storeName,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                CreatedMonth = month,
                CreatedYear = year,
                BaselineTurnoverRate = metrics.TurnoverRate,
                BaselineEarlyLeaverRate = metrics.EarlyLeaverRate,
                BaselineRetentionRate = metrics.RetentionRate,
                DetectedIssuesSummary = string.Join("\n", metrics.Signals.Select(s => s.Evidence)),
                HealthyStreakCount = 0,
                LastEvaluatedMonth = month,
                LastEvaluatedYear = year,
            };
            _db.StoreActionPlans.Add(plan);
            await _db.SaveChangesAsync();

            AddRecommendations(plan.Id, metrics.Signals);
            await _db.SaveChangesAsync();
            return;
        }

        var existingCodes = (await _db.ActionPlanRecommendations
            .Where(r => r.StoreActionPlanId == existing.Id)
            .Select(r => r.SignalCode)
            .Distinct()
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // New problems surfacing while a plan is already active are folded into
        // it — only one active plan per store, so this is how "multiple detected
        // problems grouped into the same plan" is satisfied over time, not just
        // at creation.
        var newSignals = metrics.Signals.Where(s => !existingCodes.Contains(s.Code)).ToList();
        if (newSignals.Count > 0)
        {
            AddRecommendations(existing.Id, newSignals);
            var newEvidence = string.Join("\n", newSignals.Select(s => s.Evidence));
            existing.DetectedIssuesSummary = string.IsNullOrEmpty(existing.DetectedIssuesSummary)
                ? newEvidence
                : existing.DetectedIssuesSummary + "\n" + newEvidence;
        }

        if (metrics.Signals.Count == 0)
        {
            existing.HealthyStreakCount += 1;
            if (existing.HealthyStreakCount >= HealthyCyclesRequiredToResolve)
            {
                existing.Status = "Resolved";
                existing.ResolvedAt = DateTime.UtcNow;
                existing.ResolvedReason = "Auto_Improvement";
            }
        }
        else
        {
            existing.HealthyStreakCount = 0;
        }

        existing.LastEvaluatedMonth = month;
        existing.LastEvaluatedYear = year;

        await _db.SaveChangesAsync();
    }

    private void AddRecommendations(int planId, List<FiredSignal> signals)
    {
        foreach (var signal in signals)
        {
            foreach (var text in signal.RecommendationTexts)
            {
                _db.ActionPlanRecommendations.Add(new ActionPlanRecommendation
                {
                    StoreActionPlanId = planId,
                    SignalCode = signal.Code,
                    Category = signal.Category,
                    RecommendationText = text,
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
    }

    // ────────────────────────────── Signal evaluation ──────────────────────────────

    private class FiredSignal
    {
        public string Code { get; set; } = "";
        public string Category { get; set; } = "";
        public string Evidence { get; set; } = "";
        public List<string> RecommendationTexts { get; set; } = new();
    }

    private class PeriodMetrics
    {
        public double? TurnoverRate;
        public double? EarlyLeaverRate;
        public double? RetentionRate;
        public List<FiredSignal> Signals { get; } = new();
    }

    private async Task<PeriodMetrics> ComputeSignalsAsync(string storeName, int month, int year)
    {
        var metrics = new PeriodMetrics();

        // Overall turnover rate.
        var kpi = await _dashboard.GetKpisAsync(month, year, storeName, SystemRole, null);
        if (kpi.TotalHeadcount > 0)
        {
            metrics.TurnoverRate = kpi.TurnoverRate;
            if (kpi.TotalHeadcount >= MinHeadcountForRateSignals && kpi.TurnoverRate >= OverallTurnoverRateThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "HIGH_OVERALL_TURNOVER",
                    Category = "Retention",
                    Evidence = $"Overall turnover rate {kpi.TurnoverRate:F1}% (headcount {kpi.TotalHeadcount}) is at or above the {OverallTurnoverRateThreshold:F0}% threshold.",
                    RecommendationTexts =
                    {
                        "Review overall staffing stability and scheduling fairness.",
                        "Conduct stay-interviews with current team members.",
                        "Assess workload distribution relative to store volume.",
                    },
                });
            }
        }

        // Early turnover within first 90 days.
        var ninety = await _ninetyDay.GetKpiAsync(month, year, storeName);
        if (ninety.TotalHires > 0)
        {
            metrics.EarlyLeaverRate = ninety.Rate;
            if (!ninety.IsProvisional && ninety.TotalHires >= MinHiresForEarlyLeaverSignal && ninety.Rate >= EarlyLeaverRateThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "EARLY_TURNOVER_90D",
                    Category = "Onboarding",
                    Evidence = $"Early turnover (within 90 days) is {ninety.Rate:F1}% ({ninety.EarlyLeavers} of {ninety.TotalHires} hires).",
                    RecommendationTexts =
                    {
                        "Improve onboarding process for new hires.",
                        "Assign a mentor/buddy and follow up with new hires during their first 90 days.",
                        "Review Store Leader coaching on new-hire integration.",
                    },
                });
            }
        }

        // Retention at the 6-month mark.
        var milestones = await _retention.GetMilestonesAsync(storeName);
        var sixMonth = milestones.FirstOrDefault(m => m.Label == "6 Months");
        if (sixMonth != null && sixMonth.TotalHires > 0)
        {
            metrics.RetentionRate = sixMonth.RetentionRate;
            if (sixMonth.TotalHires >= MinHiresForRetentionSignal && sixMonth.RetentionRate < SixMonthRetentionThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "LOW_RETENTION_6M",
                    Category = "Retention",
                    Evidence = $"6-month retention is {sixMonth.RetentionRate:F1}% ({sixMonth.Retained} of {sixMonth.TotalHires} hires), below the {SixMonthRetentionThreshold:F0}% threshold.",
                    RecommendationTexts =
                    {
                        "Review engagement for employees in the 3-6 month tenure range.",
                        "Check for consistent scheduling and growth opportunities before the 1-year mark.",
                    },
                });
            }
        }

        // Store Leader stability over the trailing window.
        var currentKey = year * 12 + month;
        var cutoffKey = currentKey - (LeadershipChangeLookbackMonths - 1);
        var recentRefs = await _db.StoreReferences
            .Where(s => s.StoreName == storeName && (s.Year * 12 + s.Month) >= cutoffKey && (s.Year * 12 + s.Month) <= currentKey)
            .Select(s => s.StoreLeader)
            .ToListAsync();
        var distinctLeaders = recentRefs.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctLeaders >= LeadershipChangeCountThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "LEADERSHIP_INSTABILITY",
                Category = "Leadership",
                Evidence = $"{distinctLeaders} different Store Leaders recorded in the last {LeadershipChangeLookbackMonths} months.",
                RecommendationTexts =
                {
                    "Review Store Leader coaching and support.",
                    "Ensure operational continuity during leadership transitions.",
                    "Assess whether additional Head Manager oversight is needed.",
                },
            });
        }

        // Exit interview sentiment and dominant resignation reason.
        var filter = new ExitInterviewFilter { Store = storeName };

        var sentiment = await _exitInterview.GetSentimentSummaryAsync(filter, SystemRole, null);
        if (sentiment.TotalResponses >= MinExitResponsesForSentimentSignal && sentiment.PositivePercent < ExitSentimentPositiveThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "LOW_EXIT_SENTIMENT",
                Category = "Culture",
                Evidence = $"Positive exit sentiment is {sentiment.PositivePercent:F1}% across {sentiment.TotalResponses} exit interviews.",
                RecommendationTexts =
                {
                    "Conduct stay-interviews with current team to catch issues early.",
                    "Review recent management/culture incidents reported at exit.",
                },
            });
        }

        var reasons = await _exitInterview.GetReasonsForLeavingAsync(filter, SystemRole, null);
        var totalReasonResponses = reasons.Sum(r => r.Value);
        if (totalReasonResponses >= MinExitResponsesForReasonSignal)
        {
            var top = reasons.OrderByDescending(r => r.Value).FirstOrDefault();
            if (top != null && top.Value / (double)totalReasonResponses >= ReasonConcentrationShareThreshold)
            {
                var (category, recs) = MapReasonToRecommendations(top.Label);
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "REASON_CONCENTRATION",
                    Category = category,
                    Evidence = $"\"{top.Label}\" is cited in {top.Value} of {totalReasonResponses} exit interviews ({top.Value / (double)totalReasonResponses:P0}).",
                    RecommendationTexts = recs,
                });
            }
        }

        // Early-warning watchlist (currently active, at-risk employees).
        var watchlist = await _earlyWarning.GetSummaryAsync(storeName);
        if (watchlist.HighRiskCount >= EarlyWarningHighRiskThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "EARLY_WARNING_WATCHLIST",
                Category = "Monitoring",
                Evidence = $"{watchlist.HighRiskCount} currently active employees are flagged high-risk on the early-warning watchlist.",
                RecommendationTexts =
                {
                    "Proactively check in with employees flagged as at-risk.",
                    "Review the early-warning watchlist with the Store Leader.",
                },
            });
        }

        // Honest fallback: turnover is elevated but nothing more specific explains it.
        if (metrics.Signals.Any(s => s.Code == "HIGH_OVERALL_TURNOVER") &&
            !metrics.Signals.Any(s => s.Code is "EARLY_TURNOVER_90D" or "LEADERSHIP_INSTABILITY" or "REASON_CONCENTRATION" or "LOW_EXIT_SENTIMENT"))
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "NO_DOMINANT_DRIVER",
                Category = "General",
                Evidence = "Turnover is elevated but no specific driver stands out from the available data.",
                RecommendationTexts =
                {
                    "No single cause stands out — recommend a manual review of recent exits with the Store Leader.",
                },
            });
        }

        return metrics;
    }

    private static (string Category, List<string> Recommendations) MapReasonToRecommendations(string label)
    {
        var l = (label ?? "").ToLowerInvariant();

        if (l.Contains("pay") || l.Contains("compensat") || l.Contains("salary") || l.Contains("wage"))
            return ("Compensation", new List<string>
            {
                "Review pay competitiveness against the local market.",
                "Check for pay-equity issues within the store.",
            });

        if (l.Contains("workload") || l.Contains("hour") || l.Contains("schedul") || l.Contains("pressure"))
            return ("Workload", new List<string>
            {
                "Review staffing levels against sales volume.",
                "Audit shift scheduling fairness.",
            });

        if (l.Contains("treat") || l.Contains("manage") || l.Contains("respect") || l.Contains("fair"))
            return ("Management", new List<string>
            {
                "Coach the Store Leader on people management.",
                "Review recent complaints or incidents.",
            });

        if (l.Contains("train") || l.Contains("onboard"))
            return ("Onboarding", new List<string>
            {
                "Improve onboarding process for new hires.",
                "Review Store Leader coaching on new-hire integration.",
            });

        return ("Other", new List<string>
        {
            "Review the dominant resignation reason with the Store Leader and plan a targeted response.",
        });
    }
}

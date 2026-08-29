namespace MvcApp.Services;

public class BackgroundJobStatus
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Status { get; set; } = "Running"; // Running | Succeeded | Failed
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// In-memory tracker for fire-and-forget background work (e.g. action-plan
/// detection after a Data Management upload) so the admin UI can show a
/// GitHub-Actions-style running/succeeded/failed status. Deliberately not
/// persisted — jobs are short-lived and a process restart mid-job means the
/// job needs re-running anyway.
/// </summary>
public interface IBackgroundJobTracker
{
    string Start(string label);
    void Succeed(string id);
    void Fail(string id, string error);
    void Dismiss(string id);
    List<BackgroundJobStatus> GetRecent(int count = 20);
}

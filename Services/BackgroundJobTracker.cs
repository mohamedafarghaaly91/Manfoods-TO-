using System.Collections.Concurrent;

namespace MvcApp.Services;

public class BackgroundJobTracker : IBackgroundJobTracker
{
    private const int MaxJobs = 50;

    private readonly ConcurrentDictionary<string, BackgroundJobStatus> _jobs = new();
    private readonly object _orderLock = new();
    private readonly List<string> _order = new();

    public string Start(string label)
    {
        var id = Guid.NewGuid().ToString("N");
        _jobs[id] = new BackgroundJobStatus { Id = id, Label = label, Status = "Running", StartedAt = DateTime.UtcNow };

        lock (_orderLock)
        {
            _order.Add(id);
            while (_order.Count > MaxJobs)
            {
                var oldest = _order[0];
                _order.RemoveAt(0);
                _jobs.TryRemove(oldest, out _);
            }
        }

        return id;
    }

    public void Succeed(string id)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Status = "Succeeded";
            job.FinishedAt = DateTime.UtcNow;
        }
    }

    public void Fail(string id, string error)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Status = "Failed";
            job.FinishedAt = DateTime.UtcNow;
            job.ErrorMessage = error;
        }
    }

    public void Dismiss(string id)
    {
        lock (_orderLock)
        {
            _order.Remove(id);
        }
        _jobs.TryRemove(id, out _);
    }

    public List<BackgroundJobStatus> GetRecent(int count = 20)
    {
        lock (_orderLock)
        {
            return _order.AsEnumerable().Reverse().Take(count).Select(id => _jobs[id]).ToList();
        }
    }
}

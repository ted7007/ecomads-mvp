using System.Collections.Concurrent;

namespace Ecomads.WebApplication.Services;

public interface IStatisticsQueue
{
    void Enqueue(StatisticsJob job);
    StatisticsJob Dequeue(CancellationToken cancellationToken);
}

public class StatisticsQueue : IStatisticsQueue
{
    private readonly BlockingCollection<StatisticsJob> _queue = new();

    public void Enqueue(StatisticsJob job) => _queue.Add(job);

    public StatisticsJob Dequeue(CancellationToken cancellationToken) => _queue.Take(cancellationToken);
}

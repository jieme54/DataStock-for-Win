using System.Windows.Threading;

namespace DataStock.Windows.Services;

public sealed class Scheduler
{
    private readonly DataStore store;
    private readonly DispatcherTimer timer;

    public Scheduler(DataStore store)
    {
        this.store = store;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Stop();
        timer.Start();
        Tick();
    }

    public void Stop()
    {
        timer.Stop();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach (var job in store.Jobs.Where(job => job.CanRun).ToArray())
        {
            var runKey = job.Schedule.RunKey(now);
            if (runKey is null || job.LastScheduledRunKey == runKey)
            {
                continue;
            }

            job.LastScheduledRunKey = runKey;
            store.Run(job.Id, job.Schedule.RunMode, runKey);
        }
    }
}

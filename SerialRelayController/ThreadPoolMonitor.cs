namespace SerialRelayController;

public class ThreadPoolMonitor(ILogger<ThreadPoolMonitor> logger) : IHostedService, IDisposable
{
    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ =>
        {
            ThreadPool.GetAvailableThreads(out var worker, out var io);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
            logger.LogInformation(
                "ThreadPool: {WorkerThreads}/{MaxWorker} workers free, {CompletionPortThreads}/{MaxIo} IO free",
                worker,
                maxWorker,
                io,
                maxIo);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
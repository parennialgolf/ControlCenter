using System.Diagnostics;

namespace SerialRelayController;

public class ResourceMonitor : BackgroundService
{
    private readonly int _pid = Environment.ProcessId;
    private readonly Process _process = Process.GetCurrentProcess();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _process.Refresh();

                var workingSetMb = _process.WorkingSet64 / (1024 * 1024); // RAM in MB
                var privateMb = _process.PrivateMemorySize64 / (1024 * 1024);

                // CPU usage is a little trickier — sample delta over time
                var startTime = _process.TotalProcessorTime;
                await Task.Delay(1000, stoppingToken);
                _process.Refresh();
                var endTime = _process.TotalProcessorTime;

                var cpuUsedMs = (endTime - startTime).TotalMilliseconds;
                var cpuUsagePct = cpuUsedMs / (Environment.ProcessorCount * 1000.0) * 100.0;

                Console.WriteLine(
                    $"📊 Resource usage (PID {_pid}): CPU {cpuUsagePct:F1}% | RAM {workingSetMb} MB (private {privateMb} MB)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Resource monitor failed: {ex.Message}");
            }

            // Log once per minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
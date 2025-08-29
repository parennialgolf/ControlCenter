using System.Runtime.InteropServices;

namespace SerialRelayController;

public class SystemdWatchdog(PortController ports) : BackgroundService
{
    [DllImport("libsystemd.so.0", EntryPoint = "sd_notify", CharSet = CharSet.Ansi)]
    private static extern int sd_notify(int unsetEnv, string state);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Tell systemd we're fully initialized
        sd_notify(0, "READY=1");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Health check: require at least one active serial port
                var healthy = ports
                    .GetActivePorts()
                    .Any(p => p.IsOpen);

                if (healthy)
                {
                    // Heartbeat to systemd
                    sd_notify(0, "WATCHDOG=1");
                }
                else
                {
                    Console.WriteLine("⚠️ No healthy serial ports detected, triggering panic exit...");
                    Environment.Exit(1); // panic: systemd restarts us
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Watchdog check failed: {ex.Message}");
                Environment.Exit(1);
            }

            // Must be < WatchdogSec in service file (we set 30s → 10s is safe)
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
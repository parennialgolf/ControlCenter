using System.Runtime.InteropServices;

namespace SerialRelayController;

public partial class SystemdWatchdog(PortController ports) : BackgroundService
{
    [LibraryImport("libsystemd.so.0", EntryPoint = "sd_notify")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial int sd_notify(int unsetEnv,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string state);

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
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text.Json.Serialization;

namespace SerialRelayController;

public record LockerRelayStatus(int LockerNumber, bool IsOn);

public record GetRelayResult(string SerialPort, int Channel);

public record SerialCommandResult(
    bool Success,
    string? StatusResponse = null,
    string? Error = null);

public class Command
{
    [JsonPropertyName("on")] public string On { get; init; } = string.Empty;
    [JsonPropertyName("off")] public string Off { get; init; } = string.Empty;
}

public record UnlockDuration(int Delay);

public sealed class PortController(SerialPorts ports, LockerStateCache cache) : IDisposable
{
    private readonly ConcurrentDictionary<string, SerialPort> _portCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _portLocks = new();

    private bool _disposed;

    private SerialPort GetOrCreatePort(string portPath)
    {
        return _portCache.GetOrAdd(portPath, path =>
        {
            var sp = new SerialPort(path, 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                NewLine = "\r\n"
            };

            try
            {
                sp.Open();
                Console.WriteLine($"✅ Opened serial port {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to open {path}: {ex.Message}");
                throw;
            }

            return sp;
        });
    }

    private SemaphoreSlim GetLock(string portPath) =>
        _portLocks.GetOrAdd(portPath, _ => new SemaphoreSlim(1, 1));

    public async Task<SerialCommandResult> Unlock(
        int lockerNumber,
        LockerUnlockRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var relay = ports.GetRelay(lockerNumber, request.SerialPorts);
            var result = await SendToSerialWithConfirmation(relay.SerialPort, relay.Channel, isUnlock: true, ct: ct);

            // Optimistically mark unlocked
            cache.MarkUnlocked(lockerNumber);

            // Schedule relock
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(request.Duration), ct);

                    cache.MarkLocked(lockerNumber);
                    var relockResult = await Lock(lockerNumber, request.SerialPorts, ct);

                    Console.WriteLine(relockResult.Success
                        ? $"🔒 Relocked locker {lockerNumber} after {request.Duration}s"
                        : $"⚠️ Failed to relock locker {lockerNumber}: {relockResult.Error}");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("⚠️ Relock task canceled");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Relock task failed: {ex.Message}");
                }
            }, ct);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new SerialCommandResult(false, Error: "Unlock canceled by request");
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<SerialCommandResult> Lock(
        int lockerNumber,
        List<string> serialPorts,
        CancellationToken ct = default)
    {
        try
        {
            var relay = ports.GetRelay(lockerNumber, serialPorts);
            var result = await SendToSerialWithConfirmation(relay.SerialPort, relay.Channel, isUnlock: false, ct: ct);

            if (result.Success)
            {
                cache.MarkLocked(lockerNumber);
                Console.WriteLine($"✅ Relocked locker {lockerNumber} on {relay.SerialPort}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return new SerialCommandResult(false, Error: "Lock canceled by request");
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"⚠️ Failed to relock locker {lockerNumber}: {ex.Message}");
        }
    }

    public async Task<SerialCommandResult> SendToSerialWithConfirmation(
        string portPath,
        int channel,
        bool isUnlock,
        int responseDelayMs = 200,
        int maxOperationMs = 2000,
        CancellationToken ct = default)
    {
        var sem = GetLock(portPath);
        await sem.WaitAsync(ct);

        try
        {
            var command = ports.GetCommand(channel);
            if (command == null)
                return new SerialCommandResult(false, Error: $"No command defined for channel {channel}");

            var cmd = (isUnlock ? command.On : command.Off)
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");

            var port = GetOrCreatePort(portPath);

            // Write with cancellation
            await Task.Run(() => port.WriteLine(cmd), ct);

            await Task.Delay(responseDelayMs, ct);

            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            opCts.CancelAfter(maxOperationMs);

            var response = await ReadLineWithCancelAsync(port, opCts.Token);

            if (string.IsNullOrWhiteSpace(response))
                return new SerialCommandResult(true, StatusResponse: "No response (assumed success)");

            var normalized = response.Trim();

            if (normalized.Contains("ERR", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
                return new SerialCommandResult(false, normalized, "Relay reported failure");

            if (normalized.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(cmd.Trim(), StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("ON", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("OFF", StringComparison.OrdinalIgnoreCase))
                return new SerialCommandResult(true, normalized);

            return new SerialCommandResult(true, normalized, "Unexpected response, assumed success");
        }
        catch (OperationCanceledException)
        {
            return new SerialCommandResult(false, Error: "Operation canceled due to timeout or request abort");
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Serial communication error: {ex.Message}");
        }
        finally
        {
            sem.Release();
        }
    }

    private static async Task<string> ReadLineWithCancelAsync(SerialPort port, CancellationToken ct)
    {
        var task = Task.Run(() =>
        {
            try
            {
                return port.ReadLine();
            }
            catch (TimeoutException)
            {
                return string.Empty;
            }
        }, ct);

        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, ct));
        if (completed == task)
            return await task;

        throw new OperationCanceledException(ct);
    }

    // --- Proper Dispose pattern ---
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            foreach (var kv in _portCache)
            {
                try
                {
                    kv.Value.Dispose();
                    Console.WriteLine($"✅ Closed serial port {kv.Key}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to close {kv.Key}: {ex.Message}");
                }
            }
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
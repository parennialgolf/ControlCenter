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

public class PortController(
    SerialPorts ports,
    LockerStateCache cache)
{
    /// <summary>
    /// High-level Unlock method:
    /// 1. Map locker → board/channel
    /// 2. Send ON command and confirm
    /// 3. Update cache
    /// 4. Schedule relock job with Quartz
    /// </summary>
    public async Task<SerialCommandResult> Unlock(int lockerNumber, LockerUnlockRequest request)
    {
        try
        {
            var relay = ports.GetRelay(lockerNumber, request.SerialPorts);
            if (!File.Exists(relay.SerialPort))
                return new SerialCommandResult(false, Error: $"Port {relay.SerialPort} not found");

            // Try to send unlock
            var result = await SendToSerialWithConfirmation(relay.SerialPort, relay.Channel, isUnlock: true);

            // Always mark unlocked in cache (optimistic)
            cache.MarkUnlocked(lockerNumber);

            // Always schedule relock — even if relay didn’t confirm
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(request.Duration));

                    // Update cache back to locked
                    cache.MarkLocked(lockerNumber);

                    // Attempt relock regardless of initial result
                    var relockResult = await Lock(lockerNumber, request.SerialPorts);

                    if (relockResult.Success)
                    {
                        Console.WriteLine(
                            $"🔒 Automatically relocked locker {lockerNumber} after {request.Duration} seconds.");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"⚠️ Failed to relock locker {lockerNumber} after {request.Duration} seconds: {relockResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Relock task failed: {ex.Message}");
                }
            });

            Console.WriteLine(result.Success
                ? $"✅ Successfully unlocked locker {lockerNumber} on {relay.SerialPort}"
                : $"⚠️ Unlock attempted but not confirmed for locker {lockerNumber} on {relay.SerialPort}");

            return result;
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
        }
    }


    /// <summary>
    /// High-level Lock method:
    /// 1. Map locker → board/channel
    /// 2. Send OFF command and confirm
    /// 3. Update cache
    /// </summary>
    public async Task<SerialCommandResult> Lock(int lockerNumber, List<string> serialPorts)
    {
        try
        {
            var relay = ports.GetRelay(lockerNumber, serialPorts);
            if (!File.Exists(relay.SerialPort))
                return new SerialCommandResult(false, Error: $"Port {relay.SerialPort} not found");

            var result = await SendToSerialWithConfirmation(relay.SerialPort, relay.Channel, isUnlock: false);

            if (!result.Success)
                return result;

            cache.MarkLocked(lockerNumber);
            Console.WriteLine($"✅ Relocked locker {lockerNumber} on {relay.SerialPort}");

            return result;
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"⚠️ Failed to relock locker {lockerNumber}: {ex.Message}");
        }
    }

    private async Task<SerialCommandResult> SendToSerialWithConfirmation(
        string portPath,
        int channel,
        bool isUnlock,
        int responseDelayMs = 200,
        int maxReadWindowMs = 1000)
    {
        try
        {
            var command = ports.GetCommand(channel);
            if (command == null)
                return new SerialCommandResult(false, Error: $"No command defined for channel {channel}.");

            var cmd = (isUnlock ? command.On : command.Off)
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");

            using var serialPort = new SerialPort(portPath, 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = maxReadWindowMs,
                WriteTimeout = 1000,
                NewLine = "\r\n"
            };

            try
            {
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                // Send command + flush newline
                serialPort.WriteLine(cmd);

                // Give device time to respond before we start reading
                await Task.Delay(responseDelayMs);

                // Collect all available bytes in the read window
                var deadline = DateTime.UtcNow.AddMilliseconds(maxReadWindowMs);
                string response = "";

                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        response += serialPort.ReadExisting();
                    }
                    catch (TimeoutException)
                    {
                        break; // nothing new, stop
                    }

                    if (!string.IsNullOrWhiteSpace(response))
                        break; // got something, stop early

                    await Task.Delay(50); // small poll delay
                }

                // --- Evaluate result ---
                if (string.IsNullOrWhiteSpace(response))
                {
                    // Fail-safe: no response but assume command worked
                    return new SerialCommandResult(true, StatusResponse: "No response (assumed success)");
                }

                var normalized = response.Trim();

                // Detect explicit failure
                if (normalized.Contains("ERR", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
                {
                    return new SerialCommandResult(false, StatusResponse: normalized, Error: "Relay reported failure");
                }

                // Detect common positive replies
                if (normalized.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(cmd.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("ON", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("OFF", StringComparison.OrdinalIgnoreCase))
                {
                    return new SerialCommandResult(true, StatusResponse: normalized);
                }

                // If it’s something else, assume success but log mismatch
                return new SerialCommandResult(
                    true,
                    StatusResponse: normalized,
                    Error: "Unexpected response format, assumed success"
                );
            }
            catch (Exception ex)
            {
                return new SerialCommandResult(false, Error: $"Serial error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
        }
    }
}
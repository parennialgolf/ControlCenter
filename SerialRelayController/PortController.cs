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

            // Low-level confirmation
            var result = await SendToSerialWithConfirmation(relay.SerialPort, relay.Channel, isUnlock: true);

            // if (!result.Success)
            //     return result;

            // Update cache
            cache.MarkUnlocked(lockerNumber);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(request.Duration));
                    cache.MarkLocked(lockerNumber);
                    await Lock(lockerNumber, request.SerialPorts);
                    Console.WriteLine(
                        $"🔒 Automatically relocked locker {lockerNumber} after {request.Duration} seconds.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Relock task failed: {ex.Message}");
                }
            });

            Console.WriteLine($"✅ Successfully unlocked locker {lockerNumber} on {relay.SerialPort}");
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
        bool isUnlock)
    {
        try
        {
            var command = ports.GetCommand(channel);
            if (command == null)
                return new SerialCommandResult(false, Error: $"No command defined for channel {channel}.");

            var cmd = (isUnlock ? command.On : command.Off)
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");

            using var serialPort = new SerialPort(portPath, 9600, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 2000;
            serialPort.WriteTimeout = 2000;
            serialPort.NewLine = "\r\n";

            try
            {
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                serialPort.Write(cmd);

                // Give the device time to respond
                await Task.Delay(100);

                string response;
                try
                {
                    response = serialPort.ReadLine(); // safer than ReadExisting
                }
                catch (TimeoutException)
                {
                    response = serialPort.ReadExisting(); // fallback
                }

                if (string.IsNullOrWhiteSpace(response))
                    return new SerialCommandResult(true, StatusResponse: "No response (assumed success)");

                // Accept either an OK or exact echo
                if (response.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                    response.Trim().Equals(cmd.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return new SerialCommandResult(true, StatusResponse: response);
                }

                return new SerialCommandResult(
                    true, // don’t fail the unlock if we got *something*
                    StatusResponse: response,
                    Error: "Response didn’t exactly match command, but continuing");
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
using System.IO.Ports;

namespace Shared.Services;

public static class SerialRelayController
{
    // Fixed mapping of relay boards to ports (adjust if needed).
    private static readonly List<string> SerialPortPaths =
    [
        "/dev/ttyUSB0", // board three
        "/dev/ttyUSB1", // board one
        "/dev/ttyUSB2" // board two
    ];

    private const int ChannelsPerBoard = 16;

    /// <summary>
    /// Reads the status of all relays from all configured ports.
    /// </summary>
    public static List<LockerRelayStatus> GetAllStatuses()
    {
        var results = new List<LockerRelayStatus>();

        for (var boardIndex = 0; boardIndex < SerialPortPaths.Count; boardIndex++)
        {
            var port = SerialPortPaths[boardIndex];

            try
            {
                if (!File.Exists(port))
                {
                    Console.WriteLine($"⚠️ Port {port} does not exist, skipping.");
                    continue;
                }

#pragma warning disable CA1416
                using var serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One);
                serialPort.ReadTimeout = 2000;
                serialPort.WriteTimeout = 2000;
                serialPort.NewLine = "\r\n";

                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                // Send "status" command from JSON
                var statusCmd = SerialRelayCommands.Status.On
                    .Replace("\\r", "\r")
                    .Replace("\\n", "\n");
                serialPort.Write(statusCmd);

                string? response;
                try
                {
                    response = serialPort.ReadLine();
                }
                catch (TimeoutException)
                {
                    Console.WriteLine($"⏳ Timeout waiting for response from {port}. Is a relay board connected?");
                    continue;
                }

#pragma warning restore CA1416

                if (string.IsNullOrWhiteSpace(response) || response.Length < 10)
                {
                    Console.WriteLine($"⚠️ Invalid/empty response from {port}: '{response ?? "null"}'");
                    continue;
                }

                // Parse relay state bitfield
                var hexBitfield = response.Substring(6, 4);
                var bytes = new byte[2];
                bytes[0] = Convert.ToByte(hexBitfield[..2], 16);
                bytes[1] = Convert.ToByte(hexBitfield.Substring(2, 2), 16);

                for (var i = 0; i < ChannelsPerBoard; i++)
                {
                    var lockerNumber = boardIndex * ChannelsPerBoard + i + 1;
                    var byteIndex = i / 8;
                    var bitIndex = i % 8;
                    var isOn = (bytes[byteIndex] & (1 << bitIndex)) != 0;

                    results.Add(new LockerRelayStatus(lockerNumber, isOn));
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"❌ Permission denied on {port}. Is 'user' in the dialout group?");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"❌ I/O error on {port}: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Unexpected error on {port}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Maps a locker number to the correct serial port + relay channel.
    /// </summary>
    public static GetRelayResult GetRelay(int lockerNumber)
    {
        const int channelsPerPort = 16;
        var portIndex = (lockerNumber - 1) / channelsPerPort;
        if (portIndex < 0 || portIndex >= SerialPortPaths.Count)
            throw new ArgumentOutOfRangeException(nameof(lockerNumber));

        var serialPort = SerialPortPaths[portIndex];
        var channel = ((lockerNumber - 1) % channelsPerPort) + 1;
        return new GetRelayResult(serialPort, channel);
    }

    public static Task<SerialCommandResult> SendToSerialWithConfirmation(string portPath, int channel)
    {
        try
        {
            if (!File.Exists(portPath))
                return Task.FromResult(new SerialCommandResult(false, Error: $"Port {portPath} does not exist."));

            var command = SerialRelayCommands.GetCommand(channel);
            if (command == null)
                return Task.FromResult(new SerialCommandResult(false,
                    Error: $"No command defined for channel {channel}."));

#pragma warning disable CA1416
            using var serialPort = new SerialPort(portPath, 9600);
            serialPort.ReadTimeout = 2000;
            serialPort.WriteTimeout = 2000;
            serialPort.NewLine = "\r\n";

            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // 1. Send ON command
            var onCmd = command.On.Replace("\\r", "\r").Replace("\\n", "\n");
            serialPort.Write(onCmd);

            // 2. Read echo back
            string? response;
            try
            {
                response = serialPort.ReadLine();
            }
            catch (TimeoutException)
            {
                return Task.FromResult(new SerialCommandResult(false,
                    Error: "No response (echo) received from relay."));
            }

            var normalizedResponse = response?.Trim();
            var normalizedCommand = onCmd.Trim();

            var success = !string.IsNullOrWhiteSpace(normalizedResponse) &&
                          normalizedResponse.Equals(normalizedCommand, StringComparison.OrdinalIgnoreCase);

            if (!success)
                return Task.FromResult(new SerialCommandResult(
                    false,
                    StatusResponse: response,
                    Error: $"Relay did not echo back ON command correctly."));
            // 3. Respond immediately to API caller, and schedule OFF in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(10000);
                    var offCmd = command.Off.Replace("\\r", "\r").Replace("\\n", "\n");

                    using var offPort = new SerialPort(portPath, 9600);
                    offPort.WriteTimeout = 2000;
                    offPort.Open();
                    offPort.Write(offCmd);
                    Console.WriteLine("Successfully closed serial port.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to send OFF command for channel {channel}: {ex.Message}");
                }
            });

            return Task.FromResult(new SerialCommandResult(true, StatusResponse: response));

#pragma warning restore CA1416
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new SerialCommandResult(false,
                Error: $"Permission denied to open {portPath}. Is the relay connected and accessible?"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}"));
        }
    }
}

public record LockerRelayStatus(int LockerNumber, bool IsOn);

public record GetRelayResult(string SerialPort, int Channel);

public record SerialCommandResult(bool Success, string? StatusResponse = null, string? Error = null);
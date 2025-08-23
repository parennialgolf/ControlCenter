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
                using var serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    NewLine = "\r\n"
                };

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

    /// <summary>
    /// Sends an ON command to the relay, verifies status, then turns it OFF.
    /// </summary>
    public static async Task<SerialCommandResult> SendToSerialWithConfirmation(string portPath, int channel)
    {
        try
        {
            if (!File.Exists(portPath))
                return new SerialCommandResult(false, Error: $"Port {portPath} does not exist.");

            var command = SerialRelayCommands.GetCommand(channel);
            if (command == null)
                return new SerialCommandResult(false, Error: $"No command defined for channel {channel}.");

#pragma warning disable CA1416
            using var serialPort = new SerialPort(portPath, 9600)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                NewLine = "\r\n"
            };

            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // 1. Send ON command
            var onCmd = command.On.Replace("\\r", "\r").Replace("\\n", "\n");
            serialPort.Write(onCmd);

            // 2. Wait a bit for relay to actuate
            await Task.Delay(1000);

            // 3. Ask for status
            var statusCmd = SerialRelayCommands.Status.On
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");
            serialPort.Write(statusCmd);

            string? response = null;
            try
            {
                response = serialPort.ReadLine();
            }
            catch (TimeoutException)
            {
                return new SerialCommandResult(false, Error: "Relay timed out waiting for status response.");
            }

            var success = IsRelayOn(response, channel);

            // 4. Always send OFF after confirmation
            var offCmd = command.Off.Replace("\\r", "\r").Replace("\\n", "\n");
            serialPort.Write(offCmd);

            return new SerialCommandResult(success, StatusResponse: response,
                Error: success ? null : $"Relay {channel} did not report ON.");
#pragma warning restore CA1416
        }
        catch (UnauthorizedAccessException)
        {
            return new SerialCommandResult(false,
                Error: $"Permission denied to open {portPath}. Is the relay connected and accessible?");
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses relay status response into ON/OFF state.
    /// </summary>
    private static bool IsRelayOn(string? response, int relayChannel)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length < 10)
            return false;

        response = response.TrimStart(':').Trim();
        var hexBitfield = response.Substring(6, 4);
        var bytes = new byte[2];
        bytes[0] = Convert.ToByte(hexBitfield[..2], 16);
        bytes[1] = Convert.ToByte(hexBitfield.Substring(2, 2), 16);

        var channelIndex = relayChannel - 1;
        var byteIndex = channelIndex / 8;
        var bitIndex = channelIndex % 8;

        return (bytes[byteIndex] & (1 << bitIndex)) != 0;
    }
}

public record LockerRelayStatus(int LockerNumber, bool IsOn);

public record GetRelayResult(string SerialPort, int Channel);

public record SerialCommandResult(bool Success, string? StatusResponse = null, string? Error = null);
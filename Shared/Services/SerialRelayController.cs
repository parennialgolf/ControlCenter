using System.IO.Ports;

namespace Shared.Services;

public static class SerialRelayController
{
    // List of serial port paths for the relay boards.
    // These should be updated based on the actual hardware setup.
    // Example: "/dev/ttyUSB0", "/dev/ttyUSB1", etc.    
    private static readonly List<string> SerialPortPaths =
    [
        "/dev/ttyUSB0",
        "/dev/ttyUSB1",
        "/dev/ttyUSB2"
    ];

    private const int ChannelsPerBoard = 16;

    public static List<LockerRelayStatus> GetAllStatuses()
    {
        var results = new List<LockerRelayStatus>();

        for (int boardIndex = 0; boardIndex < SerialPortPaths.Count; boardIndex++)
        {
            var port = SerialPortPaths[boardIndex];
            try
            {
                using var serialPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One);
                serialPort.ReadTimeout = 1000;
                serialPort.WriteTimeout = 1000;

                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                serialPort.Write("00");
                var response = serialPort.ReadLine()?.TrimStart(':').Trim();

                if (string.IsNullOrWhiteSpace(response) || response.Length < 10)
                    continue;

                var hexBitfield = response.Substring(6, 4); // e.g., FF00
                var bytes = new byte[2];
                bytes[0] = Convert.ToByte(hexBitfield.Substring(0, 2), 16);
                bytes[1] = Convert.ToByte(hexBitfield.Substring(2, 2), 16);

                for (int i = 0; i < ChannelsPerBoard; i++)
                {
                    int lockerNumber = boardIndex * ChannelsPerBoard + i + 1;
                    int byteIndex = i / 8;
                    int bitIndex = i % 8;
                    bool isOn = (bytes[byteIndex] & (1 << bitIndex)) != 0;

                    results.Add(new LockerRelayStatus(lockerNumber, isOn));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading status from {port}: {ex.Message}");
            }
        }

        return results;
    }

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

    public static async Task<SerialCommandResult> SendToSerialWithConfirmation(string portPath, int channel)
    {
        return await Task.Run(() =>
        {
            try
            {
                var command = SerialRelayCommands.GetCommand(channel);
                using var serialPort = new SerialPort(portPath, 9600)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                serialPort.Write(command!.On);
                Thread.Sleep(5000);

                serialPort.DiscardInBuffer();
                serialPort.Write(SerialRelayCommands.Status.On);

                var response = serialPort.ReadLine();
                var success = IsRelayOn(response, channel);

                return new SerialCommandResult(success, StatusResponse: response);
            }
            catch (UnauthorizedAccessException)
            {
                return new SerialCommandResult(false,
                    Error: $"Permission denied to open {portPath}. Is the relay connected and accessible?");
            }
            catch (TimeoutException)
            {
                return new SerialCommandResult(false, Error: "Relay timed out waiting for status response.");
            }
            catch (Exception ex)
            {
                return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
            }
        });
    }

    private static bool IsRelayOn(string? response, int relayChannel)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        response = response.TrimStart(':').Trim();
        if (response.Length < 10)
            return false;

        var hexBitfield = response.Substring(6, 4);
        var bytes = new byte[2];
        bytes[0] = Convert.ToByte(hexBitfield.Substring(0, 2), 16);
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
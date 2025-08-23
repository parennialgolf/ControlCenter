using System.Collections.Concurrent;
using System.IO.Ports;
using Quartz;
using SerialRelayController.Jobs;
using Shared;

namespace SerialRelayController;

public class LockerStateCache
{
    private readonly ConcurrentDictionary<int, bool> _states = new();

    public void MarkUnlocked(int lockerNumber)
    {
        _states[lockerNumber] = true;
    }

    public void MarkLocked(int lockerNumber)
    {
        _states[lockerNumber] = false;
    }

    public bool IsUnlocked(int lockerNumber)
    {
        return _states.TryGetValue(lockerNumber, out var state) && state;
    }
}

public class SerialRelayController(
    LockerStateCache cache,
    ISchedulerFactory factory)
{
    // Fixed mapping of relay boards to ports (order matters).
    private static readonly List<string> SerialPortPaths =
    [
        "/dev/ttyUSB0", // board three
        "/dev/ttyUSB1", // board one
        "/dev/ttyUSB2" // board two
    ];

    private const int ChannelsPerBoard = 16;

    public List<LockerRelayStatus> GetAllStatuses()
    {
        var results = new List<LockerRelayStatus>();

        for (var boardIndex = 0; boardIndex < SerialPortPaths.Count; boardIndex++)
        {
            for (var i = 0; i < ChannelsPerBoard; i++)
            {
                var lockerNumber = boardIndex * ChannelsPerBoard + i + 1;
                var isOn = cache.IsUnlocked(lockerNumber);
                results.Add(new LockerRelayStatus(lockerNumber, isOn));
            }
        }

        return results;
    }

    /// <summary>
    /// Maps a locker number to the correct serial port + relay channel.
    /// </summary>
    public static GetRelayResult GetRelay(int lockerNumber)
    {
        var portIndex = (lockerNumber - 1) / ChannelsPerBoard;
        if (portIndex < 0 || portIndex >= SerialPortPaths.Count)
            throw new ArgumentOutOfRangeException(nameof(lockerNumber));

        var serialPort = SerialPortPaths[portIndex];
        var channel = ((lockerNumber - 1) % ChannelsPerBoard) + 1;
        return new GetRelayResult(serialPort, channel);
    }

    /// <summary>
    /// Unlock a locker by sending ON, confirming echo, scheduling OFF,
    /// and updating the soft latch cache.
    /// </summary>
    public async Task<SerialCommandResult> Unlock(int lockerNumber)
    {
        var portIndex = (lockerNumber - 1) / ChannelsPerBoard;
        var channel = (lockerNumber - 1) % ChannelsPerBoard + 1;

        if (portIndex < 0 || portIndex >= SerialPortPaths.Count)
            return new SerialCommandResult(false, Error: "Invalid locker number");

        var portPath = SerialPortPaths[portIndex];
        if (!File.Exists(portPath))
            return new SerialCommandResult(false, Error: $"Port {portPath} not found");

        try
        {
            using var serialPort = new SerialPort(portPath, 9600, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 2000;
            serialPort.WriteTimeout = 2000;
            serialPort.NewLine = "\r\n";
            serialPort.Open();

            // Build ON command for this channel
            var command = SerialRelayCommands.GetCommand(channel);
            if (command == null)
                return new SerialCommandResult(false, Error: $"No command defined for channel {channel}");

            var onCmd = command.On.Replace("\\r", "\r").Replace("\\n", "\n");
            serialPort.Write(onCmd);

            string? response;
            try
            {
                response = serialPort.ReadLine();
            }
            catch (TimeoutException)
            {
                return new SerialCommandResult(false, Error: "No echo received from relay");
            }

            // Success = echoed the same command
            var success = !string.IsNullOrWhiteSpace(response) &&
                          response.Trim().Equals(onCmd.Trim(), StringComparison.OrdinalIgnoreCase);

            if (!success)
                return new SerialCommandResult(
                    false,
                    StatusResponse: response,
                    Error: "Relay did not echo back ON command correctly");


            cache.MarkUnlocked(lockerNumber);

            var scheduler = await factory.GetScheduler();
            var job = LockJob.BuildJob(lockerNumber);
            var trigger = LockJob.BuildTrigger(lockerNumber, 10); // 10 sec delay

            await scheduler.ScheduleJob(job, trigger);

            return new SerialCommandResult(true, StatusResponse: response);
        }
        catch (Exception ex)
        {
            return new SerialCommandResult(false, Error: $"Unexpected error: {ex.Message}");
        }
        finally
        {
            await Task.CompletedTask;
        }
    }
}
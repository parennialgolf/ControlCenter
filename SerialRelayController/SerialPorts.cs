using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SerialRelayController;

public class SerialPorts(
    LockerStateCache cache,
    IOptionsMonitor<SerialPortOptions> options)
{
    private const int ChannelsPerBoard = 16;

    private readonly Lazy<Dictionary<string, Command>> _commands = new(Load);

    private Dictionary<string, Command> Commands => _commands.Value;

    private static Dictionary<string, Command> Load()
    {
        var json = File.ReadAllText("commands.json");
        return JsonSerializer.Deserialize<Dictionary<string, Command>>(json) ?? new Dictionary<string, Command>();
    }

    public Command Status => GetDirectCommand("status")!;

    public Command? AllChannels => GetDirectCommand("all_channels"); // ✅ underscore

    public Command? GetCommand(int relay)
        => GetDirectCommand($"channel_{relay}"); // ✅ underscore

    private Command? GetDirectCommand(string key)
        => Commands.GetValueOrDefault(key);

    public List<LockerRelayStatus> GetAllStatuses()
    {
        var results = new List<LockerRelayStatus>();

        for (var boardIndex = 0; boardIndex < options.CurrentValue.Ports.Count; boardIndex++)
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
    /// <summary>
    /// Maps a locker number to the correct serial port + relay channel.
    /// </summary>
    public GetRelayResult GetRelay(int lockerNumber)
    {
        if (options.CurrentValue.Ports.Count == 0)
            throw new InvalidOperationException(
                "No serial ports configured. Please set SerialPortOptions:Ports in appsettings.json");

        var portIndex = (lockerNumber - 1) / ChannelsPerBoard;
        if (portIndex < 0 || portIndex >= options.CurrentValue.Ports.Count)
            throw new ArgumentOutOfRangeException(nameof(lockerNumber),
                $"Locker {lockerNumber} is out of range. " +
                $"Valid range: 1 - {options.CurrentValue.Ports.Count * ChannelsPerBoard}");

        var serialPort = options.CurrentValue.Ports[portIndex];
        var channel = ((lockerNumber - 1) % ChannelsPerBoard) + 1;
        return new GetRelayResult(serialPort, channel);
    }
}
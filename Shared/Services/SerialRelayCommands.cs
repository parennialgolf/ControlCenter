using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Services;

public static class SerialRelayCommands
{
    private static Dictionary<string, Command> Load()
    {
        var json = File.ReadAllText("commands.json");
        return JsonSerializer.Deserialize<Dictionary<string, Command>>(json) ?? new();
    }

    private static Dictionary<string, Command> Commands => Load();

    public static Command Status => GetDirectCommand("status")!;

    public static Command? AllChannels => GetDirectCommand("all-channels");

    public static Command? GetCommand(int relay)
        => GetDirectCommand($"channel-{relay}");

    private static Command? GetDirectCommand(string key)
        => Commands.GetValueOrDefault(key);
}

public class Command
{
    [JsonPropertyName("off")] public string Off { get; set; } = string.Empty;

    [JsonPropertyName("on")] public string On { get; set; } = string.Empty;
}
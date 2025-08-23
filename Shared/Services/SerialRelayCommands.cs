using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Services;

public static class SerialRelayCommands
{
    private static readonly Lazy<Dictionary<string, Command>> _commands = new(Load);

    private static Dictionary<string, Command> Commands => _commands.Value;

    private static Dictionary<string, Command> Load()
    {
        var json = File.ReadAllText("commands.json");
        return JsonSerializer.Deserialize<Dictionary<string, Command>>(json) ?? new();
    }

    public static Command Status => GetDirectCommand("status")!;

    public static Command? AllChannels => GetDirectCommand("all_channels"); // ✅ underscore

    public static Command? GetCommand(int relay)
        => GetDirectCommand($"channel_{relay}"); // ✅ underscore

    private static Command? GetDirectCommand(string key)
        => Commands.GetValueOrDefault(key);
}

public class Command
{
    [JsonPropertyName("off")] public string Off { get; set; } = string.Empty;
    [JsonPropertyName("on")] public string On { get; set; } = string.Empty;
}
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shared.Services;

public static class ControlByWebRelayCommands
{
    public static Uri On(IPAddress ip) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay1State=1");

    public static Uri Off(IPAddress ip) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay1State=0");

    public static Uri Pulse(IPAddress ip) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay1State=2");

    public static Uri Status(IPAddress ip) =>
        new($"http://admin:webrelay@{ip}/state.json");
}


// "doors": {
//     "managed": true,
//     "max": 1,
//     "relays": [
//     {
//         "id": 1,
//         "host": "192.168.7.200"
//     }
//     ]
// },
public class Doors
{
    public bool Managed { get; set; }
    public int Max { get; set; }
    public List<DoorRelayConfig> Relays { get; set; } = [];
}

public class DoorRelayConfig
{
    public int Id { get; set; }
    public IPAddress IpAddress { get; set; }

    public DoorRelayConfig(int id, IPAddress ipAddress)
    {
        Id = id;
        IpAddress = ipAddress;
    }
}

public static class IpRelayController
{
    private static readonly Dictionary<int, (string On, string Off)> Commands = new()
    {
        { 1, ("11", "21") },
        { 2, ("12", "22") }
    };

    public static async Task<RelayCommandResult> TriggerDoorAsync(int doorNumber)
    {
        if (!Commands.TryGetValue(doorNumber, out var command))
            return RelayCommandResult.FailureResult("Invalid door number.");

        try
        {
            // Immediately return success after ON
            return RelayCommandResult.SuccessResult(doorNumber, onResponse, null);
        }
        catch (Exception ex)
        {
            return RelayCommandResult.FailureResult($"Error: {ex.Message}");
        }
    }

    public static async Task<RelayStatusResult> GetRelayStatusAsync()
    {
        try
        {
            var response = await SendCommandAsync("00"); // status check command
            if (string.IsNullOrWhiteSpace(response))
                return RelayStatusResult.Failure("Empty response from relay.");

            var doors = new List<DoorStatus>();

            for (int i = 1; i <= Commands.Count; i++)
            {
                bool isOpen = IsBitSet(response, i);
                doors.Add(new DoorStatus(i, isOpen));
            }

            return RelayStatusResult.SuccessResult(doors, response);
        }
        catch (Exception ex)
        {
            return RelayStatusResult.Failure($"Error retrieving relay status: {ex.Message}");
        }
    }


    private static async Task<string> SendCommandAsync(string command)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(RelayIp, RelayPort);
        await using var stream = client.GetStream();

        var buffer = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(buffer);

        var response = new byte[16];
        var bytesRead = await stream.ReadAsync(response);
        return Encoding.ASCII.GetString(response, 0, bytesRead).Trim();
    }

    private static bool IsBitSet(string response, int doorNumber)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length < doorNumber)
            return false;

        var bitChar = response[doorNumber - 1];
        return bitChar == '1';
    }
}

public record DoorStatus(int DoorNumber, bool IsOpen);

public record RelayStatusResult(
    bool Success,
    List<DoorStatus>? Doors = null,
    string? RawResponse = null,
    string? Error = null)
{
    public static RelayStatusResult SuccessResult(List<DoorStatus> doors, string raw) =>
        new(true, doors, raw, null);

    public static RelayStatusResult Failure(string error) =>
        new(false, null, null, error);
}

public record RelayCommandResult(
    bool Success,
    int? DoorNumber = null,
    string? OnResponse = null,
    string? OffResponse = null,
    string? Error = null)
{
    public static RelayCommandResult SuccessResult(int door, string onResponse, string? offResponse = null) =>
        new(true, door, onResponse, offResponse, null);

    public static RelayCommandResult FailureResult(string error) =>
        new(false, null, null, null, error);
}
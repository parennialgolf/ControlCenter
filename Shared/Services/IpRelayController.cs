using System.Net.Sockets;
using System.Text;

namespace Shared.Services;

public static class IpRelayController
{
    private const string RelayIp = "192.168.7.101";
    private const int RelayPort = 6722;
    private const int HoldTimeSeconds = 5;

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
            var onResponse = await SendCommandAsync(command.On);
            var isOnConfirmed = IsBitSet(onResponse, doorNumber);
            if (!isOnConfirmed)
                return RelayCommandResult.FailureResult($"Relay ON failed. Response: {onResponse}");

            // Start a background task to turn OFF relay after a delay
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(HoldTimeSeconds * 1000);
                    var offResponse = await SendCommandAsync(command.Off);
                    var isOffConfirmed = !IsBitSet(offResponse, doorNumber);
                    Console.WriteLine(isOffConfirmed
                        ? $"✅ Door {doorNumber} relay turned OFF"
                        : $"⚠️ Door {doorNumber} relay failed to turn OFF. Response: {offResponse}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error turning off door {doorNumber}: {ex.Message}");
                }
            });

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
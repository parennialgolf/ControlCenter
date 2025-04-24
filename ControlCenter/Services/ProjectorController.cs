using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ControlCenter.Services;

public class ProjectorTransmissionResult(bool wasSent, string? response, string? error)
{
    public bool WasSent { get; } = wasSent;
    public string? Response { get; } = response;
    public string? Error { get; } = error;

    public static ProjectorTransmissionResult Success(string? response = null) =>
        new(true, response, null);

    public static ProjectorTransmissionResult Failure(string error) =>
        new(false, null, error);
}

public class ProjectorControlService(IPAddress ip, IProjectorProtocol protocol) : IProjectorControl
{
    public async Task<ProjectorCommandResult> OnAsync()
    {
        var result = await SendCommandAsync(protocol.On, expectResponse: true);

        if (!result.WasSent)
            return ProjectorCommandResult.FailureResult(
                ip,
                "Failed to send power ON command.",
                result.Error ?? "Unknown error");

        var confirmed = protocol.ParseStatus(result.Response ?? "") == ProjectorStatusType.On;

        return confirmed
            ? ProjectorCommandResult.SuccessResult(
                ip,
                "Power ON confirmed.",
                ProjectorStatusType.On,
                result.Response)
            : ProjectorCommandResult.FailureResult(
                ip,
                "Power ON not confirmed by projector.",
                result.Response);
    }

    public async Task<ProjectorCommandResult> OffAsync()
    {
        var result = await SendCommandAsync(protocol.Off, expectResponse: true);

        if (!result.WasSent)
            return ProjectorCommandResult.FailureResult(
                ip,
                "Failed to send power OFF command.",
                result.Error ?? "Unknown error");

        var confirmed = protocol.ParseStatus(result.Response ?? "") == ProjectorStatusType.Off;

        return confirmed
            ? ProjectorCommandResult.SuccessResult(
                ip,
                "Power OFF confirmed.",
                ProjectorStatusType.Off,
                result.Response)
            : ProjectorCommandResult.FailureResult(
                ip,
                "Power OFF not confirmed by projector.",
                result.Response);
    }


    public async Task<ProjectorCommandResult> GetStatusAsync()
    {
        var result = await SendCommandAsync(protocol.Status, expectResponse: true);

        if (!result.WasSent || string.IsNullOrWhiteSpace(result.Response))
            return ProjectorCommandResult.FailureResult(
                ip,
                "No response from projector.",
                result.Error ?? "Unknown error");

        var status = protocol.ParseStatus(result.Response);
        var message = status switch
        {
            ProjectorStatusType.On => "Projector is ON",
            ProjectorStatusType.Off => "Projector is OFF",
            _ => "Projector status unknown"
        };

        return ProjectorCommandResult.SuccessResult(ip, message, status, result.Response);
    }


    private async Task<ProjectorTransmissionResult> SendCommandAsync(string command, bool expectResponse = false)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(ip, protocol.Port, cts.Token);
            await using var stream = client.GetStream();

            var bytes = Encoding.ASCII.GetBytes(command);
            await stream.WriteAsync(bytes, cts.Token);

            if (!expectResponse)
                return ProjectorTransmissionResult.Success();

            var buffer = new byte[256];
            var bytesRead = await stream.ReadAsync(buffer, cts.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim('\0');

            return ProjectorTransmissionResult.Success(response);
        }
        catch (Exception ex)
        {
            return ProjectorTransmissionResult.Failure($"[Error] {ex.Message}");
        }
    }
}
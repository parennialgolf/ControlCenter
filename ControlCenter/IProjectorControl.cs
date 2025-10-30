using System.Net;

namespace ControlCenter;

public enum ProjectorStatusType
{
    Unknown,
    Success, // ACK from projector
    On,
    Off,
    WarmingUp,
    CoolingDown,
    Failure
}

public enum ProjectorProtocolType
{
    Rs232,
    PjLink,
    PjTalk
}

public class ProjectorCommandResult
{
    public IPAddress IpAddress { get; init; } = null!;
    public bool Success { get; init; }
    public string? Message { get; init; }
    public ProjectorStatusType Status { get; init; }
    public string? RawResponse { get; init; }

    public static ProjectorCommandResult SuccessResult(
        IPAddress ipAddress,
        string message,
        ProjectorStatusType status,
        string? rawResponse = null)
        => new()
        {
            IpAddress = ipAddress,
            Success = true,
            Message = message,
            Status = status,
            RawResponse = rawResponse
        };

    public static ProjectorCommandResult FailureResult(
        IPAddress ipAddress,
        string message,
        string? rawResponse = null)
        => new()
        {
            IpAddress = ipAddress,
            Success = false,
            Message = message,
            RawResponse = rawResponse,
            Status = ProjectorStatusType.Failure
        };
}

public interface IProjectorControl
{
    Task<ProjectorCommandResult> OnAsync();
    Task<ProjectorCommandResult> OffAsync();
    Task<ProjectorCommandResult> GetStatusAsync();
}
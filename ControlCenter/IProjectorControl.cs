namespace ControlCenter.Services;

public enum ProjectorStatusType
{
    On,
    Off,
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
    public bool Success { get; init; }
    public string? Message { get; init; }
    public ProjectorStatusType? Status { get; init; }
    public string? RawResponse { get; init; }

    public static ProjectorCommandResult SuccessResult(
        string message,
        ProjectorStatusType? status = null,
        string? rawResponse = null)
        => new()
        {
            Success = true,
            Message = message,
            Status = status,
            RawResponse = rawResponse
        };

    public static ProjectorCommandResult FailureResult(
        string message, 
        string? rawResponse = null)
        => new()
        {
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

namespace ControlCenter;

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

public interface IProjectorControl
{
    Task OnAsync();
    Task OffAsync();
    Task<ProjectorStatusType> GetStatusAsync();
}
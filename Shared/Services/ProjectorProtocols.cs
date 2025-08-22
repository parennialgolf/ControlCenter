using System.Net;

namespace Shared.Services;

public static class ProjectorControlFactory
{
    public static IProjectorControl Create(IPAddress ip, ProjectorProtocolType type)
    {
        return type switch
        {
            ProjectorProtocolType.PjTalk => new ProjectorControlService(ip, new SonyProtocol()),
            ProjectorProtocolType.Rs232 => new ProjectorControlService(ip, new LgProtocol()),
            ProjectorProtocolType.PjLink => new ProjectorControlService(ip, new PjLinkProtocol()),
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported projector type: {type}")
        };
    }
}

public class LgProtocol : IProjectorProtocol
{
    public int Port => 9761;

    public string On => "ka 01 01\r";
    public string Off => "ka 01 00\r";
    public string Status => "ka 01 FF\r";

    public ProjectorStatusType ParseStatus(string response) =>
        response switch
        {
            _ when response.Contains("OK01") => ProjectorStatusType.On,
            _ when response.Contains("OK00") => ProjectorStatusType.Off,
            _ => ProjectorStatusType.Failure
        };
}

public class PjLinkProtocol : IProjectorProtocol
{
    public int Port => 4352;

    public string On => "%1POWR 1\r";
    public string Off => "%1POWR 0\r";
    public string Status => "%1POWR ?\r";

    public ProjectorStatusType ParseStatus(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return ProjectorStatusType.Failure;

        response = response.Trim();

        return response switch
        {
            "%1POWR=0" => ProjectorStatusType.Off,
            "%1POWR=1" => ProjectorStatusType.On,
            "%1POWR=2" => ProjectorStatusType.WarmingUp,
            "%1POWR=3" => ProjectorStatusType.CoolingDown,
            "%1POWR=OK" => ProjectorStatusType.Success, // ACK for On/Off
            "%1POWR=ERR" => ProjectorStatusType.Failure, // failed command
            _ => ProjectorStatusType.Unknown
        };
    }
}

public class SonyProtocol : IProjectorProtocol
{
    public int Port => 4352;

    public string On => "%1POWR 1\r";
    public string Off => "%1POWR 0\r";
    public string Status => "%1POWR ?\r";

    public ProjectorStatusType ParseStatus(string response) =>
        response switch
        {
            _ when response.EndsWith("1") => ProjectorStatusType.On,
            _ when response.EndsWith("0") => ProjectorStatusType.Off,
            _ => ProjectorStatusType.Failure
        };
}
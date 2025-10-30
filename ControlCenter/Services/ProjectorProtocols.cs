using System.Net;

namespace ControlCenter.Services;

public static class ProjectorControlFactory
{
    public static IProjectorControl Create(IPAddress ip, ProjectorProtocolType type) => type switch
    {
        ProjectorProtocolType.PjTalk => new ProjectorControlService(ip, new SonyProtocol()),
        ProjectorProtocolType.Rs232 => new ProjectorControlService(ip, new LgProtocol()),
        ProjectorProtocolType.PjLink => new ProjectorControlService(ip, new PjLinkProtocol()),
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported projector type: {type}")
    };
}

public class LgProtocol : IProjectorProtocol
{
    public int Port => 9761;

    public string On => "ka 01 01\r";
    public string Off => "ka 01 00\r";
    public string Status => "ka 01 FF\r";

    public ProjectorStatusType ParseStatus(string response) => response switch
    {
        _ when response.Contains("OK01") => ProjectorStatusType.On,
        _ when response.Contains("OK00") => ProjectorStatusType.Off,
        _ => ProjectorStatusType.Failure
    };

    public bool IsCommandAcknowledgement(string response, ProjectorStatusType expected) =>
        expected == ProjectorStatusType.On
            ? response.Contains("OK01")
            : response.Contains("OK00");
}

public class PjLinkProtocol : IProjectorProtocol
{
    public int Port => 4352;

    public string On => "%1POWR 1\r";
    public string Off => "%1POWR 0\r";
    public string Status => "%1POWR ?\r";

    public ProjectorStatusType ParseStatus(string response) => response.Trim() switch
    {
        "%1POWR=0" => ProjectorStatusType.Off,
        "%1POWR=1" => ProjectorStatusType.On,
        "%1POWR=2" => ProjectorStatusType.WarmingUp,
        "%1POWR=3" => ProjectorStatusType.CoolingDown,
        _ => ProjectorStatusType.Unknown
    };

    public bool IsCommandAcknowledgement(string response, ProjectorStatusType expected) =>
        response.Trim() == "%1POWR=OK"; // PJLink always just says OK
}

public class SonyProtocol : IProjectorProtocol
{
    public int Port => 53484; // PJTalk default port (not 4352)

    public string On => "POWR 1\r";
    public string Off => "POWR 0\r";
    public string Status => "POWR ?\r";

    public ProjectorStatusType ParseStatus(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return ProjectorStatusType.Unknown;

        response = response.Trim();

        return response switch
        {
            "POWR=0" => ProjectorStatusType.Off,
            "POWR=1" => ProjectorStatusType.On,
            "POWR=ERR" => ProjectorStatusType.Failure,
            _ => ProjectorStatusType.Unknown
        };
    }

    public bool IsCommandAcknowledgement(string response, ProjectorStatusType expected)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        response = response.Trim();
        return response == "POWR=OK"; // PJTalk sends OK for power set commands
    }
}
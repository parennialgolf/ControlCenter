namespace Shared;

public interface IProjectorProtocol
{
    int Port { get; }
    string On { get; }
    string Off { get; }
    string Status { get; }

    ProjectorStatusType ParseStatus(string response);

    // ✅ NEW: confirm if a response is a valid ACK for On/Off
    bool IsCommandAcknowledgement(string response, ProjectorStatusType expected);
}
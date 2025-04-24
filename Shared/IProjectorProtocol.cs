namespace Shared;

public interface IProjectorProtocol
{
    int Port { get; }
    string On { get; }
    string Off { get; }
    string Status { get; }
    ProjectorStatusType ParseStatus(string response);
}
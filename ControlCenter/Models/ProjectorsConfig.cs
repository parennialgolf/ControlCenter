using System.Text.Json.Serialization;

namespace ControlCenter.Models;

public class ProjectorsConfig
{
    public bool Managed { get; set; }
    public int Max { get; set; }
    public List<RegisteredProjector> Projectors { get; set; } = [];
}

public class RegisteredProjector
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string IpAddress { get; set; } = null!;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProjectorProtocolType Protocol { get; set; }

    public RegisteredProjector(int id, string name, string ipAddress, ProjectorProtocolType protocol)
    {
        Id = id;
        Name = name;
        IpAddress = ipAddress;
        Protocol = protocol;
    }

    public RegisteredProjector()
    {
    }
}
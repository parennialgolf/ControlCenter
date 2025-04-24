using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO.Ports;
using System.Net;
using ControlCenter.Services;

namespace ControlCenter.Data;

public class DoorRelayConfig
{
    public int Id { get; set; }
    [MaxLength(50)] public string Host { get; set; } = null!;

    public int DoorNumber { get; set; }

    public int Port { get; set; }
}

public class SerialPortConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public Guid LockerId { get; set; }

    public SerialPortConfig(string name)
    {
        Name = name;
    }

    public SerialPortConfig()
    {
    }

    public SerialPort ToSerialPort()
    {
        return new SerialPort(Name);
    }
}

public class DoorConfig
{
    [Key] public int Id { get; set; }
    public Guid SystemId { get; set; }
    public bool Managed { get; set; }
    public int Count { get; set; }
    public int HoldTimeSeconds { get; set; }
    public IPAddress IpAddress { get; set; } = null!;
    public List<DoorRelayConfig> Doors { get; set; } = [];
}

public class LockerConfig
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(SystemId))] public Guid SystemId { get; set; }

    public bool Managed { get; set; }
    public int Count { get; set; }
    public List<SerialPortConfig> SerialPorts { get; set; } = [];
}

public class Projector
{
    [Key] public int Id { get; set; }

    [Required] [MaxLength(100)] public string Name { get; set; } = null!;

    [Required] public IPAddress Ip { get; set; } = null!;

    [Required] public ProjectorProtocolType Protocol { get; set; }
}

public class ProjectorConfig
{
    [Key] public int Id { get; set; }
    public Guid SystemId { get; set; }
    public bool Managed { get; set; }
    public List<Projector> Projectors { get; set; } = [];
}

public class AppConfig
{
    [Key] public Guid SystemId { get; set; }

    public DoorConfig Doors { get; set; } = null!;
    public LockerConfig Lockers { get; set; } = null!;
    public ProjectorConfig Projectors { get; set; } = null!;

    public AppConfig(DoorConfig doors, LockerConfig lockers, ProjectorConfig projectors)
    {
        SystemId = Guid.CreateVersion7();
        Doors = doors;
        Doors.SystemId = SystemId;
        Lockers = lockers;
        Lockers.SystemId = SystemId;
        Projectors = projectors;
        Projectors.SystemId = SystemId;
    }

    private AppConfig()
    {
    }
}

public class RelayCommand
{
    public string Relay { get; set; } = null!;
    public string Off { get; set; } = null!;
    public string On { get; set; } = null!;

    public RelayCommand(
        string relay,
        string off,
        string on)
    {
        Relay = relay;
        Off = off;
        On = on;
    }

    private RelayCommand()
    {
    }
}
namespace ControlCenter.Models;

public class LockersConfig
{
    public bool LegacyEnabled { get; set; }
    public string Host { get; set; } = null!;
    public bool Managed { get; set; }
    public int Max { get; set; }
    public List<string> SerialPorts { get; set; } = [];
}
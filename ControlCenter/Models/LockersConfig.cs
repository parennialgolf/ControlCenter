namespace Shared.Models;

public class LockersConfig
{
    public bool Managed { get; set; }
    public bool UseLegacy { get; set; }
    public int Max { get; set; }
    public List<string> SerialPorts { get; set; } = [];
}
namespace ControlCenter.Models;

public class DoorsConfig
{
    public bool Managed { get; set; }
    public int Max { get; set; }
    public string IpAddress { get; set; } = null!;
}
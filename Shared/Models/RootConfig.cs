namespace Shared.Models;

public class RootConfig
{
    public string SystemId { get; set; } = Environment.MachineName;
    public DoorsConfig Doors { get; set; } = null!;
    public ProjectorsConfig Projectors { get; set; } = null!;
    public LockersConfig Lockers { get; set; } = null!;
}
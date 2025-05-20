namespace Shared.Models;

public class DoorsConfig
{
    public bool Managed { get; set; }
    public int Max { get; set; }
    
    // [JsonConverter(typeof(IpAddressJsonConverter))]
    public string IpAddress { get; set; } = null!;
}
using System.Text.Json.Serialization;

namespace ControlCenter.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LockerStatus
{
    Locked,
    Unlocked,
    Unknown
}
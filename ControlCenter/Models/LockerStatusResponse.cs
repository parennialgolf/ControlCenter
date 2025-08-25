using System.Text.Json.Serialization;

namespace ControlCenter.Models;

public record LockerStatusResponse(
    bool Success,
    List<LockerStatusResult> Data
);

public record LockerStatusResult(
    int LockerNumber,
    LockerStatus Status,
    string? Error = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LockerStatus
{
    Locked,
    Unlocked,
    FailedToUnlock,
    Unknown
}
using System.Text.Json.Serialization;

namespace ControlCenter.Models;

public record LockerStatusResponse(
    bool Success,
    List<LockerStatusResult> Data
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LockerStatus
{
    Locked,
    Unlocked,
    FailedToUnlock,
    Unknown
}

public record LockerStatusResult
{
    [JsonPropertyName("lockerNumber")] public int LockerNumber { get; set; }

    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    [JsonPropertyName("error")] public string? Error { get; set; }
}
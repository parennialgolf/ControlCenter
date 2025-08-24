using ControlCenter.Models;

public record LockerStatusResponse(
    bool Success,
    List<LockerStatusResult> Data
);

public record LockerStatusResult(
    int LockerNumber,
    LockerStatus Status);
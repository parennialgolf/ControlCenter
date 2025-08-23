namespace Shared;

/// <summary>
/// Status result per locker.
/// </summary>
public record LockerRelayStatus(int LockerNumber, bool IsOn);

/// <summary>
/// Maps a locker to its serial port and channel.
/// </summary>
public record GetRelayResult(string SerialPort, int Channel);

/// <summary>
/// Result of sending a command to the relay.
/// </summary>
public record SerialCommandResult(bool Success, string? StatusResponse = null, string? Error = null);
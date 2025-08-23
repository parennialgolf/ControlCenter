using System.Collections.Concurrent;

namespace SerialRelayController;

public class LockerStateCache
{
    private readonly ConcurrentDictionary<int, bool> _states = [];

    public void MarkUnlocked(int lockerNumber)
        => _states[lockerNumber] = true;

    public void MarkLocked(int lockerNumber)
        => _states[lockerNumber] = false;

    public bool IsUnlocked(int lockerNumber)
        => _states.TryGetValue(lockerNumber, out var state) && state;
}
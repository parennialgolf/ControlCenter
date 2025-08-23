namespace ControlCenter.Services;

public static class ControlByWebRelayCommands
{
    // private static string FormatHost(IPAddress ip) =>
    //     ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString();

    public static Uri On(string ip, int doorNumber) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay{doorNumber}State=1");

    public static Uri Off(string ip, int doorNumber) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay{doorNumber}State=0");

    public static Uri Pulse(string ip, int doorNumber) =>
        new($"http://admin:webrelay@{ip}/state.xml?relay{doorNumber}State=2");

    public static Uri Status(string ip) =>
        new($"http://admin:webrelay@{ip}/state.json");
}
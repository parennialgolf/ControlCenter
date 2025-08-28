using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using ControlCenter.Models;
using Microsoft.Extensions.Options;

namespace ControlCenter.Services;

public class ControlByWebRelayController(
    IOptionsMonitor<DoorsConfig> config,
    HttpClient httpClient)
{
    private readonly DoorsConfig _doors = config.CurrentValue;

    public async Task<DoorsCommandResult> OpenAsync(int doorNumber)
    {
        try
        {
            if (!_doors.Managed)
                return DoorsCommandResult.FailureResult("Doors are not managed.", doorNumber);

            if (doorNumber < 1 || doorNumber > _doors.Max)
                return DoorsCommandResult.FailureResult("Invalid door number.", doorNumber);

            var response = await httpClient.GetAsync(ControlByWebRelayCommands.On(_doors.IpAddress, doorNumber));

            if (!response.IsSuccessStatusCode)
                return DoorsCommandResult.FailureResult($"Relay responded with {response.StatusCode}", doorNumber);

            var stream = await response.Content.ReadAsStreamAsync();
            var serializer = new XmlSerializer(typeof(DeviceStatus));
            var parsed = (DeviceStatus?)serializer.Deserialize(stream);

            return parsed == null
                ? DoorsCommandResult.FailureResult("Failed to parse relay response.", doorNumber)
                : DoorsCommandResult.SuccessResult(parsed, doorNumber);
        }
        catch (Exception ex)
        {
            return DoorsCommandResult.FailureResult($"Error: {ex.Message}", doorNumber);
        }
    }

    public async Task<DoorsCommandResult> CloseAsync(int doorNumber)
    {
        try
        {
            if (_doors.Managed == false)
                return DoorsCommandResult.FailureResult("Doors are not managed.", doorNumber);

            if (doorNumber < 1 || doorNumber > _doors.Max)
                return DoorsCommandResult.FailureResult("Invalid door number.", doorNumber);

            var response = await httpClient.GetAsync(ControlByWebRelayCommands.Off(_doors.IpAddress, doorNumber));

            if (!response.IsSuccessStatusCode)
                return DoorsCommandResult.FailureResult($"Relay responded with {response.StatusCode}", doorNumber);

            var stream = await response.Content.ReadAsStreamAsync();
            var serializer = new XmlSerializer(typeof(DeviceStatus));
            var parsed = (DeviceStatus?)serializer.Deserialize(stream);

            return parsed == null
                ? DoorsCommandResult.FailureResult("Failed to parse relay response.", doorNumber)
                : DoorsCommandResult.SuccessResult(parsed, doorNumber);
        }
        catch (Exception ex)
        {
            return DoorsCommandResult.FailureResult($"Error: {ex.Message}", doorNumber);
        }
    }

    public async Task<DoorsCommandResult> PulseAsync(int doorNumber)
    {
        if (!_doors.Managed)
            return DoorsCommandResult.FailureResult("Doors are not managed.", doorNumber);

        if (doorNumber < 1 || doorNumber > _doors.Max)
            return DoorsCommandResult.FailureResult("Invalid door number.", doorNumber);

        try
        {
            var response = await httpClient.GetAsync(ControlByWebRelayCommands.Pulse(_doors.IpAddress, doorNumber));

            if (!response.IsSuccessStatusCode)
                return DoorsCommandResult.FailureResult($"Relay responded with {response.StatusCode}", doorNumber);

            var stream = await response.Content.ReadAsStreamAsync();
            var serializer = new XmlSerializer(typeof(DeviceStatus));
            var parsed = (DeviceStatus?)serializer.Deserialize(stream);

            return parsed == null
                ? DoorsCommandResult.FailureResult("Failed to parse relay response.", doorNumber)
                : DoorsCommandResult.SuccessResult(parsed, doorNumber);
        }
        catch (Exception ex)
        {
            return DoorsCommandResult.FailureResult($"Error: {ex.Message}", doorNumber);
        }
    }

    public async Task<DoorsCommandResult> StatusAsync()
    {
        try
        {
            if (_doors.Managed == false)
                return DoorsCommandResult.FailureResult("Doors are not managed.", 0);

            var response = await httpClient.GetAsync(ControlByWebRelayCommands.Status(_doors.IpAddress));
            if (!response.IsSuccessStatusCode)
                return DoorsCommandResult.FailureResult($"Relay responded with {response.StatusCode}", 0);

            var stream = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<DeviceStatus>(stream);

            return result is null
                ? DoorsCommandResult.FailureResult("Failed to parse relay response.", 0)
                : DoorsCommandResult.SuccessResult(result, 0);
        }
        catch (Exception ex)
        {
            return DoorsCommandResult.FailureResult($"Error: {ex.Message}", 0);
        }
    }
}

public class DoorsCommandResult
{
    public DeviceStatus? Data { get; private set; }
    public bool Success { get; private set; }
    public string? Error { get; private set; }
    public int DoorNumber { get; private set; }

    public static DoorsCommandResult SuccessResult(DeviceStatus doorsConfig, int doorNumber) =>
        new() { Success = true, DoorNumber = doorNumber, Data = doorsConfig };

    public static DoorsCommandResult FailureResult(string error, int doorNumber) =>
        new() { Success = false, Error = error, DoorNumber = doorNumber };
}

[XmlRoot("datavalues")]
public class DeviceStatus
{
    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("digitalInput1")]
    public int DigitalInput1 { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("digitalInput2")]
    public int DigitalInput2 { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("relay1")]
    public int Relay1 { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("relay2")]
    public int Relay2 { get; set; }

    [JsonConverter(typeof(DoubleFromStringConverter))]
    [XmlElement("vin")]
    public double Vin { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("register1")]
    public int Register1 { get; set; }

    [JsonConverter(typeof(DoubleFromStringConverter))]
    [XmlElement("lat")]
    public double Lat { get; set; }

    [JsonConverter(typeof(DoubleFromStringConverter))]
    [XmlElement("long")]
    public double Long { get; set; }

    [JsonConverter(typeof(LongFromStringConverter))]
    [XmlElement("utcTime")]
    public long UtcTime { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("timezoneOffset")]
    public int TimezoneOffset { get; set; }

    [XmlElement("serialNumber")] public string SerialNumber { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("minRecRefresh")]
    public int MinRecRefresh { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("downloadSettings")]
    public int DownloadSettings { get; set; }
}
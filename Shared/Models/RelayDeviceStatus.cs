using System.Text.Json.Serialization;
using System.Xml.Serialization;
namespace Shared.Models;

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

    [XmlElement("serialNumber")]
    public string SerialNumber { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("minRecRefresh")]
    public int MinRecRefresh { get; set; }

    [JsonConverter(typeof(IntFromStringConverter))]
    [XmlElement("downloadSettings")]
    public int DownloadSettings { get; set; }
}

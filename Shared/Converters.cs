using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

public class IntFromStringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String when int.TryParse(reader.GetString(), out var i) => i,
            JsonTokenType.Number => reader.GetInt32(),
            _ => throw new JsonException("Invalid token for int")
        };
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public class LongFromStringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String when long.TryParse(reader.GetString(), out var l) => l,
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("Invalid token for long")
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public class DoubleFromStringConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String when double.TryParse(reader.GetString(), out var d) => d,
            JsonTokenType.Number => reader.GetDouble(),
            _ => throw new JsonException("Invalid token for double")
        };
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public class IpAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var ipString = reader.GetString();
        return IPAddress.Parse(ipString!);
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public class ProjectorStatusJsonConverter : JsonConverter<ProjectorStatusType>
{
    public override ProjectorStatusType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str?.ToLowerInvariant() switch
        {
            "On" => ProjectorStatusType.On,
            "Off" => ProjectorStatusType.Off,
            "WarmingUp" => ProjectorStatusType.WarmingUp,
            "CoolingDown" => ProjectorStatusType.CoolingDown,
            "Success" => ProjectorStatusType.Success,
            "Failure" => ProjectorStatusType.Failure,
            _ => ProjectorStatusType.Unknown
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ProjectorStatusType value,
        JsonSerializerOptions options)
    {
        var str = value switch
        {
            ProjectorStatusType.On => "On",
            ProjectorStatusType.Off => "Off",
            ProjectorStatusType.WarmingUp => "WarmingUp",
            ProjectorStatusType.CoolingDown => "CoolingDown",
            ProjectorStatusType.Success => "Success",
            ProjectorStatusType.Failure => "Failure",
            _ => "Unknown"
        };

        writer.WriteStringValue(str);
    }
}
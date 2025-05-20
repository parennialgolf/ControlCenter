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

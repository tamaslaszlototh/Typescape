using System.Text.Json;
using System.Text.Json.Serialization;
using Typescape.Snapshot.Records;

namespace Typescape.Snapshot.Json;

/// <summary>Reads and writes an <see cref="OccurrenceRecord"/> as its five-element tuple.</summary>
internal sealed class OccurrenceJsonConverter : JsonConverter<OccurrenceRecord>
{
    private const string Shape = "An Occurrence is the array [dependency, sourceMember, targetMember, document, line].";

    public override bool HandleNull => true;

    public override OccurrenceRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(Shape);
        }

        var dependency = ReadInt(ref reader) ?? throw new JsonException(Shape + " dependency is required.");
        var sourceMember = ReadInt(ref reader);
        var targetMember = ReadInt(ref reader);
        var document = ReadInt(ref reader) ?? throw new JsonException(Shape + " document is required.");
        var line = ReadInt(ref reader) ?? throw new JsonException(Shape + " line is required.");

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException(Shape + " It has more than five elements.");
        }

        return new OccurrenceRecord(dependency, sourceMember, targetMember, document, line);
    }

    public override void Write(Utf8JsonWriter writer, OccurrenceRecord value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            throw new JsonException("An Occurrence cannot be null.");
        }

        writer.WriteStartArray();
        writer.WriteNumberValue(value.Dependency);
        WriteInt(writer, value.SourceMember);
        WriteInt(writer, value.TargetMember);
        writer.WriteNumberValue(value.Document);
        writer.WriteNumberValue(value.Line);
        writer.WriteEndArray();
    }

    private static int? ReadInt(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt32(out var value) => value,
            JsonTokenType.EndArray => throw new JsonException(Shape + " It has fewer than five elements."),
            _ => throw new JsonException(Shape + " Its elements are integers or null."),
        };
    }

    private static void WriteInt(Utf8JsonWriter writer, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

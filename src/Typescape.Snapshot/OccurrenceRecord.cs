using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typescape.Snapshot;

/// <summary>
/// One place in the source where a Dependency appears. Written as the tuple
/// <c>[dependency, sourceMember, targetMember, document, line]</c>, because Occurrences outnumber every other row.
/// </summary>
/// <param name="Dependency">Index into <see cref="SnapshotRecord.Dependencies"/>.</param>
/// <param name="SourceMember">
/// Index into <see cref="SnapshotRecord.ExecutableMembers"/> of the using member; <c>null</c> when the use is outside
/// every Executable Member (field and property initializers, declaration-level Kinds such as <c>inherits</c>).
/// </param>
/// <param name="TargetMember">
/// The used member, <c>null</c> when there is none (e.g. <c>inherits</c>, <c>type-usage</c>). Which table it indexes:
/// <list type="bullet">
/// <item>the Dependency's target is External: <see cref="SnapshotRecord.ExternalMembers"/>;</item>
/// <item>the target is an own type and the value is <c>&gt;= 0</c>: <see cref="SnapshotRecord.ExecutableMembers"/>;</item>
/// <item>
/// the target is an own type and the value is negative: <see cref="SnapshotRecord.NonExecutableMembers"/> at index
/// <c>~value</c> (<c>-1</c> is row 0). Build it with <see cref="NonExecutableTarget"/>.
/// </item>
/// </list>
/// <see cref="SnapshotRecord.ResolveTargetMember"/> resolves all three cases.
/// </param>
/// <param name="Document">Index into <see cref="SnapshotRecord.Documents"/>.</param>
/// <param name="Line">The 1-based physical line, as in the original file (not <c>#line</c>-mapped).</param>
[JsonConverter(typeof(OccurrenceJsonConverter))]
public sealed record OccurrenceRecord(int Dependency, int? SourceMember, int? TargetMember, int Document, int Line)
{
    /// <summary>The <see cref="TargetMember"/> value that points at row <paramref name="index"/> of <see cref="SnapshotRecord.NonExecutableMembers"/>.</summary>
    public static int NonExecutableTarget(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return ~index;
    }
}

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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Typescape.Snapshot.Records;

namespace Typescape.Snapshot.Json;

/// <summary>
/// The JSON Schema of the Snapshot format, exported with <see cref="JsonSchemaExporter"/> from the records and
/// <see cref="SnapshotJson.Options"/>, so the documented format cannot disagree with what the writer writes.
/// <c>dotnet run build.cs -- schema</c> writes it to the checked-in <c>schema/snapshot.schema.json</c>, from which the
/// Viewer's TypeScript types are generated (ADR 0003).
/// </summary>
/// <remarks>
/// <para>
/// Every record and enum of the format is one entry of <c>$defs</c>, named after its type without the <c>Record</c>
/// suffix and referenced wherever it is used, so each one becomes one named TypeScript type. A nullable use is
/// <c>anyOf</c> the reference and <c>null</c>.
/// </para>
/// <para>
/// The output is deterministic: properties in record order, enum values in declaration order, <c>$defs</c> in ordinal
/// name order, two-space indentation and LF line endings on every platform. Its <c>$comment</c> names
/// <see cref="SnapshotRecord.CurrentSchemaVersion"/> and the <c>schemaVersion</c> pattern names its major, so a
/// review sees a format change and the version bump (or its absence) in the same diff.
/// </para>
/// </remarks>
public static class SnapshotSchema
{
    private const string DefsPointer = "#/$defs/";

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>Exports the schema as the text of <c>schema/snapshot.schema.json</c>, ending with a newline.</summary>
    public static string Export() => Build().ToJsonString(OutputOptions) + "\n";

    private static JsonObject Build()
    {
        var definitions = new Definitions();
        var root = (JsonObject)definitions.Export(typeof(SnapshotRecord));
        var defs = definitions.ExportAll();

        var major = SnapshotRecord.CurrentSchemaVersion.Split('.')[0];
        root["properties"]!["schemaVersion"]!["pattern"] = $"^{major}\\.(0|[1-9][0-9]*)$";

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$comment"] =
                $"Typescape Snapshot, schemaVersion {SnapshotRecord.CurrentSchemaVersion}. Generated from the " +
                "Typescape.Snapshot records by `dotnet run build.cs -- schema`: do not edit by hand.",
            ["title"] = "Snapshot",
        };
        foreach (var (name, value) in root.ToList())
        {
            root.Remove(name);
            schema[name] = value;
        }

        schema["$defs"] = defs;
        return schema;
    }

    /// <summary>
    /// An Occurrence is written as a tuple by <see cref="OccurrenceJsonConverter"/>, which the exporter cannot see into,
    /// so its schema is written here.
    /// </summary>
    private static JsonObject OccurrenceSchema()
    {
        static JsonObject Element(string description, bool nullable) => new()
        {
            ["description"] = description,
            ["type"] = nullable ? new JsonArray("integer", "null") : "integer",
        };

        return new JsonObject
        {
            ["type"] = "array",
            ["prefixItems"] = new JsonArray(
                Element("dependency: index into dependencies", nullable: false),
                Element("sourceMember: index into executableMembers, or null", nullable: true),
                Element(
                    "targetMember: index into externalMembers when the Dependency is external, else into " +
                    "executableMembers when >= 0, else into nonExecutableMembers at ~value; or null",
                    nullable: true),
                Element("document: index into documents", nullable: false),
                Element("line: the 1-based physical line", nullable: false)),
            ["items"] = false,
            ["minItems"] = 5,
            ["maxItems"] = 5,
        };
    }

    /// <summary>Collects the <c>$defs</c> entries: every format type reached from the root, exported once each.</summary>
    private sealed class Definitions
    {
        private readonly Dictionary<string, Type> typesByName = new(StringComparer.Ordinal);
        private readonly Queue<Type> pending = new();

        public JsonNode Export(Type type)
        {
            var options = new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
                TransformSchemaNode = (context, schema) => Transform(type, context, schema),
            };
            return JsonSchemaExporter.GetJsonSchemaAsNode(SnapshotJson.Options, type, options);
        }

        public JsonObject ExportAll()
        {
            var defs = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var type))
            {
                var name = NameOf(type);
                var schema = type == typeof(OccurrenceRecord) ? OccurrenceSchema() : (JsonObject)Export(type);
                var titled = new JsonObject { ["title"] = name };
                foreach (var (key, value) in schema.ToList())
                {
                    schema.Remove(key);
                    titled[key] = value;
                }

                defs[name] = titled;
            }

            var result = new JsonObject();
            foreach (var (name, schema) in defs)
            {
                result[name] = schema;
            }

            return result;
        }

        private JsonNode Transform(Type exported, JsonSchemaExporterContext context, JsonNode schema)
        {
            var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

            // The exporter leaves out `items` when the element schema is `true`, as it is for a type with a custom
            // converter, before this transform sees the element: restore it as a reference.
            if (context.TypeInfo.ElementType is { } element && IsDefinition(element) && schema is JsonObject collection &&
                !collection.ContainsKey("items"))
            {
                collection["items"] = new JsonObject { ["$ref"] = DefsPointer + Reference(element) };
            }

            if ((context.Path.IsEmpty && type == exported) || !IsDefinition(type))
            {
                return schema;
            }

            var reference = new JsonObject { ["$ref"] = DefsPointer + Reference(type) };
            return IsNullable(schema)
                ? new JsonObject { ["anyOf"] = new JsonArray(reference, new JsonObject { ["type"] = "null" }) }
                : reference;
        }

        private string Reference(Type type)
        {
            var name = NameOf(type);
            if (typesByName.TryGetValue(name, out var existing))
            {
                if (existing != type)
                {
                    throw new InvalidOperationException(
                        $"{existing.FullName} and {type.FullName} would share the schema definition '{name}'.");
                }
            }
            else
            {
                typesByName.Add(name, type);
                pending.Enqueue(type);
            }

            return name;
        }

        private static bool IsDefinition(Type type) =>
            type.Assembly == typeof(SnapshotRecord).Assembly && type != typeof(SnapshotRecord) &&
            (type.IsEnum || type.IsClass);

        private static string NameOf(Type type) =>
            type.Name.EndsWith("Record", StringComparison.Ordinal) ? type.Name[..^"Record".Length] : type.Name;

        private static bool IsNullable(JsonNode schema) =>
            schema is JsonObject node &&
            ((node["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "null")) ||
             (node["enum"] is JsonArray values && values.Any(v => v is null)));
    }
}

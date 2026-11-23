using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Typescape.Snapshot.Records;
using Typescape.Snapshot.Json;

namespace Typescape.Snapshot.Tests;

public class SnapshotSchemaTests
{
    private static readonly JsonObject Schema = (JsonObject)JsonNode.Parse(SnapshotSchema.Export())!;

    private static JsonObject Defs => Schema["$defs"]!.AsObject();

    [Fact]
    public void Export_is_deterministic_and_uses_LF_line_endings()
    {
        var text = SnapshotSchema.Export();

        Assert.Equal(text, SnapshotSchema.Export());
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"$schema\": ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_names_the_draft_and_the_current_schema_version()
    {
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)Schema["$schema"]);
        Assert.Equal("Snapshot", (string?)Schema["title"]);
        Assert.Contains($"schemaVersion {SnapshotRecord.CurrentSchemaVersion}.", (string?)Schema["$comment"], StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaVersion_is_the_first_required_property_and_accepts_every_minor_of_the_current_major()
    {
        var properties = Schema["properties"]!.AsObject();
        Assert.Equal("schemaVersion", properties.First().Key);
        Assert.Equal("schemaVersion", (string?)Schema["required"]![0]);

        var pattern = new Regex((string)properties["schemaVersion"]!["pattern"]!, RegexOptions.None, TimeSpan.FromSeconds(1));
        var major = SnapshotRecord.CurrentSchemaVersion.Split('.')[0];
        Assert.Matches(pattern, SnapshotRecord.CurrentSchemaVersion);
        Assert.Matches(pattern, $"{major}.0");
        Assert.Matches(pattern, $"{major}.12");
        Assert.DoesNotMatch(pattern, $"{int.Parse(major, System.Globalization.CultureInfo.InvariantCulture) + 1}.0");
        Assert.DoesNotMatch(pattern, $"{major}.01");
        Assert.DoesNotMatch(pattern, major);
    }

    [Fact]
    public void Every_record_and_enum_of_the_format_is_one_named_definition()
    {
        var expected = typeof(SnapshotRecord).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(ProjectRecord).Namespace)
            .Select(type => type.Name.EndsWith("Record", StringComparison.Ordinal) ? type.Name[..^"Record".Length] : type.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, Defs.Select(def => def.Key));
        Assert.All(Defs, def => Assert.Equal(def.Key, (string?)def.Value!["title"]));
    }

    [Fact]
    public void Every_reference_resolves_and_every_definition_is_referenced()
    {
        var references = Descendants(Schema)
            .OfType<JsonObject>()
            .Select(node => (string?)node["$ref"])
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(references, reference =>
        {
            Assert.StartsWith("#/$defs/", reference, StringComparison.Ordinal);
            Assert.True(Defs.ContainsKey(reference["#/$defs/".Length..]), $"{reference} does not resolve.");
        });
        Assert.All(Defs, def => Assert.Contains($"#/$defs/{def.Key}", references));
    }

    [Fact]
    public void Enums_are_their_kebab_case_strings()
    {
        Assert.Equal(
            ["class", "interface", "struct", "record", "record-struct", "enum", "delegate"],
            Defs["DeclaredTypeKind"]!["enum"]!.AsArray().Select(value => (string?)value));
    }

    [Fact]
    public void An_Occurrence_is_the_five_element_tuple()
    {
        var occurrence = Defs["Occurrence"]!;

        Assert.Equal("array", (string?)occurrence["type"]);
        Assert.Equal(5, occurrence["prefixItems"]!.AsArray().Count);
        Assert.False((bool)occurrence["items"]!);
        Assert.Equal(5, (int)occurrence["minItems"]!);
        Assert.Equal(5, (int)occurrence["maxItems"]!);
        Assert.Equal("#/$defs/Occurrence", (string?)Schema["properties"]!["occurrences"]!["items"]!["$ref"]);
    }

    [Fact]
    public void A_nullable_use_of_a_definition_is_anyOf_the_reference_and_null()
    {
        var git = Defs["Provenance"]!["properties"]!["git"]!.ToJsonString();
        var generatedReason = Defs["Document"]!["properties"]!["generatedReason"]!.ToJsonString();

        Assert.Equal("""{"anyOf":[{"$ref":"#/$defs/GitProvenance"},{"type":"null"}]}""", git);
        Assert.Equal("""{"anyOf":[{"$ref":"#/$defs/GeneratedReason"},{"type":"null"}]}""", generatedReason);
    }

    public static TheoryData<string> Snapshots() =>
    [
        Serialize(SnapshotRecordTests.Sample()),
        Serialize(SnapshotRecordTests.Sample() with
        {
            Occurrences = null,
            Provenance = SnapshotRecordTests.SampleProvenance() with { Git = null, Framework = "net10.0" },
        }),
    ];

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void What_the_writer_writes_conforms_to_the_schema(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Empty(Conformance.Check(document.RootElement, Schema, Schema));
    }

    [Theory]
    [InlineData("an unknown enum value", "$.projects[0].style")]
    [InlineData("a property the schema does not list", "$.projects[0].extra")]
    [InlineData("a missing required property", "$.types[0]")]
    [InlineData("an Occurrence of four elements", "$.occurrences[0]")]
    [InlineData("an Occurrence of six elements", "$.occurrences[0]")]
    [InlineData("a nullable record of the wrong shape", "$.provenance.git")]
    [InlineData("the next major schemaVersion", "$.schemaVersion")]
    public void The_conformance_check_rejects_JSON_the_schema_does_not_describe(string mutation, string path)
    {
        var snapshot = JsonNode.Parse(Serialize(SnapshotRecordTests.Sample()))!.AsObject();
        Mutate(snapshot, mutation);
        using var document = JsonDocument.Parse(snapshot.ToJsonString());

        var errors = Conformance.Check(document.RootElement, Schema, Schema);

        Assert.Contains(errors, error => error.StartsWith(path + ":", StringComparison.Ordinal));
    }

    private static string Serialize(SnapshotRecord snapshot) => JsonSerializer.Serialize(snapshot, SnapshotJson.Options);

    private static void Mutate(JsonObject snapshot, string mutation)
    {
        switch (mutation)
        {
            case "an unknown enum value":
                snapshot["projects"]![0]!["style"] = "modern";
                break;
            case "a property the schema does not list":
                snapshot["projects"]![0]!["extra"] = 1;
                break;
            case "a missing required property":
                snapshot["types"]![0]!.AsObject().Remove("loc");
                break;
            case "an Occurrence of four elements":
                snapshot["occurrences"]![0]!.AsArray().RemoveAt(4);
                break;
            case "an Occurrence of six elements":
                snapshot["occurrences"]![0]!.AsArray().Add(4);
                break;
            case "a nullable record of the wrong shape":
                snapshot["provenance"]!["git"]!["commit"] = 1;
                break;
            case "the next major schemaVersion":
                snapshot["schemaVersion"] = "2.0";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject obj => obj.Select(property => property.Value),
            JsonArray array => array.AsEnumerable(),
            _ => [],
        };
        foreach (var child in children.OfType<JsonNode>().SelectMany(Descendants))
        {
            yield return child;
        }
    }

    /// <summary>
    /// A JSON Schema check for exactly the keywords the exported schema uses, stricter than JSON Schema in one way: an
    /// object property the schema does not list is an error, because the schema has to document every field the
    /// writer writes. A keyword it does not know fails the check, so the exporter cannot start emitting one unnoticed.
    /// </summary>
    private static class Conformance
    {
        private static readonly HashSet<string> Annotations = new(StringComparer.Ordinal)
        {
            "$schema", "$comment", "$defs", "title", "description", "format",
        };

        public static List<string> Check(JsonElement value, JsonNode schema, JsonObject root)
        {
            var errors = new List<string>();
            Check(value, schema, root, "$", errors);
            return errors;
        }

        private static void Check(JsonElement value, JsonNode? schema, JsonObject root, string path, List<string> errors)
        {
            if (schema is JsonValue boolean && boolean.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                if (!(bool)boolean)
                {
                    errors.Add($"{path}: no value is allowed here");
                }

                return;
            }

            var node = schema!.AsObject();
            foreach (var (keyword, argument) in node)
            {
                switch (keyword)
                {
                    case "$ref":
                        Check(value, root["$defs"]![((string)argument!)["#/$defs/".Length..]], root, path, errors);
                        break;
                    case "anyOf":
                        if (!argument!.AsArray().Any(option => Check(value, option!, root).Count == 0))
                        {
                            errors.Add($"{path}: matches no anyOf option");
                        }

                        break;
                    case "type":
                        var types = argument is JsonArray list ? list.Select(t => (string)t!).ToList() : [(string)argument!];
                        if (!types.Contains(TypeOf(value)) && !(TypeOf(value) == "integer" && types.Contains("number")))
                        {
                            errors.Add($"{path}: is {TypeOf(value)}, not {string.Join(" or ", types)}");
                        }

                        break;
                    case "enum":
                        if (!argument!.AsArray().Any(option => JsonNode.DeepEquals(option, JsonNode.Parse(value.GetRawText()))))
                        {
                            errors.Add($"{path}: {value.GetRawText()} is not one of the enum values");
                        }

                        break;
                    case "pattern":
                        if (value.ValueKind == JsonValueKind.String &&
                            !Regex.IsMatch(value.GetString()!, (string)argument!, RegexOptions.None, TimeSpan.FromSeconds(1)))
                        {
                            errors.Add($"{path}: does not match {argument}");
                        }

                        break;
                    case "properties" when value.ValueKind == JsonValueKind.Object:
                        var properties = argument!.AsObject();
                        foreach (var property in value.EnumerateObject())
                        {
                            if (properties[property.Name] is { } propertySchema)
                            {
                                Check(property.Value, propertySchema, root, $"{path}.{property.Name}", errors);
                            }
                            else
                            {
                                errors.Add($"{path}.{property.Name}: the schema does not list it");
                            }
                        }

                        break;
                    case "required" when value.ValueKind == JsonValueKind.Object:
                        foreach (var name in argument!.AsArray().Select(n => (string)n!))
                        {
                            if (!value.TryGetProperty(name, out _))
                            {
                                errors.Add($"{path}: required {name} is missing");
                            }
                        }

                        break;
                    case "additionalProperties" when value.ValueKind == JsonValueKind.Object:
                        foreach (var property in value.EnumerateObject())
                        {
                            Check(property.Value, argument, root, $"{path}.{property.Name}", errors);
                        }

                        break;
                    case "prefixItems" when value.ValueKind == JsonValueKind.Array:
                        var prefix = argument!.AsArray();
                        foreach (var (element, index) in value.EnumerateArray().Take(prefix.Count).Select((e, i) => (e, i)))
                        {
                            Check(element, prefix[index], root, $"{path}[{index}]", errors);
                        }

                        break;
                    case "items" when value.ValueKind == JsonValueKind.Array:
                        var skip = node["prefixItems"]?.AsArray().Count ?? 0;
                        foreach (var (element, index) in value.EnumerateArray().Select((e, i) => (e, i)).Skip(skip))
                        {
                            Check(element, argument, root, $"{path}[{index}]", errors);
                        }

                        break;
                    case "minItems" when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() < (int)argument!:
                        errors.Add($"{path}: has fewer than {argument} elements");
                        break;
                    case "maxItems" when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > (int)argument!:
                        errors.Add($"{path}: has more than {argument} elements");
                        break;
                    case "properties" or "required" or "additionalProperties" or "prefixItems" or "items" or "minItems" or "maxItems":
                        break;
                    default:
                        if (!Annotations.Contains(keyword))
                        {
                            errors.Add($"{path}: the check does not know the keyword {keyword}");
                        }

                        break;
                }
            }
        }

        private static string TypeOf(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null",
        };
    }
}

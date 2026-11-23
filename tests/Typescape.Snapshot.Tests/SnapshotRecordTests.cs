using System.Text.Json;
using Typescape.Snapshot.Records;
using Typescape.Snapshot.Serialization;
using Typescape.Testing;

namespace Typescape.Snapshot.Tests;

public class SnapshotRecordTests
{
    [Fact]
    public void SchemaVersion_is_the_first_property_of_the_root_object()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Sample(), SnapshotJson.Options);

        var reader = new Utf8JsonReader(json);
        reader.Read();
        reader.Read();

        Assert.Equal(JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("schemaVersion", reader.GetString());
        reader.Read();
        Assert.Equal(SnapshotRecord.CurrentSchemaVersion, reader.GetString());
    }

    [Fact]
    public void Root_holds_the_versions_and_the_entity_tables_in_order()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sample(), SnapshotJson.Options));

        var names = document.RootElement.EnumerateObject().Select(property => property.Name);

        Assert.Equal(
            [
                "schemaVersion", "rulesVersion", "projects", "namespaces", "documents", "types",
                "externalTypes", "executableMembers", "externalMembers", "nonExecutableMembers", "dependencies",
                "occurrences",
            ],
            names);
    }

    [Fact]
    public void Records_serialize_to_the_golden_shape()
    {
        var indented = new JsonSerializerOptions(SnapshotJson.Options) { WriteIndented = true };

        Golden.Match(JsonSerializer.Serialize(Sample(), indented) + "\n", "snapshot-tables", "json");
    }

    [Fact]
    public void Records_round_trip_through_json()
    {
        var json = JsonSerializer.Serialize(Sample(), SnapshotJson.Options);

        var read = JsonSerializer.Deserialize<SnapshotRecord>(json, SnapshotJson.Options);

        Assert.Equal(json, JsonSerializer.Serialize(read, SnapshotJson.Options));
    }

    [Fact]
    public void Reading_rejects_a_missing_required_property()
    {
        const string json = """{"key":"A/A.csproj","name":"A"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectRecord>(json, SnapshotJson.Options));
    }

    [Fact]
    public void Reading_rejects_an_enum_written_as_a_number()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DependencyKind>("2", SnapshotJson.Options));
    }

    [Fact]
    public void Dependency_kinds_serialize_to_the_specified_names()
    {
        var names = Enum.GetValues<DependencyKind>()
            .Select(kind => JsonSerializer.Serialize(kind, SnapshotJson.Options).Trim('"'));

        Assert.Equal(
            [
                "inherits", "implements", "member-type", "signature", "new",
                "call", "attribute", "generic-arg", "member-access", "type-usage",
            ],
            names);
    }

    /// <summary>
    /// App (a partial generic type with a nested record struct) references Core and uses Core and the BCL;
    /// App.Tests references App. The Occurrences reach all three kinds of target member: an External member (<c>List&lt;T&gt;.Add</c>),
    /// an own Executable Member (the <c>Service&lt;T&gt;</c> constructor) and an own field (<c>EntityBase.Id</c>).
    /// Every table is sorted by stable key, as Assemble writes it.
    /// </summary>
    internal static SnapshotRecord Sample()
    {
        var coreBase = StableKeys.Type("Core", "T:Core.EntityBase");
        var appService = StableKeys.Type("App", "T:App.Service`1");
        var appOptions = StableKeys.Type("App", "T:App.Service`1.Options");

        return new SnapshotRecord(
            SchemaVersion: SnapshotRecord.CurrentSchemaVersion,
            RulesVersion: 1,
            Projects:
            [
                new ProjectRecord("src/App.Tests/App.Tests.csproj", "App.Tests", "App.Tests", ProjectStyle.Sdk, "net10.0", true, "framework:xunit", [1]),
                new ProjectRecord("src/App/App.csproj", "App", "App", ProjectStyle.Sdk, "net10.0", false, null, [2]),
                new ProjectRecord("src/Core/Core.csproj", "Core", "Core", ProjectStyle.Legacy, "net48", false, null, []),
            ],
            Namespaces:
            [
                new NamespaceRecord(1, "App"),
                new NamespaceRecord(2, "Core"),
            ],
            Documents:
            [
                new DocumentRecord("src/App/Service.Generated.g.cs", 1, true, GeneratedReason.Convention),
                new DocumentRecord("src/App/Service.cs", 1, false, null),
                new DocumentRecord("src/Core/EntityBase.cs", 2, false, null),
            ],
            Types:
            [
                new TypeRecord(appService, "Service", DeclaredTypeKind.Class, DeclaredAccessibility.Public,
                    Abstract: false, Sealed: true, Static: false, Arity: 1, Namespace: 0, ContainingType: null,
                    [
                        new TypeDeclaration(0, new LineSpan(5, 12), 6, true),
                        new TypeDeclaration(1, new LineSpan(3, 40), 30, false),
                    ],
                    Loc: 36),
                new TypeRecord(appOptions, "Options", DeclaredTypeKind.RecordStruct, DeclaredAccessibility.Public,
                    Abstract: false, Sealed: true, Static: false, Arity: 0, Namespace: 0, ContainingType: 0,
                    [new TypeDeclaration(1, new LineSpan(36, 39), 1, false)],
                    Loc: 1),
                new TypeRecord(coreBase, "EntityBase", DeclaredTypeKind.Class, DeclaredAccessibility.Public,
                    Abstract: true, Sealed: false, Static: false, Arity: 0, Namespace: 1, ContainingType: null,
                    [new TypeDeclaration(2, new LineSpan(1, 9), 7, false)],
                    Loc: 7),
            ],
            ExternalTypes:
            [
                new ExternalTypeRecord("System.Runtime|T:System.Collections.Generic.List`1", "List<T>", "System.Runtime", "System.Collections.Generic", DeclaredTypeKind.Class),
            ],
            ExecutableMembers:
            [
                new ExecutableMemberRecord(StableKeys.Member(appService, "M:App.Service`1.#ctor"), 0, 1,
                    "Service<T>.Service()", ExecutableMemberKind.Constructor, DeclaredAccessibility.Public,
                    1, new LineSpan(8, 11), 4, 1, 0, false),
                new ExecutableMemberRecord(StableKeys.Member(appService, "M:App.Service`1.Run(System.Int32)"), 0, 1,
                    "Service<T>.Run(int)", ExecutableMemberKind.Method, DeclaredAccessibility.Public,
                    1, new LineSpan(13, 30), 16, 4, 6, false),
            ],
            ExternalMembers:
            [
                new ExternalMemberRecord("M:System.Collections.Generic.List`1.Add(`0)", 0, "List<T>.Add(T)"),
            ],
            NonExecutableMembers:
            [
                new NonExecutableMemberRecord(StableKeys.Member(coreBase, "F:Core.EntityBase.Id"), 2, "EntityBase.Id"),
            ],
            Dependencies:
            [
                new DependencyRecord(0, 2, External: false, DependencyKind.Inherits, Count: 1, GeneratedCount: 0),
                new DependencyRecord(0, 2, External: false, DependencyKind.MemberAccess, Count: 1, GeneratedCount: 0),
                new DependencyRecord(0, 0, External: true, DependencyKind.Call, Count: 3, GeneratedCount: 1),
                new DependencyRecord(1, 0, External: false, DependencyKind.New, Count: 1, GeneratedCount: 0),
            ],
            Occurrences:
            [
                new OccurrenceRecord(0, SourceMember: null, TargetMember: null, Document: 1, Line: 3),
                new OccurrenceRecord(1, SourceMember: 1, OccurrenceRecord.NonExecutableTarget(0), Document: 1, Line: 20),
                new OccurrenceRecord(2, SourceMember: null, TargetMember: 0, Document: 0, Line: 8),
                new OccurrenceRecord(2, SourceMember: 1, TargetMember: 0, Document: 1, Line: 15),
                new OccurrenceRecord(2, SourceMember: 1, TargetMember: 0, Document: 1, Line: 15),
                new OccurrenceRecord(3, SourceMember: null, TargetMember: 0, Document: 1, Line: 37),
            ]);
    }
}

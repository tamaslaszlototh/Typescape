using System.Reflection;
using System.Text.Json;
using Typescape.Snapshot.Records;
using Typescape.Snapshot.Json;

namespace Typescape.Snapshot.Tests.Records;

public class LoadHealthAndProvenanceTests
{
    [Fact]
    public void Load_statuses_serialize_to_the_specified_names()
    {
        Assert.Equal(["ok", "degraded", "failed"], Names<LoadStatus>());
    }

    [Fact]
    public void Workspace_diagnostic_kinds_serialize_to_the_specified_names()
    {
        Assert.Equal(["info", "warning", "failure"], Names<WorkspaceDiagnosticKind>());
    }

    [Fact]
    public void Solution_formats_serialize_to_the_specified_names()
    {
        Assert.Equal(["sln", "slnx"], Names<SolutionFormat>());
    }

    [Fact]
    public void Provenance_writes_the_timestamp_as_ISO_8601_and_properties_as_an_object()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(SnapshotRecordTests.SampleProvenance(), SnapshotJson.Options));
        var provenance = document.RootElement;

        Assert.Equal("2026-11-23T13:52:46+00:00", provenance.GetProperty("timestamp").GetString());
        Assert.Equal("""{"Configuration":"Release","Platform":"x64"}""", provenance.GetProperty("properties").GetRawText());
        Assert.Equal(JsonValueKind.Null, provenance.GetProperty("phaseSeconds").GetProperty("restore").ValueKind);
    }

    [Fact]
    public void Provenance_alone_holds_clock_and_measurement_values()
    {
        var offenders = ReachableRecordTypes(typeof(SnapshotRecord), skip: typeof(ProvenanceRecord))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsRunDependent(property.PropertyType))
                .Select(property => $"{type.Name}.{property.Name}"));

        Assert.Empty(offenders);
    }

    [Fact]
    public void Provenance_is_reachable_only_from_the_root()
    {
        var holders = ReachableRecordTypes(typeof(SnapshotRecord), skip: typeof(ProvenanceRecord))
            .Where(type => type != typeof(SnapshotRecord))
            .Where(type => type.GetProperties().Any(property => Unwrap(property.PropertyType).Contains(typeof(ProvenanceRecord))))
            .Select(type => type.Name);

        Assert.Empty(holders);
    }

    private static IEnumerable<string> Names<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => JsonSerializer.Serialize(value, SnapshotJson.Options).Trim('"'));

    private static bool IsRunDependent(Type type) =>
        Unwrap(type).Any(t => t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal));

    /// <summary>The records of the Snapshot assembly that <paramref name="root"/> reaches through its properties.</summary>
    private static HashSet<Type> ReachableRecordTypes(Type root, Type skip)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.TryPop(out var type))
        {
            if (type == skip || type.Assembly != typeof(SnapshotRecord).Assembly || type.IsEnum || !seen.Add(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var reached in Unwrap(property.PropertyType))
                {
                    pending.Push(reached);
                }
            }
        }

        return seen;
    }

    /// <summary><paramref name="type"/> and, for generic types such as lists, dictionaries and nullables, their type arguments.</summary>
    private static IEnumerable<Type> Unwrap(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type) : [type];
}

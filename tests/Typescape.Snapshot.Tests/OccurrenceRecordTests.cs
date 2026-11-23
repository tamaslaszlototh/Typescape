using System.Text.Json;

namespace Typescape.Snapshot.Tests;

public class OccurrenceRecordTests
{
    [Fact]
    public void Occurrence_is_written_as_a_five_element_tuple()
    {
        var occurrence = new OccurrenceRecord(2, SourceMember: 1, OccurrenceRecord.NonExecutableTarget(0), Document: 3, Line: 15);

        Assert.Equal("[2,1,-1,3,15]", JsonSerializer.Serialize(occurrence, SnapshotJson.Options));
    }

    [Fact]
    public void Missing_source_and_target_members_are_written_as_null()
    {
        var occurrence = new OccurrenceRecord(0, SourceMember: null, TargetMember: null, Document: 1, Line: 3);

        Assert.Equal("[0,null,null,1,3]", JsonSerializer.Serialize(occurrence, SnapshotJson.Options));
    }

    [Theory]
    [InlineData("[2,1,-1,3,15]")]
    [InlineData("[0,null,null,1,3]")]
    public void Tuple_round_trips(string json)
    {
        var occurrence = JsonSerializer.Deserialize<OccurrenceRecord>(json, SnapshotJson.Options);

        Assert.Equal(json, JsonSerializer.Serialize(occurrence, SnapshotJson.Options));
    }

    [Theory]
    [InlineData("""{"dependency":0,"document":1,"line":3}""")]
    [InlineData("[0,null,null,1]")]
    [InlineData("[0,null,null,1,3,4]")]
    [InlineData("[null,null,null,1,3]")]
    [InlineData("[0,null,null,null,3]")]
    [InlineData("[0,null,null,1,null]")]
    [InlineData("""[0,"a",null,1,3]""")]
    [InlineData("[0,1.5,null,1,3]")]
    [InlineData("[0,null,null,1,3000000000]")]
    [InlineData("null")]
    public void Reading_rejects_anything_but_the_tuple(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OccurrenceRecord>(json, SnapshotJson.Options));
    }

    [Fact]
    public void Reading_rejects_a_null_occurrence_in_the_section()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<IReadOnlyList<OccurrenceRecord>>("[[0,null,null,1,3],null]", SnapshotJson.Options));
    }

    [Fact]
    public void Non_executable_target_is_the_complement_of_the_row_index()
    {
        Assert.Equal(-1, OccurrenceRecord.NonExecutableTarget(0));
        Assert.Equal(-8, OccurrenceRecord.NonExecutableTarget(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => OccurrenceRecord.NonExecutableTarget(-1));
    }

    [Fact]
    public void Snapshot_without_occurrences_writes_null_and_reads_back()
    {
        var snapshot = SnapshotRecordTests.Sample() with { Occurrences = null, ExternalMembers = [], NonExecutableMembers = [] };
        var json = JsonSerializer.Serialize(snapshot, SnapshotJson.Options);

        var read = JsonSerializer.Deserialize<SnapshotRecord>(json, SnapshotJson.Options);

        Assert.Contains("\"occurrences\":null", json, StringComparison.Ordinal);
        Assert.Null(read!.Occurrences);
        Assert.Equal(snapshot.Dependencies.Count, read.Dependencies.Count);
    }

    [Fact]
    public void Target_member_resolves_to_an_External_member()
    {
        var snapshot = SnapshotRecordTests.Sample();

        var target = snapshot.ResolveTargetMember(new OccurrenceRecord(2, 1, 0, 1, 15));

        Assert.Equal(new MemberReference("M:System.Collections.Generic.List`1.Add(`0)", "List<T>.Add(T)"), target);
    }

    [Fact]
    public void Target_member_resolves_to_an_own_Executable_Member()
    {
        var snapshot = SnapshotRecordTests.Sample();

        var target = snapshot.ResolveTargetMember(new OccurrenceRecord(3, null, 0, 1, 37));

        Assert.Equal(new MemberReference("M:App.Service`1.#ctor", "Service<T>.Service()"), target);
    }

    [Fact]
    public void Target_member_resolves_to_an_own_non_executable_member()
    {
        var snapshot = SnapshotRecordTests.Sample();

        var target = snapshot.ResolveTargetMember(new OccurrenceRecord(1, 1, OccurrenceRecord.NonExecutableTarget(0), 1, 20));

        Assert.Equal(new MemberReference("F:Core.EntityBase.Id", "EntityBase.Id"), target);
    }

    [Fact]
    public void Occurrence_without_a_target_member_resolves_to_null()
    {
        var snapshot = SnapshotRecordTests.Sample();

        Assert.Null(snapshot.ResolveTargetMember(new OccurrenceRecord(0, null, null, 1, 3)));
    }

    [Fact]
    public void Every_sample_occurrence_matches_its_dependency_counts()
    {
        var snapshot = SnapshotRecordTests.Sample();

        var counts = snapshot.Occurrences!.GroupBy(occurrence => occurrence.Dependency)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(snapshot.Dependencies.Select(dependency => dependency.Count), snapshot.Dependencies.Select((_, index) => counts[index]));
    }
}

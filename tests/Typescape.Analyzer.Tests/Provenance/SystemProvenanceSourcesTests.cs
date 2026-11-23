using System.Text.Json;
using System.Text.RegularExpressions;
using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Provenance;
using Typescape.Analyzer.Tests.Fakes;
using Typescape.Snapshot.Json;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.Provenance;

public sealed partial class SystemProvenanceSourcesTests : IDisposable
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";

    private readonly SystemProvenanceSources _sources = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-provenance-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("0.1.0+" + Sha1, "0.1.0", Sha1)]
    [InlineData("0.1.1-alpha.0.3+" + Sha1, "0.1.1-alpha.0.3", Sha1)]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+build.7", "0.1.0", null)]
    [InlineData("0.1.0+0123ABCDEF0123456789abcdef0123456789ab", "0.1.0", null)]
    public void The_tool_version_and_commit_come_from_the_informational_version(string informational, string version, string? commit)
    {
        Assert.Equal(new ToolProvenanceRecord(version, commit), SystemProvenanceSources.ParseTool(informational));
    }

    [Fact]
    public void The_tool_is_this_build_with_its_commit()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", _sources.Tool.Version);
        Assert.DoesNotContain('+', _sources.Tool.Version);
        Assert.Matches("^[0-9a-f]{40}$", _sources.Tool.Commit ?? Sha1);
    }

    [Fact]
    public void The_OS_names_the_system_and_architecture_but_not_the_machine()
    {
        Assert.False(string.IsNullOrWhiteSpace(_sources.Os));
        Assert.EndsWith(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), _sources.Os);
        Assert.DoesNotContain(Environment.MachineName, _sources.Os, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_sdk_version_is_the_one_dotnet_picks_in_the_solution_directory()
    {
        var version = await _sources.GetSdkVersionAsync(_directory, TestContext.Current.CancellationToken);

        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void The_peak_memory_is_measured()
    {
        Assert.True(_sources.GetPeakMemoryBytes() > 0);
    }

    [Fact]
    public async Task Outside_a_repository_there_is_no_git_state()
    {
        Assert.Null(await GitAsync());
    }

    [Fact]
    public async Task A_repository_without_a_commit_has_no_git_state()
    {
        await Git("init", "--quiet", "--initial-branch=main");

        Assert.Null(await GitAsync());
    }

    [Fact]
    public async Task A_clean_checkout_records_its_commit_and_branch()
    {
        var commit = await CommitAsync();

        Assert.Equal(new GitProvenanceRecord(commit, "main", Dirty: false), await GitAsync());
    }

    [Fact]
    public async Task A_modified_file_makes_the_working_tree_dirty()
    {
        await CommitAsync();
        await File.AppendAllTextAsync(Path.Combine(_directory, "A.cs"), "// changed\n", TestContext.Current.CancellationToken);

        Assert.True((await GitAsync())?.Dirty);
    }

    [Fact]
    public async Task An_untracked_file_makes_the_working_tree_dirty()
    {
        await CommitAsync();
        await File.WriteAllTextAsync(Path.Combine(_directory, "B.cs"), "class B { }\n", TestContext.Current.CancellationToken);

        Assert.True((await GitAsync())?.Dirty);
    }

    [Fact]
    public async Task A_detached_head_has_no_branch()
    {
        var commit = await CommitAsync();
        await Git("checkout", "--quiet", "--detach");

        Assert.Equal(new GitProvenanceRecord(commit, null, Dirty: false), await GitAsync());
    }

    [Fact]
    public async Task The_git_state_is_read_from_a_solution_directory_below_the_repository_root()
    {
        var commit = await CommitAsync();
        var below = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;

        var git = await _sources.GetGitAsync(below, TestContext.Current.CancellationToken);

        Assert.Equal(new GitProvenanceRecord(commit, "main", Dirty: false), git);
    }

    [Fact]
    public async Task Provenance_holds_no_absolute_path_and_no_machine_name()
    {
        await CommitAsync();
        var request = new AnalysisRequest
        {
            SolutionPath = Path.Combine(_directory, "Sample.slnx"),
            Properties = new Dictionary<string, string> { ["Configuration"] = "Release" },
        };

        var result = await AnalysisPipeline.RunAsync(request, new FakeStages(1), _sources, null, TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(result.Snapshot.Provenance, SnapshotJson.Options);
        Assert.DoesNotContain(_directory, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(_directory), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(AbsolutePath(), json);
        Assert.Equal("Sample.slnx", result.Snapshot.Provenance.Solution.FileName);
    }

    // A JSON string starting with a rooted path: /x, C:\ or \\server.
    [GeneratedRegex(@"""(/[^/""]|[A-Za-z]:\\\\|\\\\\\\\)")]
    private static partial Regex AbsolutePath();

    private Task<GitProvenanceRecord?> GitAsync() => _sources.GetGitAsync(_directory, TestContext.Current.CancellationToken);

    private async Task<string> CommitAsync()
    {
        await Git("init", "--quiet", "--initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(_directory, "A.cs"), "class A { }\n", TestContext.Current.CancellationToken);
        await Git("add", "A.cs");
        await Git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "--quiet", "-m", "Initial");
        return (await Git("rev-parse", "HEAD")).Trim();
    }

    private async Task<string> Git(params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync(
            "git", arguments, _directory, new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output;
    }
}

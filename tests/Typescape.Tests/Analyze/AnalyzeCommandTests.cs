using System.CommandLine;
using Typescape.Analyzer;

namespace Typescape.Tests.Analyze;

public sealed class AnalyzeCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-analyze-").FullName;
    private readonly List<AnalysisRequest> _requests = [];

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task The_Snapshot_goes_to_the_current_directory_and_only_its_path_to_stdout()
    {
        var solution = Touch("src/App.slnx");

        var run = await RunAsync("analyze", "src");

        var path = Path.Combine(_directory, "App.typescape.json.gz");
        Assert.Equal((0, path + Environment.NewLine, ""), (run.ExitCode, run.Stdout, run.Stderr));
        Assert.True(File.Exists(path));
        Assert.Equal(solution, Assert.Single(_requests).SolutionPath);
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    public async Task The_output_option_overrides_the_path_relative_to_the_current_directory(string option)
    {
        Touch("App.sln");
        Directory.CreateDirectory(Path.Combine(_directory, "out"));

        var run = await RunAsync("analyze", option, "out/snapshot.gz");

        var path = Path.Combine(_directory, "out", "snapshot.gz");
        Assert.Equal((0, path + Environment.NewLine), (run.ExitCode, run.Stdout));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(_directory, "App.typescape.json.gz")));
    }

    [Theory]
    [InlineData]
    [InlineData("App/App.csproj")]
    [InlineData("Missing.sln")]
    public async Task An_unusable_input_exits_2_on_stderr_without_analyzing(params string[] input)
    {
        Touch("App/App.csproj");

        var run = await RunAsync(["analyze", .. input]);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.NotEqual("", run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Several_solutions_exit_2_and_list_them()
    {
        Touch("App.sln");
        Touch("App.slnx");

        var run = await RunAsync("analyze");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("  App.sln" + Environment.NewLine + "  App.slnx", run.Stderr);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var root = Program.CreateRootCommand(_directory, (request, _) =>
        {
            _requests.Add(request);
            return Task.FromResult(new AnalysisResult(Snapshots.Empty(), new LoadHealthSummary(0, 0, 0)));
        });
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await root.Parse(args).InvokeAsync(
            new InvocationConfiguration { Output = stdout, Error = stderr },
            TestContext.Current.CancellationToken);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}

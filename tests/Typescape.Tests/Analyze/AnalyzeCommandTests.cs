using System.CommandLine;
using Typescape.Analyze;
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

    [Theory]
    [InlineData("-f")]
    [InlineData("--framework")]
    public async Task The_framework_option_names_the_target_framework_to_analyze(string option)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", option, "net8.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("net8.0", Assert.Single(_requests).Framework);
    }

    [Fact]
    public async Task Max_parallelism_defaults_to_the_core_count_and_the_option_overrides_it()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--max-parallelism", "1");

        Assert.Equal([Environment.ProcessorCount, 1], _requests.Select(request => request.MaxParallelism));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public async Task Max_parallelism_below_1_or_not_a_number_exits_2(string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", "--max-parallelism", value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains("--max-parallelism", run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Occurrences_and_restore_are_on_unless_switched_off()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--no-occurrences");
        await RunAsync("analyze", "--no-restore");

        Assert.Equal(
            [(true, true), (false, true), (true, false)],
            _requests.Select(request => (request.Occurrences, request.Restore)));
    }

    [Theory]
    [InlineData("-p", "Configuration=Release")]
    [InlineData("-p:Configuration=Release")]
    [InlineData("--property", "Configuration=Release")]
    [InlineData("--property:Configuration=Release")]
    public async Task A_property_is_an_MSBuild_global_property(params string[] flag)
    {
        Touch("App.sln");

        await RunAsync(["analyze", .. flag]);

        Assert.Equal(
            new Dictionary<string, string> { ["Configuration"] = "Release" },
            Assert.Single(_requests).Properties);
    }

    [Fact]
    public async Task Properties_repeat_their_value_runs_to_the_end_and_a_later_one_wins_in_any_case()
    {
        Touch("App.sln");

        await RunAsync("analyze", "-p", "DefineConstants=A=1", "-p:Empty=", "-p", "platform=x86", "-p", "Platform=x64");

        var properties = Assert.Single(_requests).Properties;
        Assert.Equal(3, properties.Count);
        Assert.Equal(("A=1", "", "x64"), (properties["DefineConstants"], properties["Empty"], properties["PLATFORM"]));
    }

    [Fact]
    public async Task A_property_takes_one_token_and_leaves_the_input_after_it()
    {
        var solution = Touch("src/App.slnx");

        await RunAsync("analyze", "-p", "A=1", "src");

        Assert.Equal(solution, Assert.Single(_requests).SolutionPath);
    }

    [Theory]
    [InlineData("Configuration")]
    [InlineData("=Release")]
    public async Task A_property_without_a_name_and_an_equals_sign_exits_2(string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", "-p", value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(value, run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Test_and_not_test_patterns_repeat_and_default_to_none()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--test", "*.Specs", "--not-test", "Gallery.Tests", "--test", "Acceptance?");

        Assert.Equal(
            [([], []), (["*.Specs", "Acceptance?"], ["Gallery.Tests"])],
            _requests.Select(request => (request.TestPatterns, request.NotTestPatterns)));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("None", "--fail-on", "none")]
    [InlineData("Degraded", "--fail-on", "degraded")]
    [InlineData("Failed", "--fail-on", "failed")]
    public void Fail_on_defaults_to_none(string expected, params string[] flag) =>
        Assert.Equal(Enum.Parse<FailOn>(expected), Parse(["analyze", .. flag]).GetValue<FailOn>("--fail-on"));

    [Theory]
    [InlineData("Normal")]
    [InlineData("Quiet", "-v", "quiet")]
    [InlineData("Normal", "--verbosity", "normal")]
    [InlineData("Detailed", "-v", "detailed")]
    public void Verbosity_defaults_to_normal(string expected, params string[] flag) =>
        Assert.Equal(Enum.Parse<Verbosity>(expected), Parse(["analyze", .. flag]).GetValue<Verbosity>("--verbosity"));

    [Theory]
    [InlineData("--fail-on", "warning")]
    [InlineData("--verbosity", "minimal")]
    public async Task A_fail_on_or_verbosity_outside_its_values_exits_2(string option, string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", option, value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(value, run.Stderr);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("analyze", "--help")]
    [InlineData("analyze", "-h")]
    public async Task Help_works_on_every_command_without_analyzing(params string[] args)
    {
        var run = await RunAsync(args);

        Assert.Equal((0, ""), (run.ExitCode, run.Stderr));
        Assert.Contains("Usage:", run.Stdout);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Analyze_help_lists_every_documented_flag()
    {
        var run = await RunAsync("analyze", "--help");

        Assert.All(
            [
                "-o, --output", "-f, --framework <tfm>", "--max-parallelism <n>", "--no-occurrences", "--no-restore",
                "-p, --property <Name=Value>", "--test <pattern>", "--not-test <pattern>",
                "--fail-on <none|degraded|failed>", "-v, --verbosity <quiet|normal|detailed>",
            ],
            flag => Assert.Contains(flag, run.Stdout));
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("-c", "Release")]
    [InlineData("--configuration", "Release")]
    public async Task An_unknown_flag_exits_2_on_stderr_without_analyzing(params string[] flag)
    {
        Touch("App.sln");

        var run = await RunAsync(["analyze", .. flag]);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(flag[0], run.Stderr);
        Assert.Empty(_requests);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await Program.InvokeAsync(
            CreateRootCommand(),
            args,
            new InvocationConfiguration { Output = stdout, Error = stderr },
            TestContext.Current.CancellationToken);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private ParseResult Parse(string[] args) => CreateRootCommand().Parse(args);

    private RootCommand CreateRootCommand() =>
        Program.CreateRootCommand(_directory, (request, _) =>
        {
            _requests.Add(request);
            return Task.FromResult(new AnalysisResult(Snapshots.Empty(), new LoadHealthSummary(0, 0, 0)));
        });

    private string Touch(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_directory, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}

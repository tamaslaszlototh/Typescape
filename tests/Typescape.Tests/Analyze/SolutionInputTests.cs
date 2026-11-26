using Typescape.Analyze;

namespace Typescape.Tests.Analyze;

public sealed class SolutionInputTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-input-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("App.SLN")]
    public void A_directory_with_one_solution_resolves_to_it(string name)
    {
        var solution = Touch(name);
        Touch("App/App.csproj");
        Touch("Nested/Other.sln");

        Assert.True(SolutionInput.TryResolve(_directory, Path.GetTempPath(), out var resolved, out _));
        Assert.Equal(solution, resolved);
    }

    [Fact]
    public void No_input_is_the_current_directory()
    {
        var solution = Touch("App.slnx");

        Assert.True(SolutionInput.TryResolve(null, _directory, out var resolved, out _));
        Assert.Equal(solution, resolved);
    }

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    public void A_solution_file_resolves_to_itself_relative_to_the_current_directory(string name)
    {
        var solution = Touch($"src/{name}");
        Touch("src/Other.sln");

        Assert.True(SolutionInput.TryResolve($"src/{name}", _directory, out var resolved, out _));
        Assert.Equal(solution, resolved);
    }

    [Fact]
    public void A_directory_without_a_solution_is_an_error()
    {
        Touch("App.csproj");

        Assert.False(SolutionInput.TryResolve(null, _directory, out _, out var error));
        Assert.Equal($"No solution file (.sln or .slnx) in '{_directory}'.", error);
    }

    [Fact]
    public void A_directory_with_several_solutions_lists_them()
    {
        Touch("B.sln");
        Touch("A.slnx");
        Touch("A.sln");

        Assert.False(SolutionInput.TryResolve(null, _directory, out _, out var error));
        Assert.Equal(
            [$"Several solution files in '{_directory}'; pass the one to analyze:", "  A.sln", "  A.slnx", "  B.sln"],
            error.Split(Environment.NewLine));
    }

    [Fact]
    public void A_project_file_is_rejected()
    {
        Touch("App/App.csproj");

        Assert.False(SolutionInput.TryResolve("App/App.csproj", _directory, out _, out var error));
        Assert.Equal("'App.csproj' is a project file; pass its solution (.sln or .slnx) or the solution's directory.", error);
    }

    [Fact]
    public void Another_file_is_rejected()
    {
        Touch("App.typescape.json.gz");

        Assert.False(SolutionInput.TryResolve("App.typescape.json.gz", _directory, out _, out var error));
        Assert.Equal("'App.typescape.json.gz' is not a solution file (.sln or .slnx).", error);
    }

    [Fact]
    public void A_missing_path_is_an_error()
    {
        Assert.False(SolutionInput.TryResolve("Missing.sln", _directory, out _, out var error));
        Assert.Equal("'Missing.sln' does not exist.", error);
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}

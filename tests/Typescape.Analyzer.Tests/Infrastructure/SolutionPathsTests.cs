using Typescape.Analyzer.Infrastructure;

namespace Typescape.Analyzer.Tests.Infrastructure;

public class SolutionPathsTests
{
    [Theory]
    [InlineData("/repo", "Msbuild failed when processing the file '/repo/App/App.csproj'", "Msbuild failed when processing the file 'App/App.csproj'")]
    [InlineData("/repo/", "/repo/A.csproj references /REPO/B/B.csproj", "A.csproj references B/B.csproj")]
    [InlineData(@"C:\repo", @"C:\repo\App\App.csproj and C:/repo/Lib/Lib.csproj", @"App\App.csproj and Lib/Lib.csproj")]
    [InlineData("/repo", "/repository/App.csproj and /other/repo/App.csproj stay", "/repository/App.csproj and /other/repo/App.csproj stay")]
    public void Paths_under_the_solution_directory_become_relative(string directory, string text, string expected)
    {
        Assert.Equal(expected, SolutionPaths.Relativize(text, directory));
    }

    [Theory]
    [InlineData("/repo", "/repo/App/Orders/Order.cs", "App/Orders/Order.cs")]
    [InlineData("/repo/", "/repo/App/App.csproj", "App/App.csproj")]
    [InlineData("/repo", "/Shared/Link.cs", "../Shared/Link.cs")]
    public void A_key_is_the_path_relative_to_the_solution_directory_with_forward_slashes(string directory, string path, string expected)
    {
        Assert.Equal(expected, SolutionPaths.Key(Path.GetFullPath(path), Path.GetFullPath(directory)));
    }

    [Fact]
    public void A_relative_path_is_taken_as_relative_to_the_solution_directory_not_the_working_directory()
    {
        Assert.Equal("App/obj/Gen/Hint.g.cs", SolutionPaths.Key("App/obj/Gen/Hint.g.cs", Path.GetFullPath("/repo")));
    }
}

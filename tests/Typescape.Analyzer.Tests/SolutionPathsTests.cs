namespace Typescape.Analyzer.Tests;

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
}

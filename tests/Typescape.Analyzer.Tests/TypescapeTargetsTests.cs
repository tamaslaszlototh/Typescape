namespace Typescape.Analyzer.Tests;

/// <summary>
/// Evaluates throwaway projects with the real <c>dotnet msbuild</c> (.NET SDK MSBuild) under <see cref="GlobalProperties"/>,
/// and asks whether the .NET SDK's <c>Microsoft.WinFX.targets</c> came in: only it sets <c>_PresentationBuildTasksAssembly</c>.
/// </summary>
public sealed class TypescapeTargetsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-targets-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_classic_project_gets_the_WPF_markup_compiler()
    {
        var project = Write("Classic.csproj", """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);

        Assert.EndsWith("PresentationBuildTasks.dll", await WinFXTasksAsync(project));
    }

    [Fact]
    public async Task An_SDK_style_project_is_left_alone()
    {
        var project = Write("Modern.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        Assert.Equal("", await WinFXTasksAsync(project));
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<string> WinFXTasksAsync(string project)
    {
        var properties = GlobalProperties.For(new AnalysisRequest { SolutionPath = project });
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            ["msbuild", project, "-getProperty:_PresentationBuildTasksAssembly", .. properties.Select(p => $"-p:{p.Key}={p.Value}")],
            _directory,
            ProcessRunner.QuietEnvironment,
            TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        return result.Output.Trim();
    }
}

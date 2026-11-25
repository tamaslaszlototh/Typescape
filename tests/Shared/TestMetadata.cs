using System.Reflection;

namespace Typescape.Testing;

/// <summary>Source directories that <c>tests/Directory.Build.props</c> records in the test assembly.</summary>
internal static class TestMetadata
{
    /// <summary>The test project's directory.</summary>
    public static string ProjectDirectory { get; } = Get("Typescape.ProjectDirectory");

    /// <summary>The repository root.</summary>
    public static string RepositoryDirectory { get; } = Get("Typescape.RepositoryDirectory");

    private static string Get(string key) =>
        typeof(TestMetadata).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value!;
}

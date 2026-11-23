using System.Reflection;
using System.Text.Json;

namespace Typescape.Snapshot.Tests.Architecture;

/// <summary>
/// Typescape.Snapshot may use only the .NET shared framework (System.Text.Json, System.IO.Compression, ...):
/// no packages and no project references, so `typescape view` never loads Roslyn or MSBuild.
/// </summary>
public class DependencyDirectionTests
{
    [Fact]
    public void Snapshot_assembly_references_only_framework_assemblies()
    {
        var snapshot = Assembly.Load("Typescape.Snapshot");

        var nonFramework = snapshot.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !IsFrameworkAssembly(name))
            .ToList();

        Assert.Empty(nonFramework);
    }

    [Fact]
    public void Snapshot_project_has_no_package_or_project_dependencies()
    {
        // The deps file records each library's direct dependencies. Framework assemblies are not listed,
        // so Typescape.Snapshot's entry must have none: every package it gained would appear here.
        var depsFile = Path.Combine(AppContext.BaseDirectory, "Typescape.Snapshot.Tests.deps.json");
        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));

        var snapshotEntries = deps.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Name.StartsWith("Typescape.Snapshot/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(snapshotEntries);
        foreach (var entry in snapshotEntries)
        {
            var dependencies = entry.Value.TryGetProperty("dependencies", out var value)
                ? value.EnumerateObject().Select(dependency => dependency.Name).ToList()
                : [];
            Assert.Empty(dependencies);
        }
    }

    private static bool IsFrameworkAssembly(string name) =>
        name is "System" or "netstandard" or "mscorlib" ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
}

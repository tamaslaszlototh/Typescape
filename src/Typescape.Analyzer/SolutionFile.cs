using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Typescape.Analyzer;

/// <summary>Reads a <c>.sln</c> or <c>.slnx</c> with the parser MSBuildWorkspace uses.</summary>
internal static class SolutionFile
{
    /// <param name="solution">The absolute path of the solution file.</param>
    /// <returns>The absolute paths of the solution's projects, solution folders excluded, each once.</returns>
    /// <exception cref="InvalidOperationException">Not a <c>.sln</c> or <c>.slnx</c> file.</exception>
    public static async Task<IReadOnlyList<string>> ProjectPathsAsync(string solution, CancellationToken cancellationToken)
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution)
            ?? throw new InvalidOperationException($"'{Path.GetFileName(solution)}' is not a .sln or .slnx file.");
        var model = await serializer.OpenAsync(solution, cancellationToken);
        var directory = Path.GetDirectoryName(solution)!;

        return
        [
            .. model.SolutionProjects
                .Select(project => Path.GetFullPath(project.FilePath.Replace('\\', Path.DirectorySeparatorChar), directory))
                .Distinct(StringComparer.Ordinal),
        ];
    }
}

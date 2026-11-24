using System.Diagnostics;
using System.Reflection;

namespace Typescape.Analyzer.Extraction;

/// <summary>Exceptions in Typescape's own walker, caught so one Roslyn edge case cannot kill a run.</summary>
internal static class AnalyzerErrors
{
    /// <summary>
    /// Runs <paramref name="walk"/>. When it throws, its facts are only the analyzer error, pinned to
    /// <paramref name="document"/>; whatever the walk found before the exception is dropped.
    /// </summary>
    /// <param name="document">The key of the document the walk is about.</param>
    public static ProjectFacts Catch(string document, Func<ProjectFacts> walk, CancellationToken cancellationToken)
    {
        try
        {
            return walk();
        }
        catch (Exception exception) when (Recordable(exception, cancellationToken))
        {
            var type = exception.GetType();
            return new ProjectFacts([], [], [new AnalyzerErrorFact(document, type.FullName ?? type.Name, TopFrame(exception))]);
        }
    }

    // Out of memory leaves nothing worth continuing with.
    private static bool Recordable(Exception exception, CancellationToken cancellationToken) =>
        exception is not OutOfMemoryException
        && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);

    // The topmost frame in Typescape's own code: frames above it are runtime throw helpers or Roslyn internals, which
    // say little about where the walker broke and change with the runtime. The method alone: the file and line would
    // hold an absolute path from the machine that built the tool.
    private static string TopFrame(Exception exception)
    {
        var methods = new StackTrace(exception, fNeedFileInfo: false).GetFrames().Select(frame => frame.GetMethod()).OfType<MethodBase>().ToArray();
        if ((methods.FirstOrDefault(method => method.DeclaringType?.Assembly == typeof(AnalyzerErrors).Assembly) ?? methods.FirstOrDefault())
            is not { } method)
        {
            return "<unknown>";
        }

        var parameters = string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{TypeName(method.DeclaringType)}.{method.Name}({parameters})";
    }

    // A constructed generic type's FullName holds its type arguments' assembly-qualified names, versions included.
    private static string TypeName(Type? type) => type switch
    {
        null => "<global>",
        { IsGenericType: true, IsGenericTypeDefinition: false } => TypeName(type.GetGenericTypeDefinition()),
        _ => type.FullName ?? type.Name,
    };
}

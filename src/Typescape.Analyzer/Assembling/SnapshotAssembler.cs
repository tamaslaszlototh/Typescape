using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Assembling;

/// <summary>
/// Merges the <see cref="ProjectFacts"/> of every project into the Snapshot: the only place where determinism is decided.
/// </summary>
/// <remarks>
/// <para>
/// Every table is sorted by its stable key, ordinally, and every reference becomes an array index, so the order in which
/// the facts arrive never shows in the result.
/// </para>
/// <para>
/// Where several projects state the same key, the project first in key order wins: a document linked into two projects
/// is one row, owned by the first. An own type wins over an External Type of the same key (a project referenced as a
/// DLL), and the External members of that type become its members.
/// </para>
/// <para>
/// A Dependency or Occurrence whose own type or member is missing, because its declaration's walk failed with an
/// analyzer error, is left out.
/// </para>
/// </remarks>
internal static class SnapshotAssembler
{
    /// <summary>The <see cref="SnapshotRecord.RulesVersion"/> this Analyzer writes. Bump it whenever the same code would give a different Snapshot.</summary>
    public const int RulesVersion = 1;

    public static SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var projects = Projects(input);
        var projectIndex = Index(projects, project => project.Key);
        var facts = projects.Select(project => project.Facts).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var documents = FirstByKey(facts.SelectMany((part, p) => part.Documents.Select(document => (p, document))), entry => entry.document.Path)
            .Select(entry => new DocumentRecord(entry.document.Path, entry.p, entry.document.GeneratedReason is not null, entry.document.GeneratedReason))
            .ToArray();
        var documentIndex = Index(documents, document => document.Path);

        var ownTypes = FirstByKey(facts.SelectMany((part, p) => part.Types.Select(type => (p, type))), entry => entry.type.Key);
        var namespaces = ownTypes
            .Select(entry => new NamespaceRecord(entry.p, entry.type.Namespace))
            .Distinct()
            .OrderBy(row => row.Project)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToArray();
        var namespaceIndex = Index(namespaces, row => (row.Project, row.Name));
        var typeIndex = Index(ownTypes, entry => entry.type.Key);
        var types = ownTypes.Select(entry => Type(entry.type, entry.p, typeIndex, namespaceIndex, documentIndex)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var externalTypes = FirstByKey(facts.SelectMany(part => part.ExternalTypes), type => type.Key)
            .Where(type => !typeIndex.ContainsKey(type.Key))
            .Select(type => new ExternalTypeRecord(type.Key, type.Name, type.Assembly, type.Namespace, type.Kind))
            .ToArray();
        var externalTypeIndex = Index(externalTypes, type => type.Key);

        var executableMembers = FirstByKey(facts.SelectMany(part => part.ExecutableMembers), member => member.Key)
            .Select(member => typeIndex.TryGetValue(member.Type, out var type) ? ExecutableMember(member, type, types, documentIndex) : null)
            .OfType<ExecutableMemberRecord>()
            .ToArray();
        var executableIndex = Index(executableMembers, member => member.Key);
        cancellationToken.ThrowIfCancellationRequested();

        var targets = new TargetMembers(input.Request.Occurrences, facts, typeIndex, externalTypeIndex, executableIndex);
        var dependencies = Dependencies(facts, typeIndex, externalTypeIndex);
        var dependencyIndex = Index(dependencies, dependency => (dependency.Source, dependency.Target, dependency.External, dependency.Kind));
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OccurrenceRecord>? occurrences = input.Request.Occurrences
            ? Occurrences(facts, typeIndex, externalTypeIndex, executableIndex, documentIndex, dependencyIndex, targets)
            : null;
        cancellationToken.ThrowIfCancellationRequested();

        return new SnapshotRecord(
            SnapshotRecord.CurrentSchemaVersion,
            RulesVersion,
            [.. projects.Select(project => project.Record(projectIndex))],
            namespaces,
            documents,
            types,
            externalTypes,
            executableMembers,
            targets.External,
            targets.NonExecutable,
            [.. dependencies.Select(dependency => dependency.Record)],
            new LoadHealthRecord(
                [.. projects.Select((project, p) => project.Health(p, documentIndex))],
                input.Solution.SolutionDiagnostics),
            input.Provenance,
            occurrences);
    }

    private static AssembledProject[] Projects(AssembleInput input) =>
    [
        .. input.Projects
            .Select(result => new AssembledProject(
                result.Project.Project.Key,
                result.Project.Project.Project.AssemblyName,
                result.Project.Project,
                Style: result.Project.Project.Style,
                result.Diagnostics,
                ProjectFacts.Combine(result.Facts),
                WorkspaceDiagnostics: result.Project.Project.WorkspaceDiagnostics))
            .Concat(input.Solution.UnopenedProjects.Select(project => new AssembledProject(
                project.Key,
                Path.GetFileNameWithoutExtension(project.Key),
                Loaded: null,
                project.Style,
                Diagnostics: null,
                ProjectFacts.Empty,
                project.WorkspaceDiagnostics)))
            .OrderBy(project => project.Key, StringComparer.Ordinal),
    ];

    private static TypeRecord Type(
        TypeFact type,
        int project,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<(int, string), int> namespaceIndex,
        IReadOnlyDictionary<string, int> documentIndex) =>
        new(
            type.Key,
            type.Name,
            type.Kind,
            type.Accessibility,
            type.Abstract,
            type.Sealed,
            type.Static,
            type.Arity,
            namespaceIndex[(project, type.Namespace)],
            type.ContainingType is { } containing && typeIndex.TryGetValue(containing, out var index) ? index : null,
            [
                .. type.Declarations
                    .Select(declaration => new TypeDeclaration(documentIndex[declaration.Document], declaration.Lines, declaration.Loc, declaration.Generated))
                    .OrderBy(declaration => declaration.Document)
                    .ThenBy(declaration => declaration.Lines.Start),
            ],
            type.Loc);

    private static ExecutableMemberRecord? ExecutableMember(
        ExecutableMemberFact member,
        int type,
        IReadOnlyList<TypeRecord> types,
        IReadOnlyDictionary<string, int> documentIndex)
    {
        var document = documentIndex[member.Document];
        var declarations = types[type].Declarations;
        var declaration = Enumerable.Range(0, declarations.Count)
            .Where(d =>
                declarations[d].Document == document
                && declarations[d].Lines.Start <= member.Lines.Start
                && member.Lines.End <= declarations[d].Lines.End)
            .Cast<int?>()
            .FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return new ExecutableMemberRecord(
            member.Key,
            type,
            declaration.Value,
            member.Signature,
            member.Kind,
            member.Accessibility,
            document,
            member.Lines,
            member.Loc,
            member.Cyclomatic,
            member.Cognitive,
            member.Generated);
    }

    private static AssembledDependency[] Dependencies(
        IEnumerable<ProjectFacts> facts,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<string, int> externalTypeIndex) =>
    [
        .. facts
            .SelectMany(part => part.Dependencies)
            .GroupBy(dependency => (dependency.Source, dependency.Target, dependency.Kind))
            .Select(group => Dependency(group.Key, group.Sum(d => d.Count), group.Sum(d => d.GeneratedCount), typeIndex, externalTypeIndex))
            .OfType<AssembledDependency>()
            .OrderBy(dependency => dependency.SourceKey, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.TargetKey, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Kind),
    ];

    private static AssembledDependency? Dependency(
        (string Source, string Target, DependencyKind Kind) key,
        int count,
        int generatedCount,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<string, int> externalTypeIndex) =>
        typeIndex.TryGetValue(key.Source, out var source) && Target(key.Target, typeIndex, externalTypeIndex) is { } target
            ? new AssembledDependency(key.Source, key.Target, new DependencyRecord(source, target.Index, target.External, key.Kind, count, generatedCount))
            : null;

    private static (int Index, bool External)? Target(
        string key,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<string, int> externalTypeIndex) =>
        typeIndex.TryGetValue(key, out var own) ? (own, false)
        : externalTypeIndex.TryGetValue(key, out var external) ? (external, true)
        : null;

    private static OccurrenceRecord[] Occurrences(
        IEnumerable<ProjectFacts> facts,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<string, int> externalTypeIndex,
        IReadOnlyDictionary<string, int> executableIndex,
        IReadOnlyDictionary<string, int> documentIndex,
        IReadOnlyDictionary<(int, int, bool, DependencyKind), int> dependencyIndex,
        TargetMembers targets) =>
    [
        .. facts
            .SelectMany(part => part.Occurrences)
            .Select(occurrence =>
                typeIndex.TryGetValue(occurrence.Source, out var source)
                && Target(occurrence.Target, typeIndex, externalTypeIndex) is { } target
                && dependencyIndex.TryGetValue((source, target.Index, target.External, occurrence.Kind), out var dependency)
                    ? new OccurrenceRecord(
                        dependency,
                        occurrence.SourceMember is { } member && executableIndex.TryGetValue(member, out var sourceMember) ? sourceMember : null,
                        targets.Of(occurrence),
                        documentIndex[occurrence.Document],
                        occurrence.Line)
                    : null)
            .OfType<OccurrenceRecord>()
            .OrderBy(occurrence => occurrence.Dependency)
            .ThenBy(occurrence => occurrence.Document)
            .ThenBy(occurrence => occurrence.Line)
            .ThenBy(occurrence => occurrence.SourceMember)
            .ThenBy(occurrence => occurrence.TargetMember),
    ];

    /// <summary>The first entry per key, sorted by key.</summary>
    private static T[] FirstByKey<T>(IEnumerable<T> entries, Func<T, string> key) =>
    [
        .. entries
            .GroupBy(key, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(key, StringComparer.Ordinal),
    ];

    private static Dictionary<TKey, int> Index<T, TKey>(IReadOnlyList<T> rows, Func<T, TKey> key)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, int>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            index.Add(key(rows[i]), i);
        }

        return index;
    }

    private sealed record AssembledDependency(string SourceKey, string TargetKey, DependencyRecord Record)
    {
        public int Source => Record.Source;

        public int Target => Record.Target;

        public bool External => Record.External;

        public DependencyKind Kind => Record.Kind;
    }

    /// <summary>A project of the solution file, loaded or not.</summary>
    /// <param name="Loaded"><c>null</c> for a project MSBuildWorkspace did not open.</param>
    /// <param name="Diagnostics"><c>null</c> for a project MSBuildWorkspace did not open.</param>
    private sealed record AssembledProject(
        string Key,
        string Assembly,
        LoadedProject? Loaded,
        ProjectStyle? Style,
        ProjectDiagnostics? Diagnostics,
        ProjectFacts Facts,
        IReadOnlyList<WorkspaceDiagnosticRecord> WorkspaceDiagnostics)
    {
        // Roslyn names the project of a multi-targeting file "Name(tfm)"; MSBuild names it after the file.
        public string Name => Path.GetFileNameWithoutExtension(Key);

        public ProjectRecord Record(IReadOnlyDictionary<string, int> projectIndex) =>
            new(
                Key,
                Name,
                Assembly,
                Style,
                Loaded?.Tfm,
                Loaded?.Test ?? false,
                Loaded?.TestReason,
                [.. (Loaded?.References ?? []).Select(reference => projectIndex[reference]).Order()]);

        public ProjectHealthRecord Health(int project, IReadOnlyDictionary<string, int> documentIndex) =>
            new(
                project,
                Loaded is null ? LoadStatus.Failed : LoadStatus.Ok,
                Loaded?.AvailableTfms ?? [],
                Loaded?.MetadataReferences ?? 0,
                Diagnostics?.ObjectResolves ?? false,
                Diagnostics?.CompileErrors ?? new CompileErrorsRecord(0, []),
                UnresolvedTypes(),
                WorkspaceDiagnostics,
                Loaded?.SdkFallback ?? false,
                Loaded?.ProjectReferenceMismatch ?? false,
                [
                    .. Facts.AnalyzerErrors
                        .Select(error => new AnalyzerErrorRecord(documentIndex[error.Document], error.ExceptionType, error.Frame))
                        .OrderBy(error => error.Document)
                        .ThenBy(error => error.ExceptionType, StringComparer.Ordinal)
                        .ThenBy(error => error.Frame, StringComparer.Ordinal),
                ]);

        private UnresolvedTypesRecord UnresolvedTypes()
        {
            var names = Facts.UnresolvedTypes
                .GroupBy(type => type.Name, StringComparer.Ordinal)
                .Select(name => new UnresolvedTypeRecord(name.Key, name.Sum(type => type.Count)))
                .ToArray();
            return new UnresolvedTypesRecord(
                names.Sum(name => name.Count),
                [
                    .. names
                        .OrderByDescending(name => name.Count)
                        .ThenBy(name => name.Name, StringComparer.Ordinal)
                        .Take(UnresolvedTypesRecord.TopCount),
                ]);
        }
    }
}

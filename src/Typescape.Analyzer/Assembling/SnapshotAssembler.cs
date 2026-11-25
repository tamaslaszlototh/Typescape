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
/// Facts are resolved by key and indexed last, after sorting every table ordinally by stable key. Where projects state one
/// key, the first project by key wins (spec D17); what that leaves out of a later project is one of its analyzer errors.
/// </remarks>
internal static class SnapshotAssembler
{
    /// <summary>The <see cref="SnapshotRecord.RulesVersion"/> this Analyzer writes. Bump it whenever the same code would give a different Snapshot.</summary>
    public const int RulesVersion = 1;

    /// <summary>The <see cref="AnalyzerErrorRecord.ExceptionType"/> of an own type left out because an earlier project declares its key.</summary>
    public const string DuplicateTypeKey = "duplicate-type-key";

    /// <summary>The <see cref="AnalyzerErrorRecord.ExceptionType"/> of an Executable Member left out because no declaration of its type holds it.</summary>
    public const string UnplacedMember = "unplaced-member";

    public static SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var projects = Projects(input);
        var projectIndex = RowIndexes.Of(projects, project => project.Key);
        var leftOut = projects.Select(_ => new List<AnalyzerErrorRecord>()).ToArray();

        var documents = FirstByKey(Rows(projects, facts => facts.Documents), row => row.Fact.Path)
            .Select(row => new DocumentRecord(row.Fact.Path, row.Project, row.Fact.GeneratedReason is not null, row.Fact.GeneratedReason))
            .ToArray();
        var documentIndex = RowIndexes.Of(documents, document => document.Path);

        var ownTypes = FirstByKey(
            Rows(projects, facts => facts.Types),
            row => row.Fact.Key,
            duplicate => leftOut[duplicate.Project].Add(
                new AnalyzerErrorRecord(documentIndex[duplicate.Fact.Declarations[0].Document], DuplicateTypeKey, duplicate.Fact.Key)));
        var owners = ownTypes.ToDictionary(row => row.Fact.Key, row => row.Project, StringComparer.Ordinal);
        bool Declares(int project, string type) => owners.TryGetValue(type, out var owner) && owner == project;

        var namespaces = ownTypes
            .Select(row => new NamespaceRecord(row.Project, row.Fact.Namespace))
            .Distinct()
            .OrderBy(row => row.Project)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToArray();
        var namespaceIndex = RowIndexes.Of(namespaces, row => (row.Project, row.Name));
        var typeIndex = RowIndexes.Of(ownTypes, row => row.Fact.Key);
        var types = ownTypes.Select(row => Type(row, typeIndex, namespaceIndex, documentIndex)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var executableMembers = new List<ExecutableMemberRecord>();
        foreach (var row in FirstByKey(Rows(projects, facts => facts.ExecutableMembers).Where(row => Declares(row.Project, row.Fact.Type)), row => row.Fact.Key))
        {
            if (ExecutableMember(row.Fact, typeIndex[row.Fact.Type], types, documentIndex) is { } member)
            {
                executableMembers.Add(member);
            }
            else
            {
                leftOut[row.Project].Add(new AnalyzerErrorRecord(documentIndex[row.Fact.Document], UnplacedMember, row.Fact.Key));
            }
        }

        var externalCandidates = FirstByKey(projects.SelectMany(project => project.Facts.ExternalTypes), type => type.Key)
            .Where(type => !owners.ContainsKey(type.Key))
            .ToDictionary(type => type.Key, StringComparer.Ordinal);
        var dependencyFacts = Rows(projects, facts => facts.Dependencies)
            .Where(row => Declares(row.Project, row.Fact.Source) && (owners.ContainsKey(row.Fact.Target) || externalCandidates.ContainsKey(row.Fact.Target)))
            .GroupBy(row => (row.Fact.Source, row.Fact.Target, row.Fact.Kind))
            .Select(group => new DependencyFact(group.Key.Source, group.Key.Target, group.Key.Kind, group.Sum(row => row.Fact.Count), group.Sum(row => row.Fact.GeneratedCount)))
            .OrderBy(dependency => dependency.Source, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Target, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Kind)
            .ToArray();
        var dependencyIndex = RowIndexes.Of(dependencyFacts, dependency => (dependency.Source, dependency.Target, dependency.Kind));

        var targeted = dependencyFacts.Select(dependency => dependency.Target).ToHashSet(StringComparer.Ordinal);
        var externalTypes = externalCandidates.Values
            .Where(type => targeted.Contains(type.Key))
            .OrderBy(type => type.Key, StringComparer.Ordinal)
            .Select(type => new ExternalTypeRecord(type.Key, type.Name, type.Assembly, type.Namespace, type.Kind))
            .ToArray();
        var indexes = new RowIndexes(documentIndex, typeIndex, RowIndexes.Of(externalTypes, type => type.Key), RowIndexes.Of(executableMembers, member => member.Key));
        cancellationToken.ThrowIfCancellationRequested();

        OccurrenceFact[] occurrenceFacts = input.Request.Occurrences
            ? [.. Rows(projects, facts => facts.Occurrences)
                .Where(row => Declares(row.Project, row.Fact.Source) && dependencyIndex.ContainsKey((row.Fact.Source, row.Fact.Target, row.Fact.Kind)))
                .Select(row => row.Fact)]
            : [];
        var targets = new TargetMembers(occurrenceFacts, projects.Select(project => project.Facts), indexes);
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
            [.. dependencyFacts.Select(dependency => Dependency(dependency, indexes))],
            new LoadHealthRecord(
                [.. projects.Select((project, p) => project.Health(p, documentIndex, leftOut[p]))],
                input.Solution.SolutionDiagnostics),
            input.Provenance,
            input.Request.Occurrences ? Occurrences(occurrenceFacts, dependencyIndex, indexes, targets) : null);
    }

    private static AssembledProject[] Projects(AssembleInput input) =>
    [
        .. input.Projects
            .Select(result =>
            {
                var loaded = result.Project.Project;
                return new AssembledProject(loaded.Key, loaded, loaded.Style, result.Diagnostics, ProjectFacts.Combine(result.Facts), loaded.WorkspaceDiagnostics);
            })
            .Concat(input.Solution.UnopenedProjects.Select(project =>
                new AssembledProject(project.Key, Loaded: null, project.Style, Diagnostics: null, ProjectFacts.Empty, project.WorkspaceDiagnostics)))
            .OrderBy(project => project.Key, StringComparer.Ordinal),
    ];

    private static IEnumerable<Row<T>> Rows<T>(IReadOnlyList<AssembledProject> projects, Func<ProjectFacts, IReadOnlyList<T>> facts) =>
        projects.SelectMany((project, p) => facts(project.Facts).Select(fact => new Row<T>(p, fact)));

    private static TypeRecord Type(
        Row<TypeFact> row,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<(int, string), int> namespaceIndex,
        IReadOnlyDictionary<string, int> documentIndex)
    {
        var type = row.Fact;
        return new TypeRecord(
            type.Key,
            type.Name,
            type.Kind,
            type.Accessibility,
            type.Abstract,
            type.Sealed,
            type.Static,
            type.Arity,
            namespaceIndex[(row.Project, type.Namespace)],
            type.ContainingType is { } containing && typeIndex.TryGetValue(containing, out var index) ? index : null,
            [
                .. type.Declarations
                    .Select(declaration => new TypeDeclaration(documentIndex[declaration.Document], declaration.Lines, declaration.Loc, declaration.Generated))
                    .OrderBy(declaration => declaration.Document)
                    .ThenBy(declaration => declaration.Lines.Start),
            ],
            type.Loc);
    }

    /// <returns><c>null</c> when no declaration of the type holds the member.</returns>
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

    private static DependencyRecord Dependency(DependencyFact dependency, RowIndexes indexes)
    {
        var target = indexes.Type(dependency.Target)!.Value;
        return new DependencyRecord(indexes.Types[dependency.Source], target.Index, target.External, dependency.Kind, dependency.Count, dependency.GeneratedCount);
    }

    private static OccurrenceRecord[] Occurrences(
        IEnumerable<OccurrenceFact> occurrences,
        IReadOnlyDictionary<(string, string, DependencyKind), int> dependencyIndex,
        RowIndexes indexes,
        TargetMembers targets) =>
    [
        .. occurrences
            .Select(occurrence => new OccurrenceRecord(
                dependencyIndex[(occurrence.Source, occurrence.Target, occurrence.Kind)],
                occurrence.SourceMember is { } member && indexes.ExecutableMembers.TryGetValue(member, out var sourceMember) ? sourceMember : null,
                targets.Of(occurrence),
                indexes.Documents[occurrence.Document],
                occurrence.Line))
            .OrderBy(occurrence => occurrence.Dependency)
            .ThenBy(occurrence => occurrence.Document)
            .ThenBy(occurrence => occurrence.Line)
            .ThenBy(occurrence => occurrence.SourceMember)
            .ThenBy(occurrence => occurrence.TargetMember),
    ];

    /// <summary>The first entry per key, sorted by key.</summary>
    /// <param name="duplicate">Receives every later entry of a key.</param>
    private static T[] FirstByKey<T>(IEnumerable<T> entries, Func<T, string> key, Action<T>? duplicate = null)
    {
        var first = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!first.TryAdd(key(entry), entry))
            {
                duplicate?.Invoke(entry);
            }
        }

        return [.. first.Values.OrderBy(key, StringComparer.Ordinal)];
    }

    /// <summary>A fact of the project at index <paramref name="Project"/> of the sorted projects.</summary>
    private readonly record struct Row<T>(int Project, T Fact);

    /// <summary>A project of the solution file, loaded or not.</summary>
    /// <param name="Loaded"><c>null</c> for a project MSBuildWorkspace did not open.</param>
    /// <param name="Diagnostics"><c>null</c> for a project MSBuildWorkspace did not open.</param>
    private sealed record AssembledProject(
        string Key,
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
                Loaded?.Project.AssemblyName,
                Style,
                Loaded?.Tfm,
                Loaded?.Test ?? false,
                Loaded?.TestReason,
                [.. (Loaded?.References ?? []).Select(reference => projectIndex[reference]).Order()]);

        /// <param name="leftOut">The analyzer errors of what Assemble left out of the project.</param>
        public ProjectHealthRecord Health(int project, IReadOnlyDictionary<string, int> documentIndex, IEnumerable<AnalyzerErrorRecord> leftOut)
        {
            var compileErrors = Diagnostics?.CompileErrors ?? new CompileErrorsRecord(0, []);
            var objectResolves = Diagnostics?.ObjectResolves ?? false;
            var metadataReferences = Loaded?.MetadataReferences ?? 0;
            AnalyzerErrorRecord[] analyzerErrors =
            [
                .. Facts.AnalyzerErrors
                    .Select(error => new AnalyzerErrorRecord(documentIndex[error.Document], error.ExceptionType, error.Frame))
                    .Concat(leftOut)
                    .OrderBy(error => error.Document)
                    .ThenBy(error => error.ExceptionType, StringComparer.Ordinal)
                    .ThenBy(error => error.Frame, StringComparer.Ordinal),
            ];

            return new ProjectHealthRecord(
                project,
                Verdict(compileErrors, objectResolves, metadataReferences, analyzerErrors),
                Loaded?.AvailableTfms ?? [],
                metadataReferences,
                objectResolves,
                compileErrors,
                UnresolvedTypes(),
                WorkspaceDiagnostics,
                Loaded?.SdkFallback ?? false,
                Loaded?.ProjectReferenceMismatch ?? false,
                analyzerErrors);
        }

        // A suspiciously low reference count (spec D7) is an unresolved System.Object or no metadata reference at all.
        private LoadStatus Verdict(CompileErrorsRecord compileErrors, bool objectResolves, int metadataReferences, IReadOnlyList<AnalyzerErrorRecord> analyzerErrors) =>
            Loaded is null ? LoadStatus.Failed
            : compileErrors.Count > 0 || !objectResolves || metadataReferences == 0 || analyzerErrors.Count > 0 ? LoadStatus.Degraded
            : LoadStatus.Ok;

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

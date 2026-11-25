using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Assembling;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Extraction;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;
using WorkspaceDiagnosticKind = Typescape.Snapshot.Records.WorkspaceDiagnosticKind;
using static Typescape.Analyzer.Tests.Assembling.AssembleTest;

namespace Typescape.Analyzer.Tests.Assembling;

public class SnapshotAssemblerTests
{
    private const string CoreKey = "Core/Core.csproj";
    private const string AppKey = "App/App.csproj";

    [Fact]
    public void Two_projects_are_merged_into_tables_sorted_by_key_that_reference_each_other_by_index()
    {
        Match(Assemble(Solution()));
    }

    [Fact]
    public void The_same_facts_in_any_order_give_the_same_Snapshot()
    {
        var projects = Solution();
        var reversed = Enumerable.Reverse(projects)
            .Select(project => project with { Parts = [.. project.Parts.Reverse().Select(Reverse)] })
            .ToArray();

        Assert.Equal(Render(Assemble(projects)), Render(Assemble(reversed)));
    }

    [Fact]
    public void A_Dependency_sums_its_counts_across_documents_per_source_target_and_kind()
    {
        var snapshot = Assemble(new TestProject(
            AppKey,
            ProjectFacts.Empty with { Types = [Type("App|T:A", "App/A.cs")] },
            Document("App/A.cs", new DependencyFact("App|T:A", "System.Runtime|T:System.String", DependencyKind.TypeUsage, 2, 1)) with
            {
                ExternalTypes = [External("System.Runtime|T:System.String")],
            },
            Document("App/B.cs",
                new DependencyFact("App|T:A", "System.Runtime|T:System.String", DependencyKind.TypeUsage, 3, 0),
                new DependencyFact("App|T:A", "System.Runtime|T:System.String", DependencyKind.Signature, 1, 1)) with
            {
                ExternalTypes = [External("System.Runtime|T:System.String")],
            }));

        Assert.Equal(
            [new DependencyRecord(0, 0, true, DependencyKind.Signature, 1, 1), new DependencyRecord(0, 0, true, DependencyKind.TypeUsage, 5, 1)],
            snapshot.Dependencies);
        Assert.Single(snapshot.ExternalTypes);
    }

    [Fact]
    public void An_own_type_wins_over_an_External_Type_of_the_same_key_and_its_External_members_become_own_members()
    {
        // App references Core as a DLL: App's walk sees Core's types as External.
        var core = new TestProject(
            CoreKey,
            ProjectFacts.Empty with { Types = [Type("Core|T:Core.Shape", "Core/Shape.cs", 1, 5)] },
            ProjectFacts.Empty with
            {
                Documents = [new DocumentFact("Core/Shape.cs", null)],
                ExecutableMembers =
                [
                    new ExecutableMemberFact("Core|T:Core.Shape|M:Core.Shape.Area", "Core|T:Core.Shape", "Area()", ExecutableMemberKind.Method,
                        DeclaredAccessibility.Public, "Core/Shape.cs", new LineSpan(2, 2), 1, 1, 0, false),
                ],
            });
        var app = new TestProject(
            AppKey,
            ProjectFacts.Empty with { Types = [Type("App|T:App.Report", "App/Report.cs")] },
            ProjectFacts.Empty with
            {
                Documents = [new DocumentFact("App/Report.cs", null)],
                Dependencies = [new("App|T:App.Report", "Core|T:Core.Shape", DependencyKind.Call, 1, 0), new("App|T:App.Report", "Core|T:Core.Shape", DependencyKind.MemberAccess, 1, 0)],
                Occurrences =
                [
                    new("App|T:App.Report", "Core|T:Core.Shape", DependencyKind.Call, null, "M:Core.Shape.Area", "App/Report.cs", 1),
                    new("App|T:App.Report", "Core|T:Core.Shape", DependencyKind.MemberAccess, null, "P:Core.Shape.Name", "App/Report.cs", 1),
                ],
                ExternalTypes = [External("Core|T:Core.Shape")],
                ExternalMembers = [new("M:Core.Shape.Area", "Core|T:Core.Shape", "Shape.Area()"), new("P:Core.Shape.Name", "Core|T:Core.Shape", "Shape.Name")],
            });

        var snapshot = Assemble(core, app);

        Assert.Empty(snapshot.ExternalTypes);
        Assert.Empty(snapshot.ExternalMembers);
        Assert.All(snapshot.Dependencies, dependency => Assert.False(dependency.External));
        Assert.Equal([new NonExecutableMemberRecord("Core|T:Core.Shape|P:Core.Shape.Name", 1, "Shape.Name")], snapshot.NonExecutableMembers);
        Assert.Equal(
            [new MemberReference("M:Core.Shape.Area", "Area()"), new MemberReference("P:Core.Shape.Name", "Shape.Name")],
            snapshot.Occurrences!.Select(occurrence => snapshot.ResolveTargetMember(occurrence)!.Value));
    }

    [Fact]
    public void External_members_are_unique_by_their_type_and_key_and_an_Occurrence_resolves_through_both()
    {
        // Two polyfills of one attribute, each with the same constructor doc ID.
        const string first = "Polyfill.A|T:System.Runtime.CompilerServices.IsExternalInit";
        const string second = "Polyfill.B|T:System.Runtime.CompilerServices.IsExternalInit";
        const string ctor = "M:System.Runtime.CompilerServices.IsExternalInit.#ctor";
        var snapshot = Assemble(new TestProject(
            AppKey,
            ProjectFacts.Empty with { Types = [Type("App|T:A", "App/A.cs", 1, 3)] },
            ProjectFacts.Empty with
            {
                Documents = [new DocumentFact("App/A.cs", null)],
                Dependencies = [new("App|T:A", second, DependencyKind.New, 1, 0), new("App|T:A", first, DependencyKind.New, 1, 0)],
                Occurrences = [new("App|T:A", second, DependencyKind.New, null, ctor, "App/A.cs", 2), new("App|T:A", first, DependencyKind.New, null, ctor, "App/A.cs", 3)],
                ExternalTypes = [External(second), External(first)],
                ExternalMembers = [new(ctor, second, "B()"), new(ctor, first, "A()"), new(ctor, first, "A()")],
            }));

        Assert.Equal([new ExternalMemberRecord(ctor, 0, "A()"), new ExternalMemberRecord(ctor, 1, "B()")], snapshot.ExternalMembers);
        Assert.Equal(
            ["A()", "B()"],
            snapshot.Occurrences!.Select(occurrence => snapshot.ResolveTargetMember(occurrence)!.Value.Name));
    }

    [Fact]
    public void Without_Occurrences_the_Dependencies_remain_and_no_target_member_is_listed()
    {
        var snapshot = Assemble(Solution(occurrences: false), unopened: [], occurrences: false);

        Assert.Null(snapshot.Occurrences);
        Assert.Empty(snapshot.ExternalMembers);
        Assert.Empty(snapshot.NonExecutableMembers);
        Assert.NotEmpty(snapshot.Dependencies);
    }

    [Fact]
    public void A_project_that_did_not_open_is_listed_failed_with_no_types_among_the_others_by_key()
    {
        var warning = new WorkspaceDiagnosticRecord(WorkspaceDiagnosticKind.Failure, "Missing/Missing.csproj not found.");
        var snapshot = Assemble(
            [new TestProject(AppKey) { References = [CoreKey] }, new TestProject(CoreKey)],
            unopened: [new UnopenedProject("B/Missing.csproj", Style: null, [warning])]);

        Assert.Equal(
            [
                (AppKey, "App", "App", ProjectStyle.Sdk, "net10.0", "2"),
                ("B/Missing.csproj", "Missing", null, null, null, ""),
                (CoreKey, "Core", "Core", ProjectStyle.Sdk, "net10.0", ""),
            ],
            snapshot.Projects.Select(project =>
                (project.Key, project.Name, project.Assembly, project.Style, project.Tfm, string.Join(',', project.References))));
        var missing = snapshot.LoadHealth.Projects[1];
        Assert.Equal((1, LoadStatus.Failed, 0, false), (missing.Project, missing.Status, missing.MetadataReferences, missing.ObjectResolves));
        Assert.Equal([warning], missing.WorkspaceDiagnostics);
    }

    [Fact]
    public void A_document_linked_into_two_projects_is_one_row_owned_by_the_first_project()
    {
        var snapshot = Assemble(
            new TestProject(CoreKey, AssembleTest.Document("Shared/Link.cs")),
            new TestProject(AppKey, AssembleTest.Document("Shared/Link.cs"), AssembleTest.Document("App/A.cs")));

        Assert.Equal([new DocumentRecord("App/A.cs", 0, false, null), new DocumentRecord("Shared/Link.cs", 0, false, null)], snapshot.Documents);
    }

    [Fact]
    public void Unresolved_type_references_are_summed_per_project_and_the_ten_most_frequent_names_kept()
    {
        var parts = Enumerable.Range(0, 12)
            .Select(i => ProjectFacts.Empty with { UnresolvedTypes = [new($"N{i:00}", i % 3 + 1), new("Json", 2)] })
            .ToArray();

        var unresolved = Assemble(new TestProject(AppKey, parts)).LoadHealth.Projects[0].UnresolvedTypes;

        Assert.Equal(48, unresolved.Count);
        Assert.Equal(
            ["Json:24", "N02:3", "N05:3", "N08:3", "N11:3", "N01:2", "N04:2", "N07:2", "N10:2", "N00:1"],
            unresolved.Top.Select(name => $"{name.Name}:{name.Count}"));
    }

    [Fact]
    public void A_Dependency_or_Occurrence_of_a_type_whose_walk_failed_is_left_out()
    {
        var snapshot = Assemble(new TestProject(
            AppKey,
            ProjectFacts.Empty with
            {
                Types = [Type("App|T:A", "App/A.cs")],
                AnalyzerErrors = [new AnalyzerErrorFact("App/A.cs", "System.InvalidCastException", "OwnTypes.Walk()")],
            },
            ProjectFacts.Empty with
            {
                Documents = [new DocumentFact("App/A.cs", null)],
                Dependencies = [new("App|T:Failed", "App|T:A", DependencyKind.TypeUsage, 1, 0), new("App|T:A", "App|T:Failed", DependencyKind.TypeUsage, 1, 0)],
                Occurrences = [new("App|T:Failed", "App|T:A", DependencyKind.TypeUsage, null, null, "App/A.cs", 1)],
            }));

        Assert.Empty(snapshot.Dependencies);
        Assert.Empty(snapshot.Occurrences!);
        Assert.Equal([new AnalyzerErrorRecord(0, "System.InvalidCastException", "OwnTypes.Walk()")], snapshot.LoadHealth.Projects[0].AnalyzerErrors);
    }

    [Fact]
    public void A_type_whose_key_an_earlier_project_declares_is_left_out_with_its_facts_as_an_analyzer_error()
    {
        var first = new TestProject(
            CoreKey,
            ProjectFacts.Empty with { Types = [Type("Lib|T:Lib.A", "Core/A.cs")] },
            Document("Core/A.cs", new DependencyFact("Lib|T:Lib.A", "Lib|T:Lib.B", DependencyKind.TypeUsage, 1, 0)) with
            {
                Types = [Type("Lib|T:Lib.B", "Core/A.cs", 2, 2)],
            });
        var second = new TestProject(
            "Other/Other.csproj",
            ProjectFacts.Empty with
            {
                Types = [Type("Lib|T:Lib.A", "Other/A.cs")],
                ExecutableMembers =
                [
                    new ExecutableMemberFact("Lib|T:Lib.A|M:Lib.A.Run", "Lib|T:Lib.A", "Run()", ExecutableMemberKind.Method,
                        DeclaredAccessibility.Public, "Other/A.cs", new LineSpan(1, 1), 1, 1, 0, false),
                ],
            },
            Document("Other/A.cs", new DependencyFact("Lib|T:Lib.A", "Lib|T:Lib.B", DependencyKind.TypeUsage, 5, 0)));

        var snapshot = Assemble(first, second);

        Assert.Equal(["Core/A.cs", "Core/A.cs"], snapshot.Types.Select(type => snapshot.Documents[type.Declarations[0].Document].Path));
        Assert.Empty(snapshot.ExecutableMembers);
        Assert.Equal([1], snapshot.Dependencies.Select(dependency => dependency.Count));
        var other = snapshot.LoadHealth.Projects[1];
        Assert.Equal(LoadStatus.Degraded, other.Status);
        Assert.Equal([new AnalyzerErrorRecord(1, SnapshotAssembler.DuplicateTypeKey, "Lib|T:Lib.A")], other.AnalyzerErrors);
        Assert.Equal(LoadStatus.Ok, snapshot.LoadHealth.Projects[0].Status);
    }

    [Fact]
    public void An_Executable_Member_no_declaration_of_its_type_holds_is_left_out_as_an_analyzer_error()
    {
        var snapshot = Assemble(new TestProject(
            AppKey,
            ProjectFacts.Empty with
            {
                Types = [Type("App|T:A", "App/A.cs", 1, 3)],
                ExecutableMembers =
                [
                    new ExecutableMemberFact("App|T:A|M:A.Run", "App|T:A", "Run()", ExecutableMemberKind.Method,
                        DeclaredAccessibility.Public, "App/A.cs", new LineSpan(5, 6), 2, 1, 0, false),
                ],
            },
            Document("App/A.cs")));

        Assert.Empty(snapshot.ExecutableMembers);
        Assert.Equal([new AnalyzerErrorRecord(0, SnapshotAssembler.UnplacedMember, "App|T:A|M:A.Run")], snapshot.LoadHealth.Projects[0].AnalyzerErrors);
    }

    [Fact]
    public void An_External_Type_or_member_only_a_left_out_Dependency_targets_is_not_listed()
    {
        const string text = "System.Runtime|T:System.String";
        var snapshot = Assemble(new TestProject(
            AppKey,
            ProjectFacts.Empty with { Types = [Type("App|T:A", "App/A.cs")] },
            Document("App/A.cs", new DependencyFact("App|T:Failed", text, DependencyKind.Call, 1, 0)) with
            {
                Occurrences = [new("App|T:Failed", text, DependencyKind.Call, null, "M:System.String.Trim", "App/A.cs", 1)],
                ExternalTypes = [External(text)],
                ExternalMembers = [new("M:System.String.Trim", text, "String.Trim()")],
                NonExecutableMembers = [new("App|T:A|F:A.Unused", "App|T:A", "A.Unused")],
            }));

        Assert.Empty(snapshot.ExternalTypes);
        Assert.Empty(snapshot.ExternalMembers);
        Assert.Empty(snapshot.NonExecutableMembers);
    }

    [Fact]
    public void A_project_is_degraded_by_compile_errors_an_unresolved_object_no_metadata_reference_or_an_analyzer_error()
    {
        var clean = new ProjectDiagnostics(new CompileErrorsRecord(0, []), ObjectResolves: true);
        var snapshot = Assemble(
            new TestProject("A/A.csproj"),
            new TestProject("B/B.csproj") { Diagnostics = clean with { CompileErrors = new CompileErrorsRecord(1, [new CompileErrorCodeRecord("CS0246", 1, "Missing")]) } },
            new TestProject("C/C.csproj") { Diagnostics = clean with { ObjectResolves = false } },
            new TestProject("D/D.csproj") { HasMetadataReferences = false },
            new TestProject("E/E.csproj", ProjectFacts.Empty with
            {
                Documents = [new DocumentFact("E/E.cs", null)],
                AnalyzerErrors = [new AnalyzerErrorFact("E/E.cs", "System.InvalidCastException", "Dependencies.Of()")],
            }));

        Assert.Equal(
            [LoadStatus.Ok, LoadStatus.Degraded, LoadStatus.Degraded, LoadStatus.Degraded, LoadStatus.Degraded],
            snapshot.LoadHealth.Projects.Select(project => project.Status));
    }

    private static TestProject[] Solution(bool occurrences = true)
    {
        var core = RuleTest.Compile(
            [
                ("Core/Shape.cs", """
                    namespace Core;

                    public abstract class Shape
                    {
                        public string Name = "";
                        public abstract double Area();
                    }

                    public enum Color { Red, Green }
                    """),
                ("Core/Circle.cs", """
                    namespace Core;

                    public sealed class Circle(double r) : Shape
                    {
                        public override double Area() => System.Math.PI * r * r;
                        public Color Color => Color.Red;
                    }
                    """),
            ],
            assembly: "Core");
        var app = RuleTest.Compile(
            [
                ("App/Report.cs", """
                    using Core;

                    namespace App;

                    public static class Report
                    {
                        public static void Print(Shape shape)
                        {
                            System.Console.WriteLine(shape.Name + shape.Area());
                            if (new Circle(1).Color == Color.Red) System.Console.WriteLine("red");
                        }
                    }
                    """),
                ("App/Zoo/Keeper.cs", """
                    namespace App.Zoo;

                    class Keeper
                    {
                        Core.Circle pet = new(2);
                        class Cage { Keeper owner = null!; }
                    }
                    """),
            ],
            assembly: "App",
            references: [core.ToMetadataReference()]);

        return
        [
            new TestProject(CoreKey, Walk(core, Context(CoreKey, occurrences))),
            new TestProject(AppKey, Walk(app, Context(AppKey, occurrences))) { References = [CoreKey] },
        ];
    }

    private static ProjectFacts Reverse(ProjectFacts facts) =>
        new(
            [.. facts.Documents.Reverse()],
            [.. facts.Types.Reverse().Select(type => type with { Declarations = [.. type.Declarations.Reverse()] })],
            [.. facts.ExecutableMembers.Reverse()],
            [.. facts.Dependencies.Reverse()],
            [.. facts.Occurrences.Reverse()],
            [.. facts.ExternalTypes.Reverse()],
            [.. facts.ExternalMembers.Reverse()],
            [.. facts.NonExecutableMembers.Reverse()],
            [.. facts.UnresolvedTypes.Reverse()],
            [.. facts.AnalyzerErrors.Reverse()]);
}

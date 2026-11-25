using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.Extraction;

// Spec §5.3.
public class LinesOfCodeTests
{
    [Fact]
    public void Every_line_with_a_token_counts_once_braces_included()
    {
        Assert.Equal(1, Loc("void M() { x++; x--; }"));
        Assert.Equal(5, Loc("""
            void M()
            {
                x++;
                x--;
            }
            """));
    }

    [Fact]
    public void Blank_and_whitespace_only_lines_do_not_count()
    {
        Assert.Equal(4, Loc("void M()\n{\n\n    x++;\n    \t\n}"));
    }

    [Fact]
    public void Comment_only_lines_do_not_count_but_code_with_a_trailing_comment_does()
    {
        Assert.Equal(4, Loc("""
            void M()
            {
                // A comment.
                /* A block
                   comment. */
                // x--;
                x++; // A trailing comment.
            }
            """));
    }

    [Fact]
    public void Doc_comments_do_not_count_and_attribute_lists_do()
    {
        Assert.Equal(3, Loc("""
            /// <summary>
            /// Documented.
            /// </summary>
            [Obsolete]
            [CLSCompliant(false)]
            void M() { }
            """));
    }

    [Fact]
    public void Every_signature_line_counts()
    {
        Assert.Equal(6, Loc("""
            int M(
                int first,
                int second)
            {
                return first + second;
            }
            """));
    }

    [Fact]
    public void Preprocessor_directives_and_inactive_text_do_not_count()
    {
        Assert.Equal(5, Loc("""
            void M()
            {
            #if DEBUG
                x++;
                x++;
            #else
                x--;
            #endif
            #region Region
            #pragma warning disable CS0168
                x--;
            #endregion
            }
            """));
    }

    [Fact]
    public void A_multi_line_token_counts_each_of_its_lines()
    {
        Assert.Equal(3, Loc("""
            string M() => @"one

            three";
            """));
        Assert.Equal(5, Loc(""""
            string M() => """
                one

                three
                """;
            """"));
        Assert.Equal(4, Loc(""""
            string M() => $"""
                {x}
                and {x + 1}
                """;
            """"));
    }

    [Fact]
    public void Line_directives_do_not_move_the_lines()
    {
        var member = Member("""
            void M()
            {
            #line 200 "Other.cs"
                x++;
            #line default
            #line hidden
                x--;
            }
            """);

        Assert.Equal(5, member.Loc);
        Assert.Equal(new LineSpan(8, 15), member.Lines);
    }

    [Fact]
    public void Lambdas_anonymous_methods_and_local_functions_count_toward_their_member()
    {
        Assert.Equal(11, Loc("""
            int M()
            {
                Func<int, int> twice = value =>
                    value * 2;
                Func<int> one = delegate
                {
                    return 1;
                };
                int Three() => 3;
                return twice(one()) + Three();
            }
            """));
    }

    [Fact]
    public void Each_accessor_counts_its_own_lines_and_an_expression_bodied_property_its_whole_declaration()
    {
        var locs = Locs(Extract("""
            int P
            {
                [Obsolete]
                get
                {
                    return x;
                }

                set => x = value;
            }

            [Obsolete]
            int Q =>
                x;

            event Action E { add { } remove { } }
            """));

        Assert.Equal(5, locs["P.get"]);
        Assert.Equal(1, locs["P.set"]);
        Assert.Equal(3, locs["Q.get"]);
        Assert.Equal(1, locs["E.add"]);
        Assert.Equal(1, locs["E.remove"]);
    }

    [Fact]
    public void A_constructor_counts_its_initializer()
    {
        var locs = Locs(Extract("""
            Subject() { }

            Subject(int y)
                : this()
            {
                x = y;
            }
            """));

        Assert.Equal(1, locs["Subject()"]);
        Assert.Equal(5, locs["Subject(int)"]);
    }

    [Fact]
    public void Members_sharing_a_line_each_count_it()
    {
        var locs = Locs(Extract("void A() { x++; } void B() { x--; }"));

        Assert.Equal(1, locs["A()"]);
        Assert.Equal(1, locs["B()"]);
    }

    [Fact]
    public void The_entry_point_counts_its_top_level_statements_only()
    {
        var facts = RuleTest.Extract(RuleTest.Compile(
            [(RuleTest.SnippetPath, """
            using System;

            // Greets.
            var name = args.Length > 0
                ? args[0]
                : "world";

            Console.WriteLine(name);

            class Other
            {
            }
            """)],
            OutputKind.ConsoleApplication));

        Assert.Equal(4, Assert.Single(facts.ExecutableMembers).Loc);
        Assert.Equal(4, TypeNamed(facts, "Program").Loc);
    }

    [Fact]
    public void A_type_counts_its_declaration_with_its_fields_not_the_sum_of_its_members()
    {
        var type = TypeNamed(RuleTest.Extract("""
            using System;

            /// <summary>A record of fields only.</summary>
            [Serializable]
            class Order
            {
                // The name.
                public string Name = "";

                public int Count;
            }
            """), "Order");

        Assert.Equal(6, type.Loc);
    }

    [Fact]
    public void A_type_counts_its_members_and_the_lines_between_them()
    {
        var facts = RuleTest.Extract("""
            class Counter
            {
                int count;

                void Add()
                {
                    count++;
                }

                int Count => count;
            }
            """);

        Assert.Equal(9, TypeNamed(facts, "Counter").Loc);
        Assert.Equal(5, facts.ExecutableMembers.Sum(member => member.Loc));
    }

    [Fact]
    public void Initializers_count_toward_their_type_only()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Settings
            {
                Func<int, int> twice = value =>
                    value * 2;

                int[] Sizes { get; } =
                [
                    1,
                    2,
                ];
            }
            """);

        Assert.Equal(10, TypeNamed(facts, "Settings").Loc);
        Assert.Empty(facts.ExecutableMembers);
    }

    [Fact]
    public void A_primary_constructors_base_arguments_count_toward_its_type()
    {
        var facts = RuleTest.Extract("""
            class Base(int x);

            class Derived(int x) : Base(
                x + 1);
            """);

        Assert.Equal(2, TypeNamed(facts, "Derived").Loc);
        Assert.Empty(facts.ExecutableMembers);
    }

    [Fact]
    public void A_nested_type_is_left_out_of_its_containing_type_attributes_and_header_included()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Outer
            {
                int x;

                [Serializable]
                class Inner
                {
                    int y;
                }

                enum Color
                {
                    Red,
                }

                delegate void Handler(
                    int value);

                int z;
            }
            """);

        Assert.Equal(5, TypeNamed(facts, "Outer").Loc);
        Assert.Equal(5, TypeNamed(facts, "Inner").Loc);
        Assert.Equal(4, TypeNamed(facts, "Color").Loc);
        Assert.Equal(2, TypeNamed(facts, "Handler").Loc);
    }

    [Fact]
    public void Partial_declarations_are_summed_each_with_its_own_loc()
    {
        var type = TypeNamed(RuleTest.Extract(
        [
            ("App/A.cs", """
                partial class Split
                {
                    int a;
                }
                """),
            ("App/B.cs", """
                partial class Split
                {

                    int b;

                    void M() { }
                }
                """),
        ]), "Split");

        Assert.Equal([4, 5], type.Declarations.Select(declaration => declaration.Loc));
        Assert.Equal(9, type.Loc);
    }

    [Fact]
    public void An_extension_block_counts_toward_its_static_class()
    {
        var facts = RuleTest.Extract("""
            static class Text
            {
                extension(string value)
                {
                    bool IsBlank =>
                        value.Trim().Length == 0;
                }
            }
            """);

        Assert.Equal(8, TypeNamed(facts, "Text").Loc);
        Assert.Equal(2, Assert.Single(facts.ExecutableMembers).Loc);
    }

    [Fact]
    public void A_one_line_record_enum_or_delegate_is_one()
    {
        var facts = RuleTest.Extract("""
            record Point(int X, int Y);
            enum Size { Small, Large }
            delegate void Handler(int value);
            """);

        Assert.All(facts.Types, type => Assert.Equal(1, type.Loc));
    }

    private const string Fields = """
        using System;
        using System.Linq;

        class Subject
        {
            int x;
            int[] xs = [];

        """;

    private static ProjectFacts Extract(string members) =>
        RuleTest.Extract(RuleTest.Compile([(RuleTest.SnippetPath, $"{Fields}{members}\n}}\n")]));

    private static ExecutableMemberFact Member(string members) => Assert.Single(Extract(members).ExecutableMembers);

    /// <summary>The LOC of the one member in <paramref name="members"/>.</summary>
    private static int Loc(string members) => Member(members).Loc;

    private static Dictionary<string, int> Locs(ProjectFacts facts) =>
        facts.ExecutableMembers.ToDictionary(member => member.Signature, member => member.Loc);

    private static TypeFact TypeNamed(ProjectFacts facts, string name) => Assert.Single(facts.Types, type => type.Name == name);
}

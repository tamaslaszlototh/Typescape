using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Tests.Extraction;

public class OwnTypesTests
{
    [Fact]
    public void A_partial_type_is_one_type_with_a_declaration_per_part()
    {
        var facts = RuleTest.Extract(
        [
            ("App/Order.cs", """
                namespace Shop;

                public partial class Order
                {
                    int id;
                }
                """),
            ("App/Order.Lines.cs", """
                namespace Shop
                {
                    partial class Order { }
                }
                """),
        ]);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void File_local_types_of_one_name_in_two_documents_are_two_types_keyed_by_their_document()
    {
        var facts = RuleTest.Extract(
        [
            ("App/A.cs", """
                file class Helper
                {
                    class Inner { }

                    void Run() { }
                }
                """),
            ("App/B.cs", """
                file class Helper
                {
                    class Inner { }

                    void Run() { }
                }

                class Shared { }
                """),
        ]);

        RuleTest.Match(RuleTest.Keep(facts, types: true, members: true));
    }

    [Fact]
    public void A_nested_type_is_its_own_type_that_names_its_containing_type()
    {
        var facts = RuleTest.Extract("""
            namespace Shop;

            public class Order
            {
                private class Line
                {
                    protected internal enum State { Open, Shipped }
                }

                private protected interface IAudit { }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void A_generic_type_is_its_open_definition_with_its_arity()
    {
        var facts = RuleTest.Extract("""
            namespace Shop;

            public class Repository<TKey, TValue>
            {
                public class Page<TItem> { }

                public class Cursor { }
            }

            public class Repository<T> { }

            public class Orders : Repository<int, string> { }
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void Each_kind_and_modifier_is_recorded_as_declared()
    {
        var facts = RuleTest.Extract("""
            abstract class Shape { }
            sealed class Circle : Shape { }
            static class Geometry { }
            interface IShape { }
            struct Point { }
            readonly record struct Size(int Width, int Height);
            abstract record Event;
            sealed record Placed : Event;
            enum Color { Red }
            delegate void Changed(Shape shape);
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void The_namespace_is_the_full_name_and_empty_for_the_global_namespace()
    {
        var facts = RuleTest.Extract("""
            class Global { }

            namespace Shop.Orders
            {
                class Order { }

                namespace @class
                {
                    class Line { }
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void A_declaration_spans_its_physical_lines_from_attributes_to_closing_brace()
    {
        var facts = RuleTest.Extract("""
            using System;

            /// <summary>Not part of the span.</summary>
            [Serializable]
            class Order
            {
            #line 100 "Other.cs"
                int id;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void Top_level_statements_are_a_declaration_of_the_synthesized_Program_type()
    {
        var facts = RuleTest.Extract(RuleTest.Compile(
            [
                (RuleTest.SnippetPath, """
                    using System;

                    Console.WriteLine(Greeting());

                    static string Greeting() => "hello";

                    class Order { }
                    """),
                ("App/Program.cs", "partial class Program { }"),
            ],
            OutputKind.ConsoleApplication));

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void An_extension_block_is_not_a_type()
    {
        var facts = RuleTest.Extract("""
            static class Text
            {
                extension(string value)
                {
                    public bool IsBlank => value.Trim().Length == 0;
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, types: true));
    }

    [Fact]
    public void Types_of_referenced_assemblies_are_not_own_types()
    {
        var facts = RuleTest.Extract("class Order { System.Collections.Generic.List<int> lines = []; }");

        Assert.Equal(["App|T:Order"], facts.Types.Select(type => type.Key));
    }}

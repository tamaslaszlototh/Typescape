using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;

namespace Typescape.Analyzer.Tests.Extraction;

public class ExecutableMembersTests
{
    [Fact]
    public void Methods_constructors_destructors_operators_and_conversions_are_executable_members()
    {
        var facts = RuleTest.Extract("""
            using System;

            namespace Shop;

            public sealed class Money : IDisposable
            {
                public Money(decimal amount) { }
                static Money() { }
                ~Money() { }

                public static Money operator +(Money left, Money right) => left;
                public static implicit operator decimal(Money money) => 0m;
                public static explicit operator Money(decimal amount) => new(amount);

                internal T Convert<T>(T value, ref int count, params int[] rates) => value;
                void IDisposable.Dispose() { }
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Each_accessor_is_its_own_member_and_an_expression_bodied_property_is_its_getter()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                int total;

                public int Total
                {
                    get { return total; }
                    private set => total = value;
                }

                public string Label =>
                    $"#{total}";

                public int Discount { get => 0; init { } }

                public int this[int line] => line;

                public event EventHandler Changed
                {
                    add { }
                    remove { }
                }
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Lambdas_anonymous_methods_and_local_functions_fold_into_their_member_and_initializers_are_none()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                Func<int, int> twice = x => x * 2;

                public int Lines { get; } = Count();

                public int Total()
                {
                    Func<int, int> add = x => x + 1;
                    Action log = delegate { };
                    return Sum(add(1));

                    static int Sum(int value) => value;
                }

                static int Count() => 0;
            }
            """);

        Assert.Equal(["App|T:Order|M:Order.Total", "App|T:Order|M:Order.Count"], facts.ExecutableMembers.Select(member => member.Key));
        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Members_without_an_executable_body_are_not_executable_members()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Runtime.InteropServices;

            abstract partial class Shape(int sides)
            {
                public abstract double Area();
                public int Sides { get; set; } = sides;
                public event EventHandler? Moved;
                partial void Drawn();

                [DllImport("native")]
                static extern int Measure();
            }

            interface IShape
            {
                double Area();
                string Name { get; }
            }

            record Point(int X, int Y);

            delegate void Changed(Shape shape);

            class Empty { }
            """);

        Assert.Empty(facts.ExecutableMembers);
    }

    [Fact]
    public void A_default_interface_method_is_an_executable_member()
    {
        var facts = RuleTest.Extract("""
            interface IShape
            {
                double Area();
                string Describe() => "shape";
            }
            """);

        Assert.Equal(["App|T:IShape|M:IShape.Describe"], facts.ExecutableMembers.Select(member => member.Key));
    }

    [Fact]
    public void A_partial_member_is_its_implementation_part()
    {
        var facts = RuleTest.Extract(
        [
            ("App/Order.cs", """
                partial class Order
                {
                    partial void Validate();
                    public partial int Total { get; }
                    partial void Notify();
                }
                """),
            ("App/Order.Impl.cs", """
                partial class Order
                {
                    partial void Validate() { }

                    public partial int Total { get => 0; }
                }
                """),
        ]);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void A_member_spans_its_attributes_to_its_end_and_belongs_to_its_own_type()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Repository<T>
            {
                /// <summary>Not part of the span.</summary>
                [Obsolete]
                public T? Find(int id)
                {
                    return default;
                }

                class Cursor
                {
                    public void Next() { }
                }
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Top_level_statements_are_the_entry_point_of_the_synthesized_Program_type()
    {
        var facts = RuleTest.Extract(RuleTest.Compile(
            [
                (RuleTest.SnippetPath, """
                    using System;

                    Console.WriteLine(Greeting());

                    static string Greeting() => "hello";

                    class Order { }
                    """),
            ],
            OutputKind.ConsoleApplication));

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Extension_block_members_belong_to_their_static_class()
    {
        var facts = RuleTest.Extract("""
            static class Text
            {
                extension(string value)
                {
                    public bool IsBlank => value.Trim().Length == 0;

                    public string Twice() => value + value;
                }

                extension(int number)
                {
                    public string Twice() => $"{number}{number}";
                }
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void An_explicit_interface_implementation_keeps_the_kind_of_the_member_it_implements()
    {
        var facts = RuleTest.Extract("""
            interface IAmount<T> where T : IAmount<T>
            {
                static abstract T operator +(T left, T right);
                static abstract explicit operator int(T value);
                static abstract T Create();
                static virtual T Zero() => T.Create();
                int Count { get; }
            }

            struct Money : IAmount<Money>
            {
                static Money IAmount<Money>.operator +(Money left, Money right) => left;
                static explicit IAmount<Money>.operator int(Money value) => 0;
                static Money IAmount<Money>.Create() => default;
                int IAmount<Money>.Count => 0;
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Members_of_a_generic_type_are_keyed_by_its_open_definition()
    {
        var facts = RuleTest.Extract("""
            class Repository<T>
            {
                public Repository(T seed) { }

                public T Seed { get => default!; }
            }
            """);

        RuleTest.Match(Members(facts));
    }

    [Fact]
    public void Each_member_lies_in_exactly_one_declaration_of_its_type()
    {
        var facts = RuleTest.Extract(
        [
            ("App/Order.cs", """
                partial class Order
                {
                    void Add() { }
                }

                partial class Order
                {
                    void Remove() { }

                    class Line { void Split() { } }
                }
                """),
            ("App/Order.Count.cs", "partial class Order { int Count => 0; }"),
        ]);

        var declarations = facts.Types.ToDictionary(type => type.Key, type => type.Declarations);
        Assert.Equal(4, facts.ExecutableMembers.Count);
        Assert.All(facts.ExecutableMembers, member => Assert.Single(
            declarations[member.Type],
            declaration => declaration.Document == member.Document
                && declaration.Lines.Start <= member.Lines.Start
                && member.Lines.End <= declaration.Lines.End));
    }

    private static ProjectFacts Members(ProjectFacts facts) =>
        ProjectFacts.Empty with { ExecutableMembers = facts.ExecutableMembers, AnalyzerErrors = facts.AnalyzerErrors };
}

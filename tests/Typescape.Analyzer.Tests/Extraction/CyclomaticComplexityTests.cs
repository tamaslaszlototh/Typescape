using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;

namespace Typescape.Analyzer.Tests.Extraction;

// Spec §5.2; Sonar's own test files are not used.
public class CyclomaticComplexityTests
{
    [Fact]
    public void A_member_without_decisions_is_one()
    {
        Assert.Equal(1, Score("void M() { }"));
        Assert.Equal(1, Score("int M() { x++; return x; }"));
    }

    [Fact]
    public void Each_if_and_else_if_counts_one_and_else_is_free()
    {
        Assert.Equal(2, Score("void M() { if (a) { } }"));
        Assert.Equal(2, Score("void M() { if (a) { } else { } }"));
        Assert.Equal(4, Score("void M() { if (a) { } else if (b) { } else if (c) { } else { } }"));
        Assert.Equal(3, Score("void M() { if (a) { if (b) { } } }"));
    }

    [Fact]
    public void Each_conditional_expression_counts_one()
    {
        Assert.Equal(2, Score("int M() => a ? 1 : 2;"));
        Assert.Equal(3, Score("int M() => a ? 1 : b ? 2 : 3;"));
    }

    [Fact]
    public void Each_case_label_counts_one_and_default_and_when_are_free()
    {
        Assert.Equal(4, Score("""
            string M()
            {
                switch (x)
                {
                    case 1:
                    case 2: return "few";
                    case > 2 when a: return "some";
                    default: return "many";
                }
            }
            """));
        Assert.Equal(1, Score("void M() { switch (x) { default: break; } }"));
    }

    [Fact]
    public void Each_switch_expression_arm_counts_one_but_a_trailing_discard()
    {
        Assert.Equal(3, Score("""string M() => x switch { 1 => "one", 2 => "two", _ => "many" };"""));
        Assert.Equal(4, Score("""string M() => x switch { 1 => "one", 2 => "two", var other => "many" };"""));
        Assert.Equal(3, Score("""string M() => x switch { 1 => "one", _ when a => "odd", _ => "many" };"""));
        Assert.Equal(2, Score("""string M() => x switch { 1 => "one", _ when a => "odd" };"""));
    }

    [Fact]
    public void Each_loop_counts_one()
    {
        Assert.Equal(7, Score("""
            async Task M(IAsyncEnumerable<int> items, (int, int)[] pairs)
            {
                for (var i = 0; i < x; i++) { }
                foreach (var item in xs) { }
                await foreach (var item in items) { }
                foreach (var (left, right) in pairs) { }
                while (a) { }
                do { } while (b);
            }
            """));
    }

    [Fact]
    public void Each_catch_counts_one_and_try_finally_and_filters_are_free()
    {
        Assert.Equal(3, Score("""
            void M()
            {
                try { }
                catch (InvalidOperationException) when (a) { }
                catch { }
                finally { }
            }
            """));
    }

    [Fact]
    public void Each_logical_operator_counts_one()
    {
        Assert.Equal(3, Score("bool M() => a && b && c;"));
        Assert.Equal(3, Score("bool M() => a || b || c;"));
        Assert.Equal(4, Score("bool M() => a && !(b || c) && d;"));
        Assert.Equal(1, Score("bool M() => a & b | c ^ d;"));
    }

    [Fact]
    public void Each_pattern_combinator_counts_one_and_other_patterns_are_free()
    {
        Assert.Equal(3, Score("bool M() => x is 1 or 2 or 3;"));
        Assert.Equal(4, Score("bool M() => x is (> 0 and < 10) or 20 and not 30;"));
        Assert.Equal(1, Score("""
            void M(object? value, int[] numbers)
            {
                var word = value is string found;
                var named = value is Order { Name.Length: > 0 };
                var list = numbers is [1, .., > 5];
                var negative = x is < 0;
            }
            """));
        Assert.Equal(1, Score("bool M() => s is not null;"));
    }

    [Fact]
    public void Each_null_shorthand_counts_one_and_throw_expressions_are_free()
    {
        Assert.Equal(2, Score("string M() => s ?? \"none\";"));
        Assert.Equal(2, Score("void M() { s ??= \"none\"; }"));
        Assert.Equal(2, Score("int? M() => s?.Length;"));
        Assert.Equal(2, Score("int? M(int[]? numbers) => numbers?[0];"));
        Assert.Equal(3, Score("int? M(Order? order) => order?.Name?.Length;"));
        Assert.Equal(2, Score("string M() => s ?? throw new InvalidOperationException();"));
    }

    [Fact]
    public void Goto_return_and_throw_are_free()
    {
        Assert.Equal(1, Score("""
            void M()
            {
                goto done;
                done:
                throw new InvalidOperationException();
            }
            """));
        Assert.Equal(1, Score("int M() { return x; }"));
    }

    [Fact]
    public void Lambdas_anonymous_methods_and_local_functions_add_their_decision_points_only()
    {
        Assert.Equal(1, Score("void M() { Func<int, int> twice = item => item * 2; }"));
        Assert.Equal(2, Score("void M() { Action log = () => { if (a) { } }; }"));
        Assert.Equal(2, Score("void M() { Action log = delegate { if (a) { } }; }"));
        Assert.Equal(3, Score("void M() { Check(); void Check() { if (a && b) { } } }"));
        Assert.Equal(3, Score("void M() { var any = xs.Where(item => item > 0 ? a : b).Any(); if (c) { } }"));
    }

    [Fact]
    public void Query_clauses_count_their_decision_points()
    {
        Assert.Equal(3, Score("""void M() { var found = from item in xs where item > 0 && a select s ?? "none"; }"""));
    }

    [Fact]
    public void Attribute_arguments_are_not_counted()
    {
        Assert.Equal(1, Score("""[Obsolete(true ? "old" : "new")] void M() { }"""));
    }

    [Fact]
    public void Recursion_is_free()
    {
        Assert.Equal(2, Score("int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);"));
    }

    [Fact]
    public void A_constructor_counts_its_initializer_and_the_entry_point_its_top_level_statements()
    {
        var scores = Scores(RuleTest.Extract(RuleTest.Compile(
            [(RuleTest.SnippetPath, """
            using System;

            if (args.Length > 0) { Console.WriteLine(args[0] ?? "none"); }

            class Base { protected Base(string name) { } }

            class Named : Base
            {
                public Named(string? name) : base(name ?? "none") { }
                bool Empty(string? name) => name is null or "";
            }
            """)],
            OutputKind.ConsoleApplication)));

        Assert.Equal(3, scores["<top-level statements>"]);
        Assert.Equal(2, scores["Named(string)"]);
        Assert.Equal(2, scores["Empty(string)"]);
    }

    private const string Fields = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        class Order { public string? Name; }

        class Subject
        {
            bool a, b, c, d;
            int x;
            int[] xs = [];
            string? s;

        """;

    /// <summary>The cyclomatic complexity of the one member in <paramref name="members"/>.</summary>
    private static int Score(string members)
    {
        var facts = RuleTest.Extract(RuleTest.Compile([(RuleTest.SnippetPath, $"{Fields}{members}\n}}\n")]));
        return Assert.Single(Scores(facts)).Value;
    }

    private static Dictionary<string, int> Scores(ProjectFacts facts) =>
        facts.ExecutableMembers.ToDictionary(member => member.Signature, member => member.Cyclomatic);
}

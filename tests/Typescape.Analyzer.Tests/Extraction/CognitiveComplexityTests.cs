using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;

namespace Typescape.Analyzer.Tests.Extraction;

// Written clean-room from the SonarSource whitepaper v1.7 and spec §5.1; Sonar's own test files are not used.
public class CognitiveComplexityTests
{
    [Fact]
    public void An_if_costs_one_plus_its_nesting_and_nests_its_body()
    {
        Assert.Equal(1, Score("void M() { if (a) { x++; } }"));
        Assert.Equal(3, Score("void M() { if (a) { if (b) { x++; } } }"));
        Assert.Equal(6, Score("void M() { if (a) { if (b) { if (c) { x++; } } } }"));
    }

    [Fact]
    public void An_else_if_or_else_costs_one_without_nesting_and_nests_its_body()
    {
        Assert.Equal(3, Score("void M() { if (a) { } else if (b) { } else { } }"));
        Assert.Equal(4, Score("void M() { if (a) { } else if (b) { if (c) { } } }"));
        Assert.Equal(4, Score("void M() { if (a) { } else { if (b) { } } }"));
        Assert.Equal(5, Score("void M() { if (a) { if (b) { } else if (c) { } else { } } }"));
    }

    [Fact]
    public void A_condition_sits_at_the_level_of_its_structure()
    {
        Assert.Equal(3, Score("void M() { if (xs.Any(item => item > 0 ? a : b)) { } }"));
        Assert.Equal(2, Score("void M() { while (a ? b : c) { } }"));
    }

    [Fact]
    public void A_conditional_expression_costs_one_plus_its_nesting_and_nests_its_branches()
    {
        Assert.Equal(1, Score("int M() => a ? 1 : 2;"));
        Assert.Equal(3, Score("int M() => a ? 1 : b ? 2 : 3;"));
        Assert.Equal(3, Score("int M() { if (a) { return b ? 1 : 2; } return 0; }"));
    }

    [Fact]
    public void A_switch_statement_costs_one_plus_its_nesting_and_its_cases_are_free()
    {
        Assert.Equal(1, Score("""
            string M()
            {
                switch (x)
                {
                    case 1: return "one";
                    case 2 when a: return "two";
                    default: return "many";
                }
            }
            """));
        Assert.Equal(3, Score("""void M() { foreach (var item in xs) { switch (item) { case 1: break; } } }"""));
    }

    [Fact]
    public void A_switch_expression_costs_one_plus_its_nesting_and_its_arms_are_free()
    {
        Assert.Equal(1, Score("""string M() => x switch { 1 => "one", 2 => "two", _ => "many" };"""));
        Assert.Equal(3, Score("""string M() => x switch { 1 => a switch { true => "yes", false => "no" }, _ => "many" };"""));
        Assert.Equal(3, Score("""void M() { if (a) { s = x switch { 1 => "one", _ => "many" }; } }"""));
    }

    [Fact]
    public void Each_loop_costs_one_plus_its_nesting_and_nests_its_body()
    {
        Assert.Equal(10, Score("""
            void M()
            {
                for (var i = 0; i < x; i++)
                {
                    foreach (var item in xs)
                    {
                        while (a)
                        {
                            do { } while (b);
                        }
                    }
                }
            }
            """));
        Assert.Equal(3, Score("""
            async Task M(IAsyncEnumerable<int> items, (int, int)[] pairs)
            {
                await foreach (var item in items) { }
                foreach (var (left, right) in pairs) { }
                foreach (int item in xs) { }
            }
            """));
    }

    [Fact]
    public void Each_catch_costs_one_plus_its_nesting_and_try_and_finally_are_free()
    {
        Assert.Equal(5, Score("""
            void M()
            {
                try
                {
                    x++;
                }
                catch (InvalidOperationException)
                {
                    if (a) { }
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (b) { }
                }
            }
            """));
        Assert.Equal(2, Score("void M() { try { } catch (Exception error) when (a && error is ArgumentException) { } }"));
        Assert.Equal(3, Score("void M() { if (a) { try { } catch { } } }"));
    }

    [Fact]
    public void Each_goto_costs_one()
    {
        Assert.Equal(4, Score("""
            void M()
            {
                switch (x)
                {
                    case 1: goto case 2;
                    case 2: goto default;
                    default: goto done;
                }

                done:
                x++;
            }
            """));
    }

    [Fact]
    public void Each_run_of_like_logical_operators_costs_one()
    {
        Assert.Equal(1, Score("bool M() => a && b && c;"));
        Assert.Equal(1, Score("bool M() => a || b || c;"));
        Assert.Equal(2, Score("bool M() => a && b || c;"));
        Assert.Equal(2, Score("bool M() => a || b && c;"));
        Assert.Equal(3, Score("bool M() => a && b && c || d || e && f;"));
        Assert.Equal(1, Score("bool M() => !a && b;"));
    }

    [Fact]
    public void A_parenthesis_or_negation_starts_a_new_sequence()
    {
        Assert.Equal(2, Score("bool M() => a && !(b && c);"));
        Assert.Equal(2, Score("bool M() => (a && b) && c;"));
        Assert.Equal(3, Score("bool M() => a || (b || c) || (d && e);"));
    }

    [Fact]
    public void Each_run_of_like_pattern_combinators_costs_one_and_not_is_free()
    {
        Assert.Equal(1, Score("bool M() => x is > 0 and < 10;"));
        Assert.Equal(1, Score("bool M() => x is 1 or 2 or 3;"));
        Assert.Equal(2, Score("bool M() => x is 1 and not 2 or 3;"));
        Assert.Equal(2, Score("bool M() => x is (> 0 and < 10) or 20;"));
        Assert.Equal(1, Score("bool M() => x is not (1 or 2);"));
        Assert.Equal(0, Score("bool M() => s is not null;"));
        Assert.Equal(3, Score("string M() => x switch { 1 or 2 => \"few\", > 2 and < 10 => \"some\", _ => \"many\" };"));
    }

    [Fact]
    public void Null_shorthands_and_throw_expressions_are_free()
    {
        Assert.Equal(0, Score("""
            int? M(int[]? numbers)
            {
                s ??= "none";
                var length = s?.Length ?? 0;
                var text = s ?? throw new ArgumentNullException();
                return numbers?[0];
            }
            """));
    }

    [Fact]
    public void Patterns_other_than_combinators_are_free()
    {
        Assert.Equal(0, Score("""
            void M(object? value, int[] numbers)
            {
                var word = value is string found;
                var named = value is Order { Name: { Length: > 0 } };
                var list = numbers is [1, .., > 5];
                var negative = x is < 0;
            }
            """));
    }

    [Fact]
    public void Boolean_operators_are_free()
    {
        Assert.Equal(0, Score("bool M() => a & b | c ^ d;"));
    }

    [Fact]
    public void Lambdas_anonymous_methods_and_local_functions_are_free_but_nest_their_bodies()
    {
        Assert.Equal(0, Score("void M() { Func<int, int> twice = item => item * 2; }"));
        Assert.Equal(2, Score("void M() { Action log = () => { if (a) { } }; }"));
        Assert.Equal(2, Score("void M() { Action log = delegate { if (a) { } }; }"));
        Assert.Equal(2, Score("void M() { Check(); void Check() { if (a) { } } }"));
        Assert.Equal(2, Score("void M() { var any = xs.Where(item => item > 0 ? a : b).Any(); }"));
        Assert.Equal(3, Score("void M() { Func<Action> outer = () => () => { if (a) { } }; }"));
        Assert.Equal(4, Score("void M() { if (a) { xs.ToList().ForEach(item => { if (b) { } }); } }"));
    }

    [Fact]
    public void A_method_that_calls_itself_costs_one_once()
    {
        Assert.Equal(2, Score("int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);"));
        Assert.Equal(2, Score("int Fibonacci(int n) => n < 2 ? n : Fibonacci(n - 1) + this.Fibonacci(n - 2);"));
        Assert.Equal(2, Score("T Repeat<T>(T value, int n) => n == 0 ? value : Repeat<T>(value, n - 1);"));
        Assert.Equal(1, Score("void Walk(int depth) { xs.ToList().ForEach(_ => Walk(depth + 1)); }"));
    }

    [Fact]
    public void A_member_that_runs_itself_through_its_property_indexer_event_operator_or_constructor_costs_one()
    {
        var scores = Scores("""
            using System;

            class Counter
            {
                int count;

                public Counter(int count) { this.count = count; if (count > 100) { _ = new Counter(count / 2); } }
                Counter() : this(0) { }

                public int Count { get => count > 0 ? Count - 1 : 0; set => count = value; }
                public int Total { get => count; set { if (value < 0) { Total = 0; } else { count = value; } } }
                public int this[int n] => n <= 0 ? 0 : this[n - 1] + 1;

                public event EventHandler? Changed
                {
                    add { if (value is null) { Changed += Ignore; } }
                    remove { }
                }

                public static Counter operator -(Counter counter) => counter.count > 0 ? -new Counter(counter.count - 1) : counter;
                public static explicit operator int(Counter counter) => counter.count < 0 ? (int)new Counter(-counter.count) : counter.count;
                public static implicit operator long(Counter counter) => counter.count == 0 ? 0 : Half(new Counter(counter.count / 2));

                static long Half(long value) => value / 2;
                static void Ignore(object? sender, EventArgs args) { }
            }
            """);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Counter(int)"] = 2,
                ["Counter()"] = 0,
                ["Count.get"] = 2,
                ["Count.set"] = 0,
                ["Total.get"] = 0,
                ["Total.set"] = 3,
                ["this[int].get"] = 2,
                ["Changed.add"] = 2,
                ["Changed.remove"] = 0,
                ["operator -(Counter)"] = 2,
                ["explicit operator int(Counter)"] = 2,
                ["implicit operator long(Counter)"] = 2,
                ["Half(long)"] = 0,
                ["Ignore(object, EventArgs)"] = 0,
            },
            scores);
    }

    [Fact]
    public void Only_the_member_itself_is_recursion()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Node
            {
                public Node? Parent;

                public int Depth(int start) => Depth(start.ToString());
                public int Depth(string start) => 0;

                public int Count() { return Next(); int Next() => Next(); }

                public virtual void Visit() { }

                public string Name { get => nameof(Name); }
            }

            class Leaf : Node
            {
                public override void Visit() => base.Visit();
            }

            class Other
            {
                public int Count(Node node) => node.Count();
            }
            """);

        Assert.Equal(7, facts.ExecutableMembers.Count);
        Assert.All(facts.ExecutableMembers, member => Assert.Equal(0, member.Cognitive));
    }

    [Fact]
    public void Extension_and_partial_methods_calling_themselves_are_recursion()
    {
        var scores = Scores(
        [
            ("App/Tree.cs", """
                class Node { public Node? Parent; }

                static class Tree
                {
                    public static int Depth(this Node node) => node.Parent is null ? 0 : node.Parent.Depth() + 1;

                    extension(Node node)
                    {
                        public int Height() => node.Parent is { } parent ? parent.Height() + 1 : 0;
                    }
                }

                partial class Walker
                {
                    partial void Step(int n);
                }
                """),
            ("App/Walker.cs", "partial class Walker { partial void Step(int n) { Step(n - 1); } }"),
        ]);

        Assert.Equal(2, scores["Depth(Node)"]);
        Assert.Equal(2, scores["extension(Node).Height()"]);
        Assert.Equal(1, scores["Step(int)"]);
    }

    [Fact]
    public void Using_lock_fixed_checked_return_yield_await_break_continue_and_throw_are_free()
    {
        Assert.Equal(0, Score("""
            async Task<int> Run(IDisposable resource, object gate)
            {
                using (resource) { }
                lock (gate) { }
                checked { x++; }
                await Task.Yield();
                return x;
            }

            IEnumerable<int> Items()
            {
                yield return 1;
                yield break;
            }
            """, "Run(IDisposable, object)"));
        Assert.Equal(0, Score("unsafe void Pin() { fixed (int* first = xs) { } }", allowedErrors: ["CS0227"]));
        Assert.Equal(0, Score("void Fail() => throw new InvalidOperationException();"));

        // Only the while and the if count.
        Assert.Equal(3, Score("void M() { while (a) { if (b) { continue; } break; } }"));
    }

    [Fact]
    public void Inactive_preprocessor_branches_are_free()
    {
        Assert.Equal(1, Score("""
            void M()
            {
            #if NEVER
                if (a) { }
            #elif ALSO_NEVER
                while (a) { }
            #else
                if (b) { }
            #endif
            }
            """));
    }

    [Fact]
    public void Query_syntax_is_free_and_does_not_nest()
    {
        Assert.Equal(0, Score("IEnumerable<int> M() => from item in xs where item > 0 orderby item select item;"));
        Assert.Equal(1, Score("IEnumerable<int> M() => from item in xs where item > 0 && a select item;"));
        Assert.Equal(1, Score("IEnumerable<int> M() => from item in xs where item > 0 ? a : b select item;"));
    }

    [Fact]
    public void Attribute_arguments_are_free()
    {
        Assert.Equal(0, Score("[System.ComponentModel.DefaultValue(true && false)] void M() { }"));
    }

    [Fact]
    public void Every_kind_of_executable_member_is_scored()
    {
        var scores = Scores("""
            using System;

            class Order
            {
                int total;
                bool open;

                public Order(int total) : this(total > 0 ? total : 0, true) { }
                Order(int total, bool open) { this.total = total; this.open = open; }
                ~Order() { if (open) { } }

                public int Total
                {
                    get { return open ? total : 0; }
                    set { if (value < 0) { throw new ArgumentOutOfRangeException(); } total = value; }
                }

                public string Label => open && total > 0 ? "open" : "closed";

                public static Order operator +(Order left, Order right) => left.open || right.open ? left : right;
                public static implicit operator int(Order order) => order.open ? order.total : 0;
            }
            """);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Order(int)"] = 1,
                ["Order(int, bool)"] = 0,
                ["~Order()"] = 1,
                ["Total.get"] = 1,
                ["Total.set"] = 1,
                ["Label.get"] = 2,
                ["operator +(Order, Order)"] = 2,
                ["implicit operator int(Order)"] = 1,
            },
            scores);
    }

    [Fact]
    public void The_entry_point_scores_its_top_level_statements_only()
    {
        var facts = RuleTest.Extract(RuleTest.Compile(
            [
                (RuleTest.SnippetPath, """
                    using System;

                    if (args.Length > 0)
                    {
                        Console.WriteLine(Greeting(args[0]));
                    }

                    static string Greeting(string name) => name.Length > 0 ? name : "you";

                    class Order
                    {
                        void Close() { while (true) { } }
                    }
                    """),
            ],
            OutputKind.ConsoleApplication));

        Assert.Equal(
            new Dictionary<string, int> { ["<top-level statements>"] = 3, ["Close()"] = 1 },
            facts.ExecutableMembers.ToDictionary(member => member.Signature, member => member.Cognitive));
    }

    // The whitepaper's worked examples, translated to C#. C# has no labelled continue, so `continue OUT` is a goto.
    [Fact]
    public void Whitepaper_sum_of_primes_is_7()
    {
        Assert.Equal(7, Score("""
            int SumOfPrimes(int max)
            {
                var total = 0;
                for (var i = 1; i <= max; ++i)
                {
                    for (var j = 2; j < i; ++j)
                    {
                        if (i % j == 0)
                        {
                            goto next;
                        }
                    }

                    total += i;
                next:;
                }

                return total;
            }
            """));
    }

    [Fact]
    public void Whitepaper_get_words_is_1()
    {
        Assert.Equal(1, Score("""
            string GetWords(int number)
            {
                switch (number)
                {
                    case 1: return "one";
                    case 2: return "a couple";
                    case 3: return "a few";
                    default: return "lots";
                }
            }
            """));
    }

    // Java's multi-catch `catch (A | B e)` is one clause, so one C# catch.
    [Fact]
    public void Whitepaper_nesting_example_is_9()
    {
        Assert.Equal(9, Score("""
            void Process()
            {
                try
                {
                    if (a)
                    {
                        for (var i = 0; i < 10; i++)
                        {
                            while (b) { }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    if (b) { }
                }
            }
            """));
    }

    [Fact]
    public void Whitepaper_lambda_example_is_2()
    {
        Assert.Equal(2, Score("""
            void Process()
            {
                Action run = () =>
                {
                    if (a) { }
                };
            }
            """));
    }

    [Fact]
    public void Whitepaper_mixed_operator_condition_is_4()
    {
        Assert.Equal(4, Score("void M() { if (a && b && c || d || e && f) { } }"));
    }

    [Fact]
    public void Whitepaper_null_check_costs_one_and_its_shorthand_none()
    {
        Assert.Equal(1, Score("string? Name(Order? order) { string? name = null; if (order != null) { name = order.Name; } return name; }"));
        Assert.Equal(0, Score("string? Name(Order? order) => order?.Name;"));
    }

    [Fact]
    public void Whitepaper_appendix_c_overridden_symbol_from_is_19()
    {
        var scores = Scores("""
            using System.Collections.Generic;

            class Symbol
            {
                public bool IsMethod;
                public bool IsStatic;
            }

            class ClassType
            {
                public bool IsUnknown;
                public IEnumerable<Symbol> Lookup(string name) => [];
            }

            class Resolver
            {
                static readonly Symbol Unknown = new();
                readonly string name = "";

                Symbol? OverriddenSymbolFrom(ClassType classType)
                {
                    if (classType.IsUnknown)                                  // +1
                    {
                        return Unknown;
                    }

                    var unknownFound = false;
                    foreach (var overrideSymbol in classType.Lookup(name))   // +1
                    {
                        if (overrideSymbol.IsMethod                           // +2 (nesting 1)
                            && !overrideSymbol.IsStatic)                      // +1
                        {
                            var method = overrideSymbol;
                            if (CanOverride(method))                          // +3 (nesting 2)
                            {
                                bool? overriding = CheckOverridingParameters(method, classType);
                                if (overriding == null)                       // +4 (nesting 3)
                                {
                                    if (!unknownFound)                        // +5 (nesting 4)
                                    {
                                        unknownFound = true;
                                    }
                                }
                                else if (overriding.Value)                    // +1
                                {
                                    return method;
                                }
                            }
                        }
                    }

                    if (unknownFound)                                         // +1
                    {
                        return Unknown;
                    }

                    return null;
                }

                static bool CanOverride(Symbol method) => true;
                static bool? CheckOverridingParameters(Symbol method, ClassType classType) => null;
            }
            """);

        Assert.Equal(19, scores["OverriddenSymbolFrom(ClassType)"]);
    }

    [Fact]
    public void Whitepaper_appendix_c_add_version_is_35()
    {
        var scores = Scores("""
            using System;
            using System.Threading;

            class Entry
            {
                public long Version;
                public Entry? Previous;
            }

            class Transaction
            {
                public bool IsActive;
                public int Status;
            }

            class TransactionIndex
            {
                public long WwDependency(long version, int status, long wait) => 0;
            }

            class RetryException(long versionHandle) : Exception
            {
                public long VersionHandle => versionHandle;
            }

            class RollbackException : Exception;

            class InterruptedException(Exception inner) : Exception("interrupted", inner);

            class TimelyResource
            {
                const long TimedOut = -1;
                const long Aborted = -2;
                const long DefaultMaxWaitTime = 1000;

                readonly TransactionIndex transactionIndex = new();
                Entry? first;

                void AddVersion(Entry entry, Transaction txn)
                {
                    var ti = transactionIndex;
                    while (true)                                                       // +1
                    {
                        try
                        {
                            lock (this)
                            {
                                if (first != null)                                     // +2 (nesting 1)
                                {
                                    if (first.Version > entry.Version)                 // +3 (nesting 2)
                                    {
                                        throw new RollbackException();
                                    }

                                    if (txn.IsActive)                                  // +3 (nesting 2)
                                    {
                                        for (var e = first; e != null; e = e.Previous) // +4 (nesting 3)
                                        {
                                            var version = e.Version;
                                            var depends = ti.WwDependency(version, txn.Status, 0);
                                            if (depends == TimedOut)                   // +5 (nesting 4)
                                            {
                                                throw new RetryException(version);
                                            }

                                            if (depends != 0                           // +5 (nesting 4)
                                                && depends != Aborted)                 // +1
                                            {
                                                throw new RollbackException();
                                            }
                                        }
                                    }
                                }

                                entry.Previous = first;
                                first = entry;
                                break;
                            }
                        }
                        catch (RetryException re)                                      // +2 (nesting 1)
                        {
                            try
                            {
                                var depends = transactionIndex.WwDependency(re.VersionHandle, txn.Status, DefaultMaxWaitTime);
                                if (depends != 0                                       // +3 (nesting 2)
                                    && depends != Aborted)                             // +1
                                {
                                    throw new RollbackException();
                                }
                            }
                            catch (ThreadInterruptedException ie)                      // +3 (nesting 2)
                            {
                                throw new InterruptedException(ie);
                            }
                        }
                        catch (ThreadInterruptedException ie)                          // +2 (nesting 1)
                        {
                            throw new InterruptedException(ie);
                        }
                    }
                }
            }
            """);

        Assert.Equal(35, scores["AddVersion(Entry, Transaction)"]);
    }

    [Fact]
    public void Whitepaper_appendix_c_to_regexp_is_20()
    {
        var scores = Scores("""
            using System.Text;

            static class WildcardPattern
            {
                const string SpecialChars = "()[]^$.{}+|";

                static string ToRegexp(string antPattern, string directorySeparator)
                {
                    var escapedDirectorySeparator = '\\' + directorySeparator;
                    var sb = new StringBuilder(antPattern.Length);
                    sb.Append('^');
                    var i = antPattern.StartsWith('/')                        // +1
                        || antPattern.StartsWith('\\') ? 1 : 0;               // +1
                    while (i < antPattern.Length)                             // +1
                    {
                        var ch = antPattern[i];
                        if (SpecialChars.IndexOf(ch) != -1)                   // +2 (nesting 1)
                        {
                            sb.Append('\\').Append(ch);
                        }
                        else if (ch == '*')                                   // +1
                        {
                            if (i + 1 < antPattern.Length                     // +3 (nesting 2)
                                && antPattern[i + 1] == '*')                  // +1
                            {
                                if (i + 2 < antPattern.Length                 // +4 (nesting 3)
                                    && IsSlash(antPattern[i + 2]))            // +1
                                {
                                    sb.Append("(?:.*").Append(escapedDirectorySeparator).Append("|)");
                                    i += 2;
                                }
                                else                                          // +1
                                {
                                    sb.Append(".*");
                                    i += 1;
                                }
                            }
                            else                                              // +1
                            {
                                sb.Append("[^").Append(escapedDirectorySeparator).Append("]*?");
                            }
                        }
                        else if (ch == '?')                                   // +1
                        {
                            sb.Append("[^").Append(escapedDirectorySeparator).Append(']');
                        }
                        else if (IsSlash(ch))                                 // +1
                        {
                            sb.Append(escapedDirectorySeparator);
                        }
                        else                                                  // +1
                        {
                            sb.Append(ch);
                        }

                        i++;
                    }

                    sb.Append('$');
                    return sb.ToString();
                }

                static bool IsSlash(char ch) => ch is '/' or '\\';
            }
            """);

        Assert.Equal(20, scores["ToRegexp(string, string)"]);
    }

    // The JavaScript idioms `options || (options = {})` and `callback && callback(…)` stay logical operators, as in the
    // original; their C# shorthands `??=` and `?.` would be free.
    [Fact]
    public void Whitepaper_appendix_c_save_is_20()
    {
        var scores = Scores("""
            using System;

            class Options;

            class Model
            {
                object? saveEvent;
                bool changed;

                public Model Save(object? options, Func<Exception?, bool>? callback)
                {
                    var self = this;
                    if (options is Func<Exception?, bool> function)            // +1
                    {
                        callback = function;
                        options = new Options();
                    }

                    _ = options != null || (options = new Options()) != null;  // +1
                    self.Validate(self.ToJson(), err =>
                    {
                        if (err != null)                                       // +2 (nesting 1)
                        {
                            _ = callback != null && callback(err);             // +1
                            return;
                        }

                        self.Sync(self.IsNew() ? "create" : "update", options, (error, response) =>  // +2 (nesting 1)
                        {
                            if (error != null)                                 // +3 (nesting 2)
                            {
                                self.Fire("error", error);
                            }
                            else                                               // +1
                            {
                                if (self.saveEvent == null)                    // +4 (nesting 3)
                                {
                                    self.saveEvent = self.Publish("save");
                                }

                                if (response != null)                          // +4 (nesting 3)
                                {
                                    self.SetAttributes(self.Parse(response), options);
                                }

                                self.changed = false;
                                self.Fire("save", response);
                            }

                            _ = callback != null && callback(null);            // +1
                        });
                    });

                    return self;
                }

                object ToJson() => this;
                bool IsNew() => true;
                void Validate(object json, Action<Exception?> done) { }
                void Sync(string action, object? options, Action<Exception?, object?> done) { }
                object Publish(string name) => name;
                void Fire(string name, object? facade) { }
                object Parse(object response) => response;
                void SetAttributes(object parsed, object? options) { }
            }
            """);

        Assert.Equal(20, scores["Save(object, Func<Exception, bool>)"]);
    }

    private const string Fields = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        class Order { public string? Name; }

        class Subject
        {
            bool a, b, c, d, e, f;
            int x;
            int[] xs = [];
            string? s;

        """;

    /// <summary>The score of the one member, or the one named by <paramref name="signature"/>, in <paramref name="members"/>.</summary>
    /// <param name="allowedErrors">The compiler error codes the snippet may have, e.g. <c>CS0227</c>.</param>
    private static int Score(string members, string? signature = null, IReadOnlyCollection<string>? allowedErrors = null)
    {
        var facts = RuleTest.Extract(RuleTest.Compile([(RuleTest.SnippetPath, $"{Fields}{members}\n}}\n")], allowedErrors: allowedErrors));
        var scores = Scores(facts);
        return signature is null ? Assert.Single(scores).Value : scores[signature];
    }

    private static Dictionary<string, int> Scores(string source) => Scores([(RuleTest.SnippetPath, source)]);

    private static Dictionary<string, int> Scores(IEnumerable<(string Path, string Source)> documents) => Scores(RuleTest.Extract(documents));

    private static Dictionary<string, int> Scores(ProjectFacts facts) =>
        facts.ExecutableMembers.ToDictionary(member => member.Signature, member => member.Cognitive);
}

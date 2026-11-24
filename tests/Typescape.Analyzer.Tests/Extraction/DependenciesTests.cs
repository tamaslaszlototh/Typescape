namespace Typescape.Analyzer.Tests.Extraction;

public class DependenciesTests
{
    [Fact]
    public void A_base_class_and_an_interfaces_base_interface_are_inherits()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Entity { }
            class Order : Entity, IDisposable { public void Dispose() { } }
            record Line(int Count) : Base(Count);
            record Base(int Count);
            interface IRepository : IDisposable { }
            enum Status : byte { Open }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void An_interface_in_the_base_list_of_a_class_struct_or_record_is_implements()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order : IDisposable { public void Dispose() { } }
            struct Money : IComparable { public int CompareTo(object? other) => 0; }
            record Line : IDisposable { public void Dispose() { } }
            record struct Tax : IFormattable { public string ToString(string? format, IFormatProvider? provider) => ""; }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_positional_record_parameter_is_also_member_type_unless_the_record_declares_the_property_itself()
    {
        var facts = RuleTest.Extract("""
            using System;

            record Line(Guid Id, decimal Price)
            {
                public decimal Price { get; } = Price;
            }

            class Order(Uri source);
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Type_constraints_and_explicit_interface_specifiers_are_signature_but_not_in_a_local_function()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Repository<T> : IEquatable<Guid> where T : class, IComparable, new()
            {
                bool IEquatable<Guid>.Equals(Guid other) => false;

                public void Save<TItem>(TItem item) where TItem : IComparer<Uri>
                {
                    void Local<TLocal>() where TLocal : IDisposable { }
                }
            }

            delegate void Handler<T>(T item) where T : Exception;
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void The_declared_type_of_a_field_property_event_or_indexer_is_member_type()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                Guid id, other;
                public DateTime Placed { get; set; }
                public event EventHandler? Changed;
                public event Action Closed { add { } remove { } }
                public decimal this[int line] => 0m;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Parameter_and_return_types_of_members_indexers_primary_constructors_and_delegates_are_signature()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order(Guid id)
            {
                public Order(Uri source) : this(Guid.Empty) { }
                public TimeSpan Age(DateTime now) => TimeSpan.Zero;
                public void Close(ref decimal total, out string note) => note = "";
                public int this[long line] => 0;
                public static Order operator +(Order order, Version version) => order;
                public static implicit operator Exception(Order order) => new();
            }

            delegate Version Parse(string text);
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void An_applied_attributes_type_is_attribute()
    {
        var facts = RuleTest.Extract("""
            using System;

            [Serializable]
            class Order
            {
                [Obsolete] int count;

                [return: CLSCompliant(false)]
                public uint Count([CLSCompliant(true)] int line) => 0;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_type_argument_anywhere_is_generic_arg_and_the_generic_type_gets_the_kind_of_its_position()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Order : List<Guid>
            {
                Dictionary<string, List<Uri>> lines = new();

                public void Run() => Array.Empty<Version>();
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Arrays_pointers_nullables_refs_and_tuples_are_their_element_types_at_the_same_kind()
    {
        // The snippet needs /unsafe for its pointer, which only that error stands for.
        var facts = RuleTest.Extract(RuleTest.Compile(
            [(RuleTest.SnippetPath, """
                using System;

                unsafe class Order
                {
                    Guid[] ids = [];
                    int* cursor;
                    DateTime? closed;
                    (Uri Source, Version Version) origin;

                    public ref decimal Total(scoped ref long seed) => throw new();
                }
                """)],
            allowedErrors: ["CS0227"]));

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_using_alias_is_the_type_it_names_with_its_type_arguments_as_generic_arg()
    {
        var facts = RuleTest.Extract("""
            using Lines = System.Collections.Generic.List<System.Guid>;
            using Ids = System.Guid[];

            class Order
            {
                Lines lines = [];
                Ids ids = [];
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_types_use_of_itself_is_no_dependency()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Node<T> : IEquatable<Node<T>>
            {
                Node<T>? next;
                public Node<T> Next(Node<int> other) => this;
                public bool Equals(Node<T>? other) => false;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void The_source_is_the_innermost_own_type_and_the_target_the_open_generic_definition()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Outer
            {
                Inner first;

                class Inner
                {
                    Outer owner;
                    Dictionary<Inner, Outer> map;
                }
            }

            static class Extensions
            {
                extension(Outer outer)
                {
                    public Uri Source => null!;
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Top_level_statements_belong_to_the_synthesized_Program_type()
    {
        var facts = RuleTest.Extract(RuleTest.Compile(
            [(RuleTest.SnippetPath, "System.Console.WriteLine(System.Array.Empty<System.Guid>());")],
            Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_use_in_a_generated_declaration_or_member_is_generated()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.CodeDom.Compiler;

            partial class Order
            {
                Guid id;
                [GeneratedCode("tool", "1.0")] Uri source;
            }

            [GeneratedCode("tool", "1.0")]
            partial class Order
            {
                Version version;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }
}

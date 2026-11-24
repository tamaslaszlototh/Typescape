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

    [Fact]
    public void An_object_creation_is_new_of_the_created_type_target_typed_ones_included()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Order
            {
                public void Place(bool valid)
                {
                    var source = new Uri("https://example.com");
                    var lines = new List<Guid>();
                    Exception error = new();
                    DateTime? placed = new();
                    var copy = new Order();
                    if (!valid) throw new InvalidOperationException();
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void An_invocation_is_call_of_the_called_methods_type_and_an_extension_method_of_its_static_class()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Linq;

            class Order
            {
                public void Place(string note, Guid[] ids, Action done)
                {
                    Console.WriteLine(note.Trim());
                    var list = ids
                        .Where(id => id != Guid.Empty)
                        .ToList();
                    note.Shout();
                    done();
                    Close();
                    Local();

                    void Local() { }
                }

                void Close() { }
            }

            static class Texts
            {
                extension(string text)
                {
                    public string Shout() => text;
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_field_property_event_or_indexer_access_or_a_method_group_is_member_access_of_the_members_type()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Order
            {
                int count;

                public void Place(string? note, List<Guid> ids, (int Count, Uri Source) origin)
                {
                    count = int.MaxValue + note?.Length ?? 0;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => { };
                    var first = ids[0];
                    var day = DayOfWeek.Monday;
                    Action<string> write = Console.WriteLine;
                    var builder = new UriBuilder { Port = origin.Count };
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Locals_casts_typeof_is_as_patterns_nameof_and_default_are_type_usage()
    {
        var facts = RuleTest.Extract("""
            using System;
            using System.Collections.Generic;

            class Order
            {
                public object Place(object value, Uri[] sources)
                {
                    Guid id = default;
                    var inferred = sources;
                    var version = (Version)value;
                    var type = typeof(DateTime);
                    if (value is TimeSpan || value as Exception is not null) { }
                    if (value is Uri { IsFile: true } file) { }
                    switch (value)
                    {
                        case DateOnly:
                        case TimeOnly time:
                            break;
                    }
                    foreach (Uri source in sources) { }
                    try { } catch (FormatException) { }
                    int.TryParse("1", out int parsed);
                    var names = new[] { nameof(Decimal), nameof(Environment.NewLine) };
                    var empty = new Half[0];
                    return default(TimeZoneInfo)!;
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_constant_after_is_is_member_access_of_its_type_not_type_usage()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                const int Limit = 10;

                public bool Check(object value) => value is DayOfWeek.Monday || value is Math.PI || value is Limit || value is Version;
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void Lambda_and_local_function_parameter_return_and_constraint_types_are_type_usage()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                public void Place()
                {
                    Func<Uri, bool> isFile = (Uri source) => source.IsFile;
                    var parse = Version (string text) => new(text);
                    Action<Guid> log = delegate (Guid id) { };

                    TimeSpan Age<T>(DateTime now) where T : IComparable => TimeSpan.Zero;
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, dependencies: true));
    }

    [Fact]
    public void A_use_in_a_lambda_or_local_function_belongs_to_the_executable_member_and_in_an_initializer_to_none()
    {
        var facts = RuleTest.Extract("""
            using System;

            class Order
            {
                Func<Uri> source = () => new Uri("https://example.com");
                public Guid Id { get; } = Guid.NewGuid();

                public Version Current => new(1, 0);

                public string Note
                {
                    get => DateTime.Now.ToString();
                }

                public void Place()
                {
                    Action run = () => Console.WriteLine();
                    Local();

                    void Local() => Math.Abs(1);
                }
            }
            """);

        RuleTest.Match(RuleTest.Keep(facts, members: true, dependencies: true));
    }
}

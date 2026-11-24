namespace Typescape.Analyzer.Tests.Extraction;

public class AnalyzerErrorsTests
{
    [Fact]
    public void An_exception_on_one_type_is_an_analyzer_error_of_its_document_and_the_other_types_survive()
    {
        // A member outside any type (CS9348) makes Roslyn declare an implicit <invalid-global-code> type through the
        // whole compilation unit, which has no top-level statements for OwnTypes to span. If OwnTypes learns to skip
        // that type, this test needs another edge case.
        var facts = RuleTest.Extract(RuleTest.Compile(
        [
            ("App/Broken.cs", "public int stray;\npublic class Order { }"),
            ("App/Customer.cs", "public class Customer { }"),
        ], allowedErrors: ["CS9348"]));

        RuleTest.Match(facts);
    }
}

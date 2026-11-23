namespace Typescape.Snapshot.Tests;

public class StableKeysTests
{
    [Fact]
    public void Type_key_is_assembly_and_documentation_comment_id()
    {
        Assert.Equal("App|T:App.Service`1", StableKeys.Type("App", "T:App.Service`1"));
    }

    [Fact]
    public void Executable_member_key_is_type_key_and_documentation_comment_id()
    {
        var typeKey = StableKeys.Type("App", "T:App.Service`1");

        Assert.Equal(
            "App|T:App.Service`1|M:App.Service`1.Run(System.Int32)",
            StableKeys.ExecutableMember(typeKey, "M:App.Service`1.Run(System.Int32)"));
    }

    [Theory]
    [InlineData("", "T:A")]
    [InlineData("A", "")]
    public void Empty_parts_are_rejected(string assembly, string documentationCommentId)
    {
        Assert.Throws<ArgumentException>(() => StableKeys.Type(assembly, documentationCommentId));
    }
}

namespace Typescape.Snapshot.Tests;

public class StableKeysTests
{
    [Fact]
    public void Type_key_is_assembly_and_documentation_comment_id()
    {
        Assert.Equal("App|T:App.Service`1", StableKeys.Type("App", "T:App.Service`1"));
    }

    [Fact]
    public void File_local_type_key_adds_the_document_key()
    {
        Assert.Equal("App|T:Helper@App/A.cs", StableKeys.Type("App", "T:Helper", "App/A.cs"));
    }

    [Fact]
    public void Empty_file_local_document_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => StableKeys.Type("App", "T:Helper", ""));
    }

    [Fact]
    public void Member_key_is_type_key_and_documentation_comment_id()
    {
        var typeKey = StableKeys.Type("App", "T:App.Service`1");

        Assert.Equal(
            "App|T:App.Service`1|M:App.Service`1.Run(System.Int32)",
            StableKeys.Member(typeKey, "M:App.Service`1.Run(System.Int32)"));
    }

    [Fact]
    public void Member_documentation_comment_id_is_read_back_from_the_member_key()
    {
        var typeKey = StableKeys.Type("App", "T:App.Service`1");
        var memberKey = StableKeys.Member(typeKey, "F:App.Service`1.Count");

        Assert.Equal("F:App.Service`1.Count", StableKeys.MemberDocumentationCommentId(memberKey, typeKey));
    }

    [Fact]
    public void Member_documentation_comment_id_is_read_back_from_a_file_local_member_key()
    {
        var typeKey = StableKeys.Type("App", "T:Helper", "App/A.cs");
        var memberKey = StableKeys.Member(typeKey, "M:Helper.Run");

        Assert.Equal("M:Helper.Run", StableKeys.MemberDocumentationCommentId(memberKey, typeKey));
    }

    [Theory]
    [InlineData("App|T:App.C|F:App.C.X", "App|T:App.D")]
    [InlineData("App|T:App.C", "App|T:App.C")]
    [InlineData("App|T:App.C|", "App|T:App.C")]
    [InlineData("App|T:App.CF:App.C.X", "App|T:App.C")]
    public void Member_documentation_comment_id_rejects_a_key_of_another_type(string memberKey, string typeKey)
    {
        Assert.Throws<ArgumentException>(() => StableKeys.MemberDocumentationCommentId(memberKey, typeKey));
    }

    [Theory]
    [InlineData("", "T:A")]
    [InlineData("A", "")]
    public void Empty_parts_are_rejected(string assembly, string documentationCommentId)
    {
        Assert.Throws<ArgumentException>(() => StableKeys.Type(assembly, documentationCommentId));
    }
}

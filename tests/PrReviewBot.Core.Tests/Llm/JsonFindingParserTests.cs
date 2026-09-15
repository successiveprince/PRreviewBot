using PrReviewBot.Core.Llm;

namespace PrReviewBot.Core.Tests.Llm;

public class JsonFindingParserTests
{
    private readonly JsonFindingParser _parser = new();

    [Fact]
    public void Parse_WellFormedJson_ReturnsSuggestions()
    {
        const string content = "[{\"filePath\":\"Foo.cs\",\"line\":3,\"severity\":\"Warning\",\"message\":\"Rename this field.\"}]";

        var suggestions = _parser.Parse(content);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Foo.cs", suggestion.FilePath);
        Assert.Equal(3, suggestion.Line);
        Assert.Equal("Warning", suggestion.Severity);
        Assert.Equal("Rename this field.", suggestion.Message);
    }

    [Fact]
    public void Parse_JsonWrappedInMarkdownFences_StripsFencesAndParses()
    {
        const string content = "```json\n[{\"filePath\":\"Foo.cs\",\"line\":3,\"severity\":\"Warning\",\"message\":\"Rename this field.\"}]\n```";

        var suggestions = _parser.Parse(content);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Foo.cs", suggestion.FilePath);
        Assert.Equal("Rename this field.", suggestion.Message);
    }

    [Fact]
    public void Parse_JsonWrappedInPlainFences_StripsFencesAndParses()
    {
        const string content = "```\n[{\"filePath\":\"Foo.cs\",\"line\":1,\"severity\":\"Info\",\"message\":\"Nit.\"}]\n```";

        var suggestions = _parser.Parse(content);

        Assert.Single(suggestions);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsEmptyListWithoutThrowing()
    {
        var suggestions = _parser.Parse("this is not json at all");

        Assert.Empty(suggestions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespaceContent_ReturnsEmptyList(string? content)
    {
        var suggestions = _parser.Parse(content);

        Assert.Empty(suggestions);
    }
}

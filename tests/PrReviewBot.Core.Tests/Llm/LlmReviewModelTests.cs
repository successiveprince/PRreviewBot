using Moq;
using PrReviewBot.Core.Analysis;
using PrReviewBot.Core.Llm;

namespace PrReviewBot.Core.Tests.Llm;

public class LlmReviewModelTests
{
    private const string WellFormedCompletion = "[{\"filePath\":\"Foo.cs\",\"line\":3,\"severity\":\"Warning\",\"message\":\"Rename this field.\"}]";

    [Fact]
    public async Task GetSuggestionsAsync_ComposesPromptCallsChatClientAndParsesResult()
    {
        var chatClient = new Mock<IChatCompletionClient>();
        chatClient
            .Setup(c => c.GetCompletionAsync(
                ReviewPromptBuilder.SystemPrompt,
                It.Is<string>(userPrompt => userPrompt.Contains("Foo.cs") && userPrompt.Contains("SA1234"))))
            .ReturnsAsync(WellFormedCompletion);

        var findings = new List<CodeFinding> { new("Foo.cs", 3, "SA1234", "Warning", "Field naming") };
        var model = new LlmReviewModel(chatClient.Object, new ReviewPromptBuilder(), new JsonFindingParser());

        var suggestions = await model.GetSuggestionsAsync("Foo.cs", "@@ -1 +1 @@\n+field", findings);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Foo.cs", suggestion.FilePath);
        Assert.Equal(3, suggestion.Line);
        Assert.Equal("Rename this field.", suggestion.Message);
        chatClient.Verify(
            c => c.GetCompletionAsync(ReviewPromptBuilder.SystemPrompt, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ChatClientReturnsMalformedContent_ReturnsEmptyListWithoutThrowing()
    {
        var chatClient = new Mock<IChatCompletionClient>();
        chatClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("this is not json at all");

        var model = new LlmReviewModel(chatClient.Object, new ReviewPromptBuilder(), new JsonFindingParser());

        var suggestions = await model.GetSuggestionsAsync("Foo.cs", "@@ -1 +1 @@\n+field", []);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ChatClientThrowsHttpRequestException_ReturnsEmptyListWithoutThrowing()
    {
        var chatClient = new Mock<IChatCompletionClient>();
        chatClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var model = new LlmReviewModel(chatClient.Object, new ReviewPromptBuilder(), new JsonFindingParser());

        var suggestions = await model.GetSuggestionsAsync("Foo.cs", "@@ -1 +1 @@\n+field", []);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ChatClientThrowsJsonException_ReturnsEmptyListWithoutThrowing()
    {
        var chatClient = new Mock<IChatCompletionClient>();
        chatClient
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.Text.Json.JsonException("bad envelope"));

        var model = new LlmReviewModel(chatClient.Object, new ReviewPromptBuilder(), new JsonFindingParser());

        var suggestions = await model.GetSuggestionsAsync("Foo.cs", "@@ -1 +1 @@\n+field", []);

        Assert.Empty(suggestions);
    }
}

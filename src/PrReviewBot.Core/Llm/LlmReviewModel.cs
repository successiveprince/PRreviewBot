namespace PrReviewBot.Core.Llm;

using System.Text.Json;
using PrReviewBot.Core.Analysis;

/// <summary>
/// Generates review suggestions by sending diff context and static-analysis findings to an
/// LLM. Provider-agnostic: composes <see cref="IChatCompletionClient"/>, <see cref="ReviewPromptBuilder"/>,
/// and <see cref="JsonFindingParser"/> rather than talking to any HTTP API directly.
/// </summary>
public sealed class LlmReviewModel : IReviewModel
{
    private readonly IChatCompletionClient chatClient;
    private readonly ReviewPromptBuilder promptBuilder;
    private readonly JsonFindingParser jsonParser;

    public LlmReviewModel(IChatCompletionClient chatClient, ReviewPromptBuilder promptBuilder, JsonFindingParser jsonParser)
    {
        this.chatClient = chatClient;
        this.promptBuilder = promptBuilder;
        this.jsonParser = jsonParser;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewSuggestion>> GetSuggestionsAsync(string filePath, string diffHunk, IReadOnlyList<CodeFinding> findings)
    {
        try
        {
            var userPrompt = this.promptBuilder.BuildUserPrompt(filePath, diffHunk, findings);
            var completion = await this.chatClient.GetCompletionAsync(ReviewPromptBuilder.SystemPrompt, userPrompt).ConfigureAwait(false);
            return this.jsonParser.Parse(completion);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"LlmReviewModel: could not parse the LLM response as JSON: {ex.Message}");
            return Array.Empty<ReviewSuggestion>();
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"LlmReviewModel: request to the LLM provider failed: {ex.Message}");
            return Array.Empty<ReviewSuggestion>();
        }
    }
}

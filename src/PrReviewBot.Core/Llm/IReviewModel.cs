namespace PrReviewBot.Core.Llm;

using PrReviewBot.Core.Analysis;

/// <summary>
/// Turns raw static-analysis findings into readable, contextual review comments via an LLM.
/// </summary>
public interface IReviewModel
{
    Task<IReadOnlyList<ReviewSuggestion>> GetSuggestionsAsync(string filePath, string diffHunk, IReadOnlyList<CodeFinding> findings);
}

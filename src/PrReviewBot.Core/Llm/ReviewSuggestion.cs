namespace PrReviewBot.Core.Llm;

/// <summary>
/// A single LLM-generated review comment for one line of a file.
/// </summary>
public sealed record ReviewSuggestion(string FilePath, int Line, string Severity, string Message);

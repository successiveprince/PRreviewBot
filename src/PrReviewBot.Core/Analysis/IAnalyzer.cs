namespace PrReviewBot.Core.Analysis;

/// <summary>
/// Runs static analysis over a single source file's content.
/// </summary>
public interface IAnalyzer
{
    Task<IReadOnlyList<CodeFinding>> AnalyzeAsync(string filePath, string sourceCode);
}

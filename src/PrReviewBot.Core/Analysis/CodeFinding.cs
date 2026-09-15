namespace PrReviewBot.Core.Analysis;

/// <summary>
/// A single static-analysis result for one line of a file.
/// </summary>
public sealed record CodeFinding(string FilePath, int Line, string RuleId, string Severity, string Message);

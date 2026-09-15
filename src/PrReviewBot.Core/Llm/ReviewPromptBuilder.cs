namespace PrReviewBot.Core.Llm;

using System.Text;
using System.Text.Json;
using PrReviewBot.Core.Analysis;

/// <summary>
/// Builds the system and user prompts sent to an LLM to turn a diff hunk and static-analysis
/// findings into review suggestions. Provider-agnostic - has no knowledge of HTTP or any
/// specific model/vendor.
/// </summary>
public sealed class ReviewPromptBuilder
{
    public const string SystemPrompt =
        "You are a senior code reviewer. Given a file's diff hunk and static-analysis findings, " +
        "produce readable, contextual review comments. Respond with ONLY a JSON array of objects " +
        "shaped exactly as { \"filePath\": string, \"line\": number, \"severity\": string, \"message\": string }. " +
        "Do not wrap the array in markdown code fences and do not include any prose before or after it.";

    public string BuildUserPrompt(string filePath, string diffHunk, IReadOnlyList<CodeFinding> findings)
        => new StringBuilder()
            .AppendLine($"File: {filePath}")
            .AppendLine()
            .AppendLine("Diff hunk:")
            .AppendLine(diffHunk)
            .AppendLine()
            .AppendLine("Static analysis findings (JSON):")
            .AppendLine(JsonSerializer.Serialize(findings))
            .ToString();
}

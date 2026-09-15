namespace PrReviewBot.Core.Llm;

using System.Text.Json;

/// <summary>
/// Defensively parses an LLM's raw completion text into <see cref="ReviewSuggestion"/>s.
/// Small/free models on any provider tend to misbehave the same way (wrapping JSON in markdown
/// fences, adding stray prose, returning truncated output), so this never throws - malformed
/// input just yields an empty list.
/// </summary>
public sealed class JsonFindingParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<ReviewSuggestion> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<ReviewSuggestion>();
        }

        try
        {
            var cleaned = StripMarkdownFences(content);
            var suggestions = JsonSerializer.Deserialize<List<ReviewSuggestion>>(cleaned, SerializerOptions);
            return suggestions ?? (IReadOnlyList<ReviewSuggestion>)Array.Empty<ReviewSuggestion>();
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"JsonFindingParser: could not parse the LLM response as JSON: {ex.Message}");
            return Array.Empty<ReviewSuggestion>();
        }
    }

    private static string StripMarkdownFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var contentStart = firstNewline + 1;
        var withoutOpeningFence = firstNewline >= 0 ? trimmed[contentStart..] : trimmed;

        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        var fenced = closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex] : withoutOpeningFence;
        return fenced.Trim();
    }
}

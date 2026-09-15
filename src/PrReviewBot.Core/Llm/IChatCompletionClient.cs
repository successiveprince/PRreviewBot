namespace PrReviewBot.Core.Llm;

/// <summary>
/// A provider-agnostic chat completion call: give it a system and user prompt, get back the
/// model's answer text. Implementations own everything provider-specific (endpoint, auth,
/// model id, request/response shape).
/// </summary>
public interface IChatCompletionClient
{
    Task<string> GetCompletionAsync(string systemPrompt, string userPrompt);
}

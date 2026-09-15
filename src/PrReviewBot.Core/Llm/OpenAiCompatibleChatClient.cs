namespace PrReviewBot.Core.Llm;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// Calls any OpenAI-compatible <c>/chat/completions</c> endpoint. Provider-specific details
/// (base address, auth header, model id) are supplied by the caller - via the injected
/// <see cref="HttpClient"/> and constructor parameter - not hardcoded here, so this class is
/// reusable across OpenRouter, Azure OpenAI, Groq, local Ollama, etc.
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatCompletionClient
{
    private static readonly TimeSpan[] DefaultRetryDelaysOn429 = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) };
    private static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient httpClient;
    private readonly string model;
    private readonly TimeSpan[] retryDelaysOn429;

    /// <param name="httpClient">Pre-configured with BaseAddress and auth header.</param>
    /// <param name="model">The provider's model id.</param>
    /// <param name="retryDelaysOn429">
    /// Fallback backoff per retry attempt when a 429 response has no <c>Retry-After</c> header.
    /// Defaults to real-world delays; tests can pass near-zero values to stay fast.
    /// </param>
    public OpenAiCompatibleChatClient(HttpClient httpClient, string model, TimeSpan[]? retryDelaysOn429 = null)
    {
        this.httpClient = httpClient;
        this.model = model;
        this.retryDelaysOn429 = retryDelaysOn429 ?? DefaultRetryDelaysOn429;
    }

    /// <inheritdoc/>
    public async Task<string> GetCompletionAsync(string systemPrompt, string userPrompt)
    {
        var requestBody = BuildRequestBody(this.model, systemPrompt, userPrompt);
        using var response = await this.SendWithRetryAsync(requestBody).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ExtractMessageContent(responseJson) ?? string.Empty;
    }

    private static string BuildRequestBody(string model, string systemPrompt, string userPrompt)
    {
        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string? ExtractMessageContent(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentElement))
        {
            return null;
        }

        return contentElement.GetString();
    }

    private static HttpRequestMessage CreateRequest(string requestBody)
        => new(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, TimeSpan fallback)
    {
        // Prefer the server's own guidance (either delta-seconds or an HTTP-date) over our
        // fallback backoff, but never wait an unreasonably long time for it.
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return Clamp(delta);
        }

        if (retryAfter?.Date is { } date)
        {
            var untilDate = date - DateTimeOffset.UtcNow;
            return untilDate > TimeSpan.Zero ? Clamp(untilDate) : TimeSpan.Zero;
        }

        return fallback;
    }

    private static TimeSpan Clamp(TimeSpan delay)
        => delay > MaxRetryAfterDelay ? MaxRetryAfterDelay : delay;

    private async Task<HttpResponseMessage> SendWithRetryAsync(string requestBody)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await this.httpClient.SendAsync(CreateRequest(requestBody)).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= this.retryDelaysOn429.Length)
            {
                return response;
            }

            var delay = GetRetryDelay(response, this.retryDelaysOn429[attempt]);
            response.Dispose();
            await Task.Delay(delay).ConfigureAwait(false);
        }
    }
}

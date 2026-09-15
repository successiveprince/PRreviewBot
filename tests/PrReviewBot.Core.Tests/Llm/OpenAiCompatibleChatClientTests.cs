using System.Net;
using System.Text.Json;
using Moq;
using Moq.Protected;
using PrReviewBot.Core.Llm;

namespace PrReviewBot.Core.Tests.Llm;

public class OpenAiCompatibleChatClientTests
{
    private const string WellFormedChatResponse =
        """
        {
            "choices": [
                {
                    "message": {
                        "content": "the completion text"
                    }
                }
            ]
        }
        """;

    [Fact]
    public async Task GetCompletionAsync_SendsExpectedRequestShapeAndReturnsCompletion()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handlerMock = CreateHandlerMock((request, body) =>
        {
            capturedRequest = request;
            capturedBody = body;
            return OkResponse(WellFormedChatResponse);
        });

        var client = CreateClient(handlerMock, "my-model");

        var completion = await client.GetCompletionAsync("system says", "user says");

        Assert.Equal("the completion text", completion);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), capturedRequest.RequestUri);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("my-model", payload.RootElement.GetProperty("model").GetString());

        var messages = payload.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system says", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("user says", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetCompletionAsync_TooManyRequestsThenSuccess_RetriesAndReturnsCompletion()
    {
        var callCount = 0;
        var handlerMock = CreateHandlerMock((_, _) =>
        {
            callCount++;
            return callCount == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : OkResponse(WellFormedChatResponse);
        });

        var client = CreateClient(handlerMock, "my-model");

        var completion = await client.GetCompletionAsync("system", "user");

        Assert.Equal(2, callCount);
        Assert.Equal("the completion text", completion);
    }

    [Fact]
    public async Task GetCompletionAsync_AlwaysTooManyRequests_RetriesTwiceThenThrows()
    {
        var callCount = 0;
        var handlerMock = CreateHandlerMock((_, _) =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });

        var client = CreateClient(handlerMock, "my-model");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCompletionAsync("system", "user"));

        // Initial attempt + 2 retries.
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task GetCompletionAsync_NonJsonResponseBody_ThrowsJsonException()
    {
        var handlerMock = CreateHandlerMock((_, _) => OkResponse("this is not json at all"));

        var client = CreateClient(handlerMock, "my-model");

        await Assert.ThrowsAnyAsync<JsonException>(() => client.GetCompletionAsync("system", "user"));
    }

    [Fact]
    public async Task GetCompletionAsync_TooManyRequestsWithRetryAfterHeader_HonorsHeaderInsteadOfFallbackDelay()
    {
        var callCount = 0;
        var handlerMock = CreateHandlerMock((_, _) =>
        {
            callCount++;
            if (callCount > 1)
            {
                return OkResponse(WellFormedChatResponse);
            }

            var tooManyRequests = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            tooManyRequests.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return tooManyRequests;
        });

        var client = CreateClient(handlerMock, "my-model");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var completion = await client.GetCompletionAsync("system", "user");
        stopwatch.Stop();

        Assert.Equal(2, callCount);
        Assert.Equal("the completion text", completion);

        // A zero-second Retry-After should be honored immediately, not the (slower) fallback delay.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Expected a fast retry, took {stopwatch.Elapsed}.");
    }

    private static HttpResponseMessage OkResponse(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static Mock<HttpMessageHandler> CreateHandlerMock(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
                return respond(request, body);
            });

        return handlerMock;
    }

    private static OpenAiCompatibleChatClient CreateClient(Mock<HttpMessageHandler> handlerMock, string model)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };

        // Fast fallback delays so retry tests don't slow down the suite; production uses the
        // class's real-world defaults.
        var fastRetryDelays = new[] { TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5) };
        return new OpenAiCompatibleChatClient(httpClient, model, fastRetryDelays);
    }
}

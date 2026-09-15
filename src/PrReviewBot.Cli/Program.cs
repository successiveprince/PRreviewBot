using Microsoft.Extensions.DependencyInjection;
using Octokit;
using System.Net.Http.Headers;
using PrReviewBot.Cli;
using PrReviewBot.Core.Analysis;
using PrReviewBot.Core.DiffParsing;
using PrReviewBot.Core.GitHubIntegration;
using PrReviewBot.Core.Llm;
using PrReviewBot.Core.Pipeline;

// Load GITHUB_TOKEN/OPENROUTER_API_KEY/OPENROUTER_MODEL from a local ".env" file, if present.
// Real env vars (e.g. GitHub Actions secrets) always take precedence - see .env.example.
EnvFile.Load();

// --- Swap these three values to point PrReviewBot at a different OpenAI-compatible provider ---
// (e.g. Azure OpenAI, Groq, a local Ollama server) - nothing else in the app needs to change.
const string ProviderBaseAddress = "https://openrouter.ai/api/v1/";
const string ProviderApiKeyEnvVar = "OPENROUTER_API_KEY";
// meta-llama/llama-3.3-70b-instruct:free was discontinued by OpenRouter (404) - keep this pointed at a currently-live free model.
const string DefaultModel = "nvidia/nemotron-3-ultra-550b-a55b:free";
// --- end provider-specific configuration ---

var prNumber = ParseIntArgument(args, "--pr");
var owner = ParseStringArgument(args, "--owner") ?? GetRepositoryPart(0);
var repo = ParseStringArgument(args, "--repo") ?? GetRepositoryPart(1);
if (prNumber is null || owner is null || repo is null)
{
    Console.Error.WriteLine("Usage: PrReviewBot.Cli --pr <number> [--owner <owner> --repo <repo>]");
    Console.Error.WriteLine("(owner/repo default to the GITHUB_REPOSITORY env var, e.g. inside a GitHub Action)");
    return 1;
}

var services = new ServiceCollection();

services.AddHttpClient();
services.AddHttpClient("ChatCompletion", client =>
{
    client.BaseAddress = new Uri(ProviderBaseAddress);
    client.Timeout = TimeSpan.FromSeconds(60);

    var apiKey = Environment.GetEnvironmentVariable(ProviderApiKeyEnvVar);
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
});
services.AddScoped<ReviewPromptBuilder>();
services.AddScoped<JsonFindingParser>();
services.AddScoped<IChatCompletionClient>(CreateChatCompletionClient);
services.AddScoped<IReviewModel, LlmReviewModel>();

services.AddSingleton<IGitHubClient>(CreateGitHubClient());

services.AddScoped<IDiffParser, UnifiedDiffParser>();
services.AddScoped<IAnalyzer, RoslynAnalyzer>();
services.AddScoped<IGitHubReviewClient, OctokitReviewClient>();
services.AddScoped<PipelineRunner>();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var pipelineRunner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();

try
{
    await pipelineRunner.RunAsync(owner, repo, prNumber.Value);
}
catch (Exception ex)
{
    // Top-level boundary: fail the Action step cleanly (exit code 1, readable message) instead
    // of an unhandled-exception stack trace dump.
    Console.Error.WriteLine($"PrReviewBot failed: {ex.Message}");
    return 1;
}

return 0;

static int? ParseIntArgument(string[] args, string name)
{
    var value = ParseStringArgument(args, name);
    return value is not null && int.TryParse(value, out var parsed) ? parsed : null;
}

static string? ParseStringArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return null;
}

static string? GetRepositoryPart(int index)
{
    // GitHub Actions sets GITHUB_REPOSITORY to "owner/repo" for every workflow run.
    var parts = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")?.Split('/', 2);
    return parts?.Length == 2 ? parts[index] : null;
}

static IChatCompletionClient CreateChatCompletionClient(IServiceProvider serviceProvider)
{
    var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ChatCompletion");
    var model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
    return new OpenAiCompatibleChatClient(httpClient, string.IsNullOrWhiteSpace(model) ? DefaultModel : model);
}

static GitHubClient CreateGitHubClient()
{
    var client = new GitHubClient(new Octokit.ProductHeaderValue("PrReviewBot"));
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
    {
        client.Credentials = new Credentials(token);
    }

    return client;
}

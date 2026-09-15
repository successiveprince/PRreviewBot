namespace PrReviewBot.Core.Pipeline;

using PrReviewBot.Core.Analysis;
using PrReviewBot.Core.DiffParsing;
using PrReviewBot.Core.GitHubIntegration;
using PrReviewBot.Core.Llm;

/// <summary>
/// Orchestrates the end-to-end review pipeline: fetch the PR's changed files, run static
/// analysis on commentable lines, ask the LLM for readable suggestions, and post them back
/// as a single review.
/// </summary>
public sealed class PipelineRunner : IDisposable
{
    private readonly IDiffParser diffParser;
    private readonly IAnalyzer analyzer;
    private readonly IGitHubReviewClient gitHubReviewClient;
    private readonly IReviewModel reviewModel;
    private readonly SemaphoreSlim reviewConcurrencyLimiter;

    /// <param name="maxConcurrentFileReviews">
    /// How many changed files to analyze/review at once. Bounded so a large PR doesn't hammer
    /// the LLM provider (and its rate limits) with dozens of simultaneous requests.
    /// </param>
    public PipelineRunner(
        IDiffParser diffParser,
        IAnalyzer analyzer,
        IGitHubReviewClient gitHubReviewClient,
        IReviewModel reviewModel,
        int maxConcurrentFileReviews = 3)
    {
        this.diffParser = diffParser;
        this.analyzer = analyzer;
        this.gitHubReviewClient = gitHubReviewClient;
        this.reviewModel = reviewModel;
        this.reviewConcurrencyLimiter = new SemaphoreSlim(maxConcurrentFileReviews);
    }

    public async Task RunAsync(string owner, string repo, int pullRequestNumber)
    {
        var changedFiles = await this.gitHubReviewClient.GetChangedFilesAsync(owner, repo, pullRequestNumber).ConfigureAwait(false);

        var suggestionsPerFile = await Task.WhenAll(changedFiles.Select(this.ReviewFileWithLimiterAsync)).ConfigureAwait(false);
        var suggestionsToPost = suggestionsPerFile.SelectMany(suggestions => suggestions).ToList();

        if (suggestionsToPost.Count == 0)
        {
            Console.WriteLine("No issues found - skipping review.");
            return;
        }

        var headSha = await this.gitHubReviewClient.GetPullRequestHeadShaAsync(owner, repo, pullRequestNumber).ConfigureAwait(false);
        await this.gitHubReviewClient.PostReviewAsync(owner, repo, pullRequestNumber, headSha, suggestionsToPost).ConfigureAwait(false);

        Console.WriteLine($"Posted a review with {suggestionsToPost.Count} comment(s) on PR #{pullRequestNumber}.");
    }

    /// <inheritdoc/>
    public void Dispose() => this.reviewConcurrencyLimiter.Dispose();

    private async Task<IReadOnlyList<ReviewSuggestion>> ReviewFileWithLimiterAsync(ChangedFile file)
    {
        await this.reviewConcurrencyLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            return await this.ReviewFileAsync(file).ConfigureAwait(false);
        }
        finally
        {
            this.reviewConcurrencyLimiter.Release();
        }
    }

    private async Task<IReadOnlyList<ReviewSuggestion>> ReviewFileAsync(ChangedFile file)
    {
        if (!file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.RawContent is null)
        {
            return Array.Empty<ReviewSuggestion>();
        }

        var commentableLines = this.diffParser.GetCommentableLines(file.Patch);
        if (commentableLines.Count == 0)
        {
            return Array.Empty<ReviewSuggestion>();
        }

        var findings = await this.analyzer.AnalyzeAsync(file.Path, file.RawContent).ConfigureAwait(false);
        var commentableFindings = findings.Where(f => commentableLines.Contains(f.Line)).ToList();
        if (commentableFindings.Count == 0)
        {
            // Nothing to hand the LLM - avoid a wasted (and potentially hallucinated) call.
            return Array.Empty<ReviewSuggestion>();
        }

        var suggestions = await this.reviewModel.GetSuggestionsAsync(file.Path, file.Patch, commentableFindings).ConfigureAwait(false);

        // Defense in depth: don't trust the LLM to have echoed back the line number - or even the
        // file path - correctly. We already know which file this is, so pin it ourselves.
        return suggestions
            .Where(s => commentableLines.Contains(s.Line))
            .Select(s => new ReviewSuggestion(file.Path, s.Line, s.Severity, s.Message))
            .ToList();
    }
}

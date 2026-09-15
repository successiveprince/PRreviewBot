namespace PrReviewBot.Core.GitHubIntegration;

using PrReviewBot.Core.Llm;

/// <summary>
/// Reads pull request data from, and posts reviews back to, GitHub.
/// </summary>
public interface IGitHubReviewClient
{
    /// <summary>
    /// Gets the files changed by a pull request, including each file's diff patch and its
    /// raw content at the PR head commit.
    /// </summary>
    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string owner, string repo, int pullRequestNumber);

    /// <summary>
    /// Gets the SHA of a pull request's head commit, needed to anchor a posted review.
    /// </summary>
    Task<string> GetPullRequestHeadShaAsync(string owner, string repo, int pullRequestNumber);

    /// <summary>
    /// Posts a single review with one inline comment per suggestion to a pull request.
    /// </summary>
    Task PostReviewAsync(string owner, string repo, int pullRequestNumber, string commitSha, IReadOnlyList<ReviewSuggestion> suggestions);
}

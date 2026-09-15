namespace PrReviewBot.Core.GitHubIntegration;

/// <summary>
/// A single file changed by a pull request, with its diff patch and (when available) the
/// full file content at the PR head commit.
/// </summary>
public sealed record ChangedFile(string Path, string Patch, string? RawContent);

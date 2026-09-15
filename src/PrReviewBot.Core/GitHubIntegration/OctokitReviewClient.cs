namespace PrReviewBot.Core.GitHubIntegration;

using Octokit;
using PrReviewBot.Core.DiffParsing;
using PrReviewBot.Core.Llm;

/// <inheritdoc cref="IGitHubReviewClient"/>
public sealed class OctokitReviewClient : IGitHubReviewClient
{
    private readonly IGitHubClient client;
    private readonly IDiffParser diffParser;

    public OctokitReviewClient(IGitHubClient client, IDiffParser diffParser)
    {
        this.client = client;
        this.diffParser = diffParser;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string owner, string repo, int pullRequestNumber)
    {
        var headSha = await this.GetPullRequestHeadShaAsync(owner, repo, pullRequestNumber).ConfigureAwait(false);
        var files = await this.client.PullRequest.Files(owner, repo, pullRequestNumber).ConfigureAwait(false);

        var changedFiles = new List<ChangedFile>(files.Count);
        foreach (var file in files)
        {
            var rawContent = await this.TryGetRawContentAsync(owner, repo, file.FileName, headSha).ConfigureAwait(false);
            changedFiles.Add(new ChangedFile(file.FileName, file.Patch ?? string.Empty, rawContent));
        }

        return changedFiles;
    }

    /// <inheritdoc/>
    public async Task<string> GetPullRequestHeadShaAsync(string owner, string repo, int pullRequestNumber)
    {
        var pullRequest = await this.client.PullRequest.Get(owner, repo, pullRequestNumber).ConfigureAwait(false);
        return pullRequest.Head.Sha;
    }

    /// <inheritdoc/>
    public async Task PostReviewAsync(string owner, string repo, int pullRequestNumber, string commitSha, IReadOnlyList<ReviewSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        // Octokit's review-comment DTO only understands the legacy "diff position" (not the
        // file's line number), so re-fetch each file's patch to translate line -> position.
        var files = await this.client.PullRequest.Files(owner, repo, pullRequestNumber).ConfigureAwait(false);
        var positionsByFile = files.ToDictionary(f => f.FileName, f => this.diffParser.GetDiffPositionsByLine(f.Patch));

        var review = new PullRequestReviewCreate
        {
            CommitId = commitSha,
            Body = "Automated review from PrReviewBot.",
            Event = PullRequestReviewEvent.Comment,
        };

        foreach (var suggestion in suggestions)
        {
            if (!positionsByFile.TryGetValue(suggestion.FilePath, out var positions)
                || !positions.TryGetValue(suggestion.Line, out var diffPosition))
            {
                // The suggestion doesn't map to a line that's actually in the current diff - skip
                // it rather than fail the whole review over one bad comment.
                continue;
            }

            review.Comments.Add(new DraftPullRequestReviewComment(suggestion.Message, suggestion.FilePath, diffPosition));
        }

        if (review.Comments.Count == 0)
        {
            return;
        }

        await this.client.PullRequest.Review.Create(owner, repo, pullRequestNumber, review).ConfigureAwait(false);
    }

    private async Task<string?> TryGetRawContentAsync(string owner, string repo, string path, string reference)
    {
        try
        {
            var contents = await this.client.Repository.Content.GetAllContentsByRef(owner, repo, path, reference).ConfigureAwait(false);
            return contents.Count > 0 ? contents[0].Content : null;
        }
        catch (NotFoundException)
        {
            // The file no longer exists at the head commit (e.g. it was deleted in this PR).
            return null;
        }
        catch (ApiException ex)
        {
            // e.g. the file is too large for the Content API (>100 MB) - skip it rather than
            // fail the whole run over one file we can't review anyway.
            Console.Error.WriteLine($"OctokitReviewClient: could not fetch content for '{path}': {ex.Message}");
            return null;
        }
    }
}

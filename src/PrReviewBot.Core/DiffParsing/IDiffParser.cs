namespace PrReviewBot.Core.DiffParsing;

/// <summary>
/// Parses a GitHub-style unified diff patch for a single file.
/// </summary>
public interface IDiffParser
{
    /// <summary>
    /// Returns the set of new-file line numbers that a review comment can be attached to,
    /// i.e. lines that appear as added or context lines in the patch.
    /// </summary>
    /// <param name="patch">The unified diff patch text for a single file.</param>
    IReadOnlySet<int> GetCommentableLines(string? patch);

    /// <summary>
    /// Maps each commentable new-file line number to its GitHub "diff position" - the value
    /// the GitHub API expects (via <c>DraftPullRequestReviewComment.Position</c>) to anchor an
    /// inline review comment, which is not the same as the file's line number.
    /// </summary>
    /// <param name="patch">The unified diff patch text for a single file.</param>
    IReadOnlyDictionary<int, int> GetDiffPositionsByLine(string? patch);
}

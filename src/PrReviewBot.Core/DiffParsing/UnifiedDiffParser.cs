namespace PrReviewBot.Core.DiffParsing;

using System.Text.RegularExpressions;

/// <inheritdoc cref="IDiffParser"/>
public sealed partial class UnifiedDiffParser : IDiffParser
{
    public IReadOnlySet<int> GetCommentableLines(string? patch)
        => new HashSet<int>(this.GetDiffPositionsByLine(patch).Keys);

    public IReadOnlyDictionary<int, int> GetDiffPositionsByLine(string? patch)
    {
        var positionsByLine = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(patch))
        {
            return positionsByLine;
        }

        var newLineNumber = 0;
        var diffPosition = 0;
        var isFirstLine = true;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // The first hunk header is the diff-position reference point (position 0) and is
            // never counted itself; every other line - including later hunk headers - is.
            if (!isFirstLine)
            {
                diffPosition++;
            }

            isFirstLine = false;

            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                newLineNumber = int.Parse(hunkMatch.Groups["newStart"].Value);
                continue;
            }

            if (line.Length == 0)
            {
                // A blank line inside a hunk is an unchanged (context) line.
                positionsByLine[newLineNumber] = diffPosition;
                newLineNumber++;
                continue;
            }

            switch (line[0])
            {
                case '+':
                    positionsByLine[newLineNumber] = diffPosition;
                    newLineNumber++;
                    break;
                case '-':
                    // Removed lines don't exist in the new file, so the new-line counter doesn't move.
                    break;
                case '\\':
                    // "\ No newline at end of file" marker - not an actual line.
                    break;
                default:
                    positionsByLine[newLineNumber] = diffPosition;
                    newLineNumber++;
                    break;
            }
        }

        return positionsByLine;
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<newStart>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderRegex();
}

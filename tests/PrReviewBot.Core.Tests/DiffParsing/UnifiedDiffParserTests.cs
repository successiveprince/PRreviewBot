using PrReviewBot.Core.DiffParsing;

namespace PrReviewBot.Core.Tests.DiffParsing;

public class UnifiedDiffParserTests
{
    private readonly UnifiedDiffParser _parser = new();

    [Fact]
    public void SingleHunkWithOnlyAdditions_ReturnsAllAddedLines()
    {
        var patch = "@@ -1,2 +1,4 @@\n" +
                    " line1\n" +
                    "+line2\n" +
                    "+line3\n" +
                    " line4";

        var result = _parser.GetCommentableLines(patch);

        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void HunkWithContextAdditionsAndDeletions_ExcludesDeletedLinesAndKeepsNumberingCorrect()
    {
        var patch = "@@ -1,5 +1,4 @@\n" +
                    " line1\n" +
                    "-line2\n" +
                    " line3\n" +
                    "+line4\n" +
                    " line5";

        var result = _parser.GetCommentableLines(patch);

        // new file: 1=line1, 2=line3, 3=line4, 4=line5 (line2 was removed, no new-line number allocated)
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void MultipleHunks_ReturnsCommentableLinesFromEachHunk()
    {
        var patch = "@@ -1,2 +1,2 @@\n" +
                    " line1\n" +
                    "+line2\n" +
                    "@@ -10,2 +10,3 @@\n" +
                    " line10\n" +
                    "+line11\n" +
                    " line12";

        var result = _parser.GetCommentableLines(patch);

        Assert.Equal(new HashSet<int> { 1, 2, 10, 11, 12 }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyOrNullPatch_ReturnsEmptySet(string? patch)
    {
        var result = _parser.GetCommentableLines(patch);

        Assert.Empty(result);
    }

    [Fact]
    public void GetDiffPositionsByLine_SingleHunkNoDeletions_PositionsMatchLineNumbers()
    {
        var patch = "@@ -1,2 +1,4 @@\n" +
                    " line1\n" +
                    "+line2\n" +
                    "+line3\n" +
                    " line4";

        var result = _parser.GetDiffPositionsByLine(patch);

        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4 }, result);
    }

    [Fact]
    public void GetDiffPositionsByLine_AfterADeletion_PositionsDivergeFromLineNumbers()
    {
        var patch = "@@ -1,5 +1,4 @@\n" +
                    " line1\n" +
                    "-line2\n" +
                    " line3\n" +
                    "+line4\n" +
                    " line5";

        var result = _parser.GetDiffPositionsByLine(patch);

        // Line 2 (new file) is at diff position 3 because the deleted line still counts toward position.
        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 3, [3] = 4, [4] = 5 }, result);
    }

    [Fact]
    public void GetDiffPositionsByLine_MultipleHunks_PositionKeepsIncreasingAcrossHunks()
    {
        var patch = "@@ -1,2 +1,2 @@\n" +
                    " line1\n" +
                    "+line2\n" +
                    "@@ -10,2 +10,3 @@\n" +
                    " line10\n" +
                    "+line11\n" +
                    " line12";

        var result = _parser.GetDiffPositionsByLine(patch);

        Assert.Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [10] = 4, [11] = 5, [12] = 6 }, result);
    }
}

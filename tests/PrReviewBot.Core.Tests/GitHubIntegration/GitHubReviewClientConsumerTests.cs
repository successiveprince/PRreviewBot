using Moq;
using PrReviewBot.Core.GitHubIntegration;

namespace PrReviewBot.Core.Tests.GitHubIntegration;

public class GitHubReviewClientConsumerTests
{
    [Fact]
    public async Task GetReviewableFilesAsync_FiltersOutFilesWithoutAPatch()
    {
        var mockClient = new Mock<IGitHubReviewClient>();
        mockClient
            .Setup(c => c.GetChangedFilesAsync("octocat", "hello-world", 42))
            .ReturnsAsync(new List<ChangedFile>
            {
                new("src/Foo.cs", "@@ -1,1 +1,2 @@\n line1\n+line2", "line1\nline2"),
                new("assets/logo.png", string.Empty, null),
            });

        var reviewableFiles = await GetReviewableFilesAsync(mockClient.Object, "octocat", "hello-world", 42);

        var file = Assert.Single(reviewableFiles);
        Assert.Equal("src/Foo.cs", file.Path);
        mockClient.Verify(c => c.GetChangedFilesAsync("octocat", "hello-world", 42), Times.Once);
    }

    [Fact]
    public async Task GetReviewableFilesAsync_NoChangedFiles_ReturnsEmpty()
    {
        var mockClient = new Mock<IGitHubReviewClient>();
        mockClient
            .Setup(c => c.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChangedFile>());

        var reviewableFiles = await GetReviewableFilesAsync(mockClient.Object, "octocat", "hello-world", 7);

        Assert.Empty(reviewableFiles);
    }

    /// <summary>
    /// Stand-in for the pipeline step that will consume <see cref="IGitHubReviewClient"/>:
    /// only files with a non-empty patch (i.e. not binary/unparseable) can be reviewed.
    /// </summary>
    private static async Task<IReadOnlyList<ChangedFile>> GetReviewableFilesAsync(
        IGitHubReviewClient client, string owner, string repo, int pullRequestNumber)
    {
        var files = await client.GetChangedFilesAsync(owner, repo, pullRequestNumber);
        return files.Where(f => !string.IsNullOrEmpty(f.Patch)).ToList();
    }
}

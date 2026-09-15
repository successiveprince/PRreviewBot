using System.Net;
using Moq;
using Octokit;
using PrReviewBot.Core.DiffParsing;
using PrReviewBot.Core.GitHubIntegration;
using PrReviewBot.Core.Llm;

namespace PrReviewBot.Core.Tests.GitHubIntegration;

public class OctokitReviewClientTests
{
    [Fact]
    public async Task GetChangedFilesAsync_ReturnsPatchAndDecodedRawContentPerFile()
    {
        var (clientMock, pullRequestMock, contentMock, _) = CreateGitHubClientMock();
        pullRequestMock.Setup(p => p.Get("octocat", "hello-world", 42)).ReturnsAsync(CreatePullRequest("sha123"));
        pullRequestMock
            .Setup(p => p.Files("octocat", "hello-world", 42))
            .ReturnsAsync(new List<PullRequestFile> { CreatePullRequestFile("Foo.cs", "@@ -1 +1 @@\n+x") });
        contentMock
            .Setup(c => c.GetAllContentsByRef("octocat", "hello-world", "Foo.cs", "sha123"))
            .ReturnsAsync(new List<RepositoryContent> { CreateRepositoryContent("public class Foo {}") });

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        var changedFiles = await client.GetChangedFilesAsync("octocat", "hello-world", 42);

        var file = Assert.Single(changedFiles);
        Assert.Equal("Foo.cs", file.Path);
        Assert.Equal("@@ -1 +1 @@\n+x", file.Patch);
        Assert.Equal("public class Foo {}", file.RawContent);
    }

    [Fact]
    public async Task GetChangedFilesAsync_DeletedFile_RawContentIsNullWithoutThrowing()
    {
        var (clientMock, pullRequestMock, contentMock, _) = CreateGitHubClientMock();
        pullRequestMock.Setup(p => p.Get("octocat", "hello-world", 42)).ReturnsAsync(CreatePullRequest("sha123"));
        pullRequestMock
            .Setup(p => p.Files("octocat", "hello-world", 42))
            .ReturnsAsync(new List<PullRequestFile> { CreatePullRequestFile("Deleted.cs", "@@ -1 +0,0 @@\n-x") });
        contentMock
            .Setup(c => c.GetAllContentsByRef("octocat", "hello-world", "Deleted.cs", "sha123"))
            .ThrowsAsync(new NotFoundException("not found", HttpStatusCode.NotFound));

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        var changedFiles = await client.GetChangedFilesAsync("octocat", "hello-world", 42);

        Assert.Null(Assert.Single(changedFiles).RawContent);
    }

    [Fact]
    public async Task GetChangedFilesAsync_ContentApiError_RawContentIsNullWithoutThrowing()
    {
        // e.g. the file is too large for the Content API (>100 MB) - a real, if rare, edge case.
        var (clientMock, pullRequestMock, contentMock, _) = CreateGitHubClientMock();
        pullRequestMock.Setup(p => p.Get("octocat", "hello-world", 42)).ReturnsAsync(CreatePullRequest("sha123"));
        pullRequestMock
            .Setup(p => p.Files("octocat", "hello-world", 42))
            .ReturnsAsync(new List<PullRequestFile> { CreatePullRequestFile("Huge.cs", "@@ -1 +1 @@\n+x") });
        contentMock
            .Setup(c => c.GetAllContentsByRef("octocat", "hello-world", "Huge.cs", "sha123"))
            .ThrowsAsync(new ApiException("too large", HttpStatusCode.Forbidden));

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        var changedFiles = await client.GetChangedFilesAsync("octocat", "hello-world", 42);

        Assert.Null(Assert.Single(changedFiles).RawContent);
    }

    [Fact]
    public async Task GetPullRequestHeadShaAsync_ReturnsHeadSha()
    {
        var (clientMock, pullRequestMock, _, _) = CreateGitHubClientMock();
        pullRequestMock.Setup(p => p.Get("octocat", "hello-world", 42)).ReturnsAsync(CreatePullRequest("sha123"));

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        var sha = await client.GetPullRequestHeadShaAsync("octocat", "hello-world", 42);

        Assert.Equal("sha123", sha);
    }

    [Fact]
    public async Task PostReviewAsync_MapsLineToDiffPositionAndPostsOnce()
    {
        var (clientMock, pullRequestMock, _, reviewMock) = CreateGitHubClientMock();
        pullRequestMock
            .Setup(p => p.Files("octocat", "hello-world", 42))
            .ReturnsAsync(new List<PullRequestFile>
            {
                CreatePullRequestFile("Foo.cs", "@@ -1,2 +1,3 @@\n line1\n+line2\n line3"),
            });

        PullRequestReviewCreate? capturedReview = null;
        reviewMock
            .Setup(r => r.Create("octocat", "hello-world", 42, It.IsAny<PullRequestReviewCreate>()))
            .Callback<string, string, int, PullRequestReviewCreate>((_, _, _, review) => capturedReview = review)
            .ReturnsAsync((PullRequestReview)null!);

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());
        var suggestions = new List<ReviewSuggestion> { new("Foo.cs", 2, "Warning", "Add a doc comment.") };

        await client.PostReviewAsync("octocat", "hello-world", 42, "sha123", suggestions);

        Assert.NotNull(capturedReview);
        Assert.Equal("sha123", capturedReview!.CommitId);
        var comment = Assert.Single(capturedReview.Comments);
        Assert.Equal("Foo.cs", comment.Path);
        Assert.Equal("Add a doc comment.", comment.Body);
        Assert.Equal(2, comment.Position); // line 2 (new-file) is diff position 2 in this hunk.
        reviewMock.Verify(r => r.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PullRequestReviewCreate>()), Times.Once);
    }

    [Fact]
    public async Task PostReviewAsync_SuggestionOutsideDiff_IsSkippedAndNotPosted()
    {
        var (clientMock, pullRequestMock, _, reviewMock) = CreateGitHubClientMock();
        pullRequestMock
            .Setup(p => p.Files("octocat", "hello-world", 42))
            .ReturnsAsync(new List<PullRequestFile> { CreatePullRequestFile("Foo.cs", "@@ -1,2 +1,3 @@\n line1\n+line2\n line3") });

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        // Line 99 doesn't exist in the diff above, so no comment can be mapped to a position.
        var suggestions = new List<ReviewSuggestion> { new("Foo.cs", 99, "Warning", "Stale suggestion.") };

        await client.PostReviewAsync("octocat", "hello-world", 42, "sha123", suggestions);

        reviewMock.Verify(
            r => r.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PullRequestReviewCreate>()),
            Times.Never);
    }

    [Fact]
    public async Task PostReviewAsync_NoSuggestions_NeverCallsGitHub()
    {
        var (clientMock, pullRequestMock, _, reviewMock) = CreateGitHubClientMock();

        var client = new OctokitReviewClient(clientMock.Object, new UnifiedDiffParser());

        await client.PostReviewAsync("octocat", "hello-world", 42, "sha123", Array.Empty<ReviewSuggestion>());

        pullRequestMock.Verify(p => p.Files(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        reviewMock.Verify(
            r => r.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PullRequestReviewCreate>()),
            Times.Never);
    }

    private static (
        Mock<IGitHubClient> Client,
        Mock<IPullRequestsClient> PullRequest,
        Mock<IRepositoryContentsClient> Content,
        Mock<IPullRequestReviewsClient> Review) CreateGitHubClientMock()
    {
        var clientMock = new Mock<IGitHubClient>();
        var pullRequestMock = new Mock<IPullRequestsClient>();
        var contentMock = new Mock<IRepositoryContentsClient>();
        var repositoryMock = new Mock<IRepositoriesClient>();
        var reviewMock = new Mock<IPullRequestReviewsClient>();

        clientMock.Setup(c => c.PullRequest).Returns(pullRequestMock.Object);
        clientMock.Setup(c => c.Repository).Returns(repositoryMock.Object);
        repositoryMock.Setup(r => r.Content).Returns(contentMock.Object);
        pullRequestMock.Setup(p => p.Review).Returns(reviewMock.Object);

        return (clientMock, pullRequestMock, contentMock, reviewMock);
    }

    private static PullRequestFile CreatePullRequestFile(string fileName, string patch)
        => new(sha: "filesha", fileName: fileName, status: "modified", additions: 1, deletions: 0, changes: 1,
            blobUrl: null!, rawUrl: null!, contentsUrl: null!, patch: patch, previousFileName: null!);

    private static RepositoryContent CreateRepositoryContent(string decodedContent)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(decodedContent));
        return new RepositoryContent(
            name: "Foo.cs", path: "Foo.cs", sha: "sha", size: decodedContent.Length, type: ContentType.File,
            downloadUrl: null!, url: null!, gitUrl: null!, htmlUrl: null!, encoding: "base64",
            encodedContent: encoded, target: null!, submoduleGitUrl: null!);
    }

    private static PullRequest CreatePullRequest(string headSha)
    {
        var head = new GitReference(null!, null!, null!, null!, headSha, null!, null!);
        return new PullRequest(
            id: 1, nodeId: null!, url: null!, htmlUrl: null!, diffUrl: null!, patchUrl: null!, issueUrl: null!,
            statusesUrl: null!, number: 42, state: ItemState.Open, title: null!, body: null!,
            createdAt: DateTimeOffset.MinValue, updatedAt: DateTimeOffset.MinValue, closedAt: null, mergedAt: null,
            head: head, @base: null!, user: null!, assignee: null!, assignees: null!, draft: false, mergeable: null,
            mergeableState: null, mergedBy: null!, mergeCommitSha: null!, comments: 0, commits: 0, additions: 0,
            deletions: 0, changedFiles: 0, milestone: null!, locked: false, maintainerCanModify: null,
            requestedReviewers: null!, requestedTeams: null!, labels: null!, activeLockReason: null);
    }
}

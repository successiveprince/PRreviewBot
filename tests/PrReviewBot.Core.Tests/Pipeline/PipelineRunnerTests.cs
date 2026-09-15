using Moq;
using PrReviewBot.Core.Analysis;
using PrReviewBot.Core.DiffParsing;
using PrReviewBot.Core.GitHubIntegration;
using PrReviewBot.Core.Llm;
using PrReviewBot.Core.Pipeline;

namespace PrReviewBot.Core.Tests.Pipeline;

public class PipelineRunnerTests
{
    private const string Patch =
        "@@ -1,3 +1,4 @@\n" +
        " line1\n" +
        "+line2\n" +
        " line3\n" +
        "+line4";

    [Fact]
    public async Task RunAsync_FindingsAndSuggestionsOutsideDiffLines_AreDroppedBeforePosting()
    {
        var changedFile = new ChangedFile("Foo.cs", Patch, "raw content");

        var gitHubClient = new Mock<IGitHubReviewClient>();
        gitHubClient
            .Setup(c => c.GetChangedFilesAsync("octocat", "hello-world", 42))
            .ReturnsAsync(new List<ChangedFile> { changedFile });
        gitHubClient
            .Setup(c => c.GetPullRequestHeadShaAsync("octocat", "hello-world", 42))
            .ReturnsAsync("sha123");

        var analyzer = new Mock<IAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync("Foo.cs", "raw content"))
            .ReturnsAsync(new List<CodeFinding>
            {
                new("Foo.cs", 2, "SA1234", "Warning", "In-diff finding"),
                new("Foo.cs", 99, "SA9999", "Warning", "Out-of-diff finding"),
            });

        var reviewModel = new Mock<IReviewModel>();
        reviewModel
            .Setup(m => m.GetSuggestionsAsync(
                "Foo.cs",
                Patch,
                It.Is<IReadOnlyList<CodeFinding>>(findings => findings.Count == 1 && findings[0].Line == 2)))
            .ReturnsAsync(new List<ReviewSuggestion>
            {
                new("Foo.cs", 2, "Warning", "In-diff suggestion"),
                new("Foo.cs", 99, "Warning", "Out-of-diff suggestion"),
            });

        var runner = new PipelineRunner(new UnifiedDiffParser(), analyzer.Object, gitHubClient.Object, reviewModel.Object);

        await runner.RunAsync("octocat", "hello-world", 42);

        gitHubClient.Verify(
            c => c.PostReviewAsync(
                "octocat",
                "hello-world",
                42,
                "sha123",
                It.Is<IReadOnlyList<ReviewSuggestion>>(s => s.Count == 1 && s[0].Line == 2)),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_NoCommentableFindings_NeverCallsTheReviewModel()
    {
        var changedFile = new ChangedFile("Foo.cs", Patch, "raw content");

        var gitHubClient = new Mock<IGitHubReviewClient>();
        gitHubClient
            .Setup(c => c.GetChangedFilesAsync("octocat", "hello-world", 42))
            .ReturnsAsync(new List<ChangedFile> { changedFile });

        var analyzer = new Mock<IAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync("Foo.cs", "raw content"))
            .ReturnsAsync(new List<CodeFinding> { new("Foo.cs", 99, "SA9999", "Warning", "Out-of-diff finding") });

        var reviewModel = new Mock<IReviewModel>();

        var runner = new PipelineRunner(new UnifiedDiffParser(), analyzer.Object, gitHubClient.Object, reviewModel.Object);

        await runner.RunAsync("octocat", "hello-world", 42);

        reviewModel.Verify(
            m => m.GetSuggestionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<CodeFinding>>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ManyFiles_NeverExceedsMaxConcurrentFileReviews()
    {
        const int maxConcurrency = 2;
        var changedFiles = Enumerable.Range(1, 6).Select(i => new ChangedFile($"File{i}.cs", Patch, "raw content")).ToList();

        var gitHubClient = new Mock<IGitHubReviewClient>();
        gitHubClient
            .Setup(c => c.GetChangedFilesAsync("octocat", "hello-world", 42))
            .ReturnsAsync(changedFiles);

        var concurrentCalls = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        var analyzer = new Mock<IAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () =>
            {
                lock (gate)
                {
                    concurrentCalls++;
                    maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCalls);
                }

                await Task.Delay(20);

                lock (gate)
                {
                    concurrentCalls--;
                }

                return (IReadOnlyList<CodeFinding>)Array.Empty<CodeFinding>();
            });

        var reviewModel = new Mock<IReviewModel>();

        var runner = new PipelineRunner(new UnifiedDiffParser(), analyzer.Object, gitHubClient.Object, reviewModel.Object, maxConcurrency);

        await runner.RunAsync("octocat", "hello-world", 42);

        Assert.True(maxObservedConcurrency <= maxConcurrency, $"Expected at most {maxConcurrency} concurrent reviews, observed {maxObservedConcurrency}.");
    }

    [Fact]
    public async Task RunAsync_NoSuggestions_DoesNotPostAReview()
    {
        var gitHubClient = new Mock<IGitHubReviewClient>();
        gitHubClient
            .Setup(c => c.GetChangedFilesAsync("octocat", "hello-world", 7))
            .ReturnsAsync(new List<ChangedFile>());

        var analyzer = new Mock<IAnalyzer>();
        var reviewModel = new Mock<IReviewModel>();

        var runner = new PipelineRunner(new UnifiedDiffParser(), analyzer.Object, gitHubClient.Object, reviewModel.Object);

        await runner.RunAsync("octocat", "hello-world", 7);

        gitHubClient.Verify(
            c => c.PostReviewAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ReviewSuggestion>>()),
            Times.Never);
        gitHubClient.Verify(c => c.GetPullRequestHeadShaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}

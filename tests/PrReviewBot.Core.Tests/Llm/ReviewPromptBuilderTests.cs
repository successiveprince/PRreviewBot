using PrReviewBot.Core.Analysis;
using PrReviewBot.Core.Llm;

namespace PrReviewBot.Core.Tests.Llm;

public class ReviewPromptBuilderTests
{
    private readonly ReviewPromptBuilder _builder = new();

    [Fact]
    public void SystemPrompt_InstructsJsonOnlyOutputMatchingReviewSuggestionShape()
    {
        Assert.Contains("JSON array", ReviewPromptBuilder.SystemPrompt);
        Assert.Contains("filePath", ReviewPromptBuilder.SystemPrompt);
        Assert.Contains("line", ReviewPromptBuilder.SystemPrompt);
        Assert.Contains("severity", ReviewPromptBuilder.SystemPrompt);
        Assert.Contains("message", ReviewPromptBuilder.SystemPrompt);
        Assert.Contains("markdown code fences", ReviewPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void BuildUserPrompt_IncludesFilePathDiffHunkAndFindings()
    {
        var findings = new List<CodeFinding> { new("Foo.cs", 2, "SA1234", "Warning", "Do X") };

        var userPrompt = _builder.BuildUserPrompt("Foo.cs", "@@ -1 +1 @@\n+field", findings);

        Assert.Contains("Foo.cs", userPrompt);
        Assert.Contains("@@ -1 +1 @@\n+field", userPrompt);
        Assert.Contains("SA1234", userPrompt);
        Assert.Contains("Do X", userPrompt);
    }

    [Fact]
    public void BuildUserPrompt_NoFindings_StillIncludesFilePathAndDiffHunk()
    {
        var userPrompt = _builder.BuildUserPrompt("Foo.cs", "@@ -1 +1 @@\n+field", []);

        Assert.Contains("Foo.cs", userPrompt);
        Assert.Contains("@@ -1 +1 @@\n+field", userPrompt);
    }
}

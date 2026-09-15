using Microsoft.CodeAnalysis;
using PrReviewBot.Core.Analysis;

namespace PrReviewBot.Core.Tests.Analysis;

public class RoslynAnalyzerTests
{
    private const string FixtureWithNamingViolation = """
        namespace Sample
        {
            public class SampleClass
            {
                private int my_count = 0;
            }
        }
        """;

    [Fact]
    public async Task AnalyzeAsync_DetectsStyleCopNamingViolation()
    {
        var analyzer = new RoslynAnalyzer();

        var findings = await analyzer.AnalyzeAsync("SampleClass.cs", FixtureWithNamingViolation);

        var namingFinding = Assert.Single(findings, f => f.RuleId == "SA1310");
        Assert.Equal(5, namingFinding.Line);
        Assert.Equal("SampleClass.cs", namingFinding.FilePath);
    }

    [Fact]
    public async Task AnalyzeAsync_MinimumSeverityAboveFindingSeverity_FiltersItOut()
    {
        // SA1310 is reported at Warning severity, so raising the bar to Error should hide it.
        var analyzer = new RoslynAnalyzer(minimumSeverity: DiagnosticSeverity.Error);

        var findings = await analyzer.AnalyzeAsync("SampleClass.cs", FixtureWithNamingViolation);

        Assert.DoesNotContain(findings, f => f.RuleId == "SA1310");
    }

    [Fact]
    public async Task AnalyzeAsync_FileReferencesUnresolvableProjectType_DoesNotLeakCompilerDiagnostics()
    {
        // Analyzing a single file in isolation (no other project files/references) means any
        // reference to another type in the same project can't resolve - that must not surface
        // as a CS-prefixed compiler finding, only genuine StyleCop (SA-prefixed) findings.
        const string fixtureReferencingUnknownType = """
            namespace Sample
            {
                public class SampleClass
                {
                    private readonly SomeOtherProjectType _dependency;
                }
            }
            """;

        var analyzer = new RoslynAnalyzer();

        var findings = await analyzer.AnalyzeAsync("SampleClass.cs", fixtureReferencingUnknownType);

        Assert.DoesNotContain(findings, f => f.RuleId.StartsWith("CS", StringComparison.Ordinal));
    }
}

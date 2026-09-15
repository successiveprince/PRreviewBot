namespace PrReviewBot.Core.Analysis;

using System.Collections.Immutable;
using System.Reflection;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Runs Roslyn + StyleCop.Analyzers against a single in-memory source file, without needing
/// an MSBuild project or workspace.
/// </summary>
public sealed class RoslynAnalyzer : IAnalyzer
{
    private static readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> StyleCopAnalyzers = new(LoadStyleCopAnalyzers);

    private readonly DiagnosticSeverity minimumSeverity;

    /// <summary>Initializes a new instance of the <see cref="RoslynAnalyzer"/> class.</summary>
    /// <param name="minimumSeverity">The minimum diagnostic severity to include in results.</param>
    public RoslynAnalyzer(DiagnosticSeverity minimumSeverity = DiagnosticSeverity.Info)
    {
        this.minimumSeverity = minimumSeverity;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodeFinding>> AnalyzeAsync(string filePath, string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);

        var compilation = CSharpCompilation.Create(
            assemblyName: "PrReviewBot.Analysis",
            syntaxTrees: new[] { syntaxTree },
            references: Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = StyleCopAnalyzers.Value;
        if (analyzers.IsEmpty)
        {
            return Array.Empty<CodeFinding>();
        }

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        // GetAllDiagnosticsAsync() would also include the compilation's own compiler diagnostics
        // (e.g. CS0246 for any type this single file can't resolve, since it's compiled in
        // isolation without the rest of the project) - GetAnalyzerDiagnosticsAsync() returns only
        // what StyleCop.Analyzers itself reported.
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);

        var findings = new List<CodeFinding>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity < this.minimumSeverity)
            {
                continue;
            }

            var lineSpan = diagnostic.Location.GetLineSpan();
            findings.Add(new CodeFinding(
                filePath,
                lineSpan.StartLinePosition.Line + 1,
                diagnostic.Id,
                diagnostic.Severity.ToString(),
                diagnostic.GetMessage()));
        }

        return findings;
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadStyleCopAnalyzers()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "StyleCop.Analyzers.dll");
        if (!File.Exists(assemblyPath))
        {
            return ImmutableArray<DiagnosticAnalyzer>.Empty;
        }

        var assembly = Assembly.LoadFrom(assemblyPath);
        var types = GetLoadableTypes(assembly);

        var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
            {
                builder.Add(analyzer);
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}

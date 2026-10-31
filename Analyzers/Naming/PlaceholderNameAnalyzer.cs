using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

/// <summary>
/// Fixes name the symbols they extract with <c>TODO_</c> placeholders because only
/// a person or agent knows the domain meaning. The build fails until each is renamed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlaceholderNameAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PlaceholderName,
        "Rename extracted placeholders to domain names",
        "Rename this extracted {0} to a domain name",
        "Naming", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);
        var declared = root.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken) && PlaceholderNames.IsPlaceholder(token.ValueText));
        foreach (var token in declared)
        {
            var symbol = token.Parent is { } parent ? context.SemanticModel.GetDeclaredSymbol(parent, context.CancellationToken) : null;
            if (symbol?.Name == token.ValueText)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation(), Kind(token.ValueText)));
            }
        }
    }

    // The placeholder names its extracted role; any other TODO_ name is a plain symbol.
    private static string Kind(string name) => Enum.GetValues(typeof(Placeholder)).Cast<Placeholder>()
        .Where(placeholder => name == PlaceholderNames.Of(placeholder)
            || name.StartsWith(PlaceholderNames.Of(placeholder) + "_", StringComparison.Ordinal))
        .Select(placeholder => placeholder.ToString().ToLowerInvariant())
        .DefaultIfEmpty("symbol")
        .First();
}

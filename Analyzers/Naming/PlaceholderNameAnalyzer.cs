using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

/// <summary>
/// Fixes name the symbols they extract with <c>TODO_</c> placeholders because only
/// a person or agent knows the domain meaning. Each stays reported until it is renamed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlaceholderNameAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PlaceholderName,
        "Rename extracted placeholders to domain names",
        "Rename this extracted {0} to a domain name",
        "Naming", DiagnosticSeverity.Warning, isEnabledByDefault: true);

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
            if (IsDeclaration(token, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation(), Kind(token.ValueText)));
            }
        }
    }

    // Using aliases and namespace names carry their identifier in a name node rather than
    // in the declaration itself, so they are resolved explicitly.
    private static bool IsDeclaration(SyntaxToken token, SemanticModel model, CancellationToken cancellation)
    {
        switch (token.Parent)
        {
            case IdentifierNameSyntax { Parent: NameEqualsSyntax { Parent: UsingDirectiveSyntax } }:
                return true;
            case IdentifierNameSyntax name when IsNamespaceName(name):
                return true;
            case { } parent:
                return model.GetDeclaredSymbol(parent, cancellation)?.Name == token.ValueText;
            default:
                return false;
        }
    }

    private static bool IsNamespaceName(IdentifierNameSyntax name)
    {
        SyntaxNode part = name;
        while (part.Parent is QualifiedNameSyntax)
        {
            part = part.Parent;
        }

        return part.Parent is BaseNamespaceDeclarationSyntax declaration && declaration.Name == part;
    }

    // The placeholder names its extracted role; any other TODO_ name is a plain symbol.
    private static string Kind(string name) => Enum.GetValues(typeof(Placeholder)).Cast<Placeholder>()
        .Where(placeholder => name == PlaceholderNames.Of(placeholder)
            || name.StartsWith(PlaceholderNames.Of(placeholder) + "_", StringComparison.Ordinal))
        .Select(placeholder => placeholder.ToString().ToLowerInvariant())
        .DefaultIfEmpty("symbol")
        .First();
}

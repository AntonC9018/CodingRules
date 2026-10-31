using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

/// <summary>
/// <c>Encoding.UTF8.GetBytes("literal")</c> encodes a constant at run time.
/// From C# 11 the compiler encodes a <c>"literal"u8</c> once into the assembly.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Utf8LiteralAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.Utf8StringLiteral,
        "Use a UTF-8 string literal",
        "Use a UTF-8 string literal instead of encoding a constant string at run time",
        "Performance", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1") is null) return;
            start.RegisterSyntaxNodeAction(node =>
            {
                var call = (InvocationExpressionSyntax)node.Node;
                if (Utf8LiteralFacts.EncodedLiteral(call, node.SemanticModel) is not null)
                {
                    node.ReportDiagnostic(Diagnostic.Create(Rule, call.GetLocation()));
                }
            }, SyntaxKind.InvocationExpression);
        });
    }
}

internal static class Utf8LiteralFacts
{
    /// <summary>The string literal <paramref name="call"/> encodes with <c>Encoding.UTF8.GetBytes</c>.</summary>
    public static LiteralExpressionSyntax? EncodedLiteral(InvocationExpressionSyntax call, SemanticModel model)
    {
        if (call.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp11 }
            || call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "GetBytes", Expression: var receiver }
            || call.ArgumentList.Arguments is not [{ NameColon: null, RefKindKeyword.RawKind: 0 } argument]
            || Parentheses.Unwrap(argument.Expression) is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression)
            || literal.Token.Value is not string text
            || !IsWellFormed(text)
            || ExpressionTrees.Contains(call, model))
        {
            return null;
        }

        var encoding = model.Compilation.GetTypeByMetadataName("System.Text.Encoding");
        return model.GetSymbolInfo(call).Symbol is IMethodSymbol { IsStatic: false, Parameters: [{ Type.SpecialType: SpecialType.System_String }] } method
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, encoding)
            && model.GetSymbolInfo(receiver).Symbol is IPropertySymbol { IsStatic: true, Name: "UTF8" } property
            && SymbolEqualityComparer.Default.Equals(property.ContainingType, encoding)
                ? literal
                : null;
    }

    // A u8 literal rejects unpaired surrogates, which GetBytes would replace.
    private static bool IsWellFormed(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }
}

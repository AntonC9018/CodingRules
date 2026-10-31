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
                if (Utf8LiteralFacts.ReportedLiteral(call, node.SemanticModel) is not null)
                {
                    node.ReportDiagnostic(Diagnostic.Create(Rule, call.GetLocation()));
                }
            }, SyntaxKind.InvocationExpression);
        });
    }
}

internal static class Utf8LiteralFacts
{
    /// <summary>
    /// The literal of a reported call. An array that is only stored in a
    /// <c>byte[]</c> variable, field, property or element keeps its plain call:
    /// replacing it would only add a copy.
    /// </summary>
    public static LiteralExpressionSyntax? ReportedLiteral(InvocationExpressionSyntax call, SemanticModel model) =>
        EncodedLiteral(call, model) is { } literal && (AcceptsSpan(call, model) || !IsStoredArray(call, model)) ? literal : null;

    public static bool AcceptsSpan(InvocationExpressionSyntax call, SemanticModel model) =>
        model.GetTypeInfo(call).ConvertedType is INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Byte }] } type
        && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            model.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1"));

    /// <summary><paramref name="call"/> with any parentheses around it.</summary>
    public static SyntaxNode Value(InvocationExpressionSyntax call)
    {
        SyntaxNode value = call;
        while (value.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            value = parentheses;
        }

        return value;
    }

    private static bool IsStoredArray(InvocationExpressionSyntax call, SemanticModel model)
    {
        var value = Value(call);
        var stored = value.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax or PropertyDeclarationSyntax } => true,
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == value,
            _ => false,
        };
        return stored && model.GetTypeInfo(call).ConvertedType is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };
    }

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

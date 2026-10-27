using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class OperationValueNames
{
    public static string Stem(ExpressionSyntax expression, SemanticModel model)
    {
        SyntaxNode site = expression;
        while (site.Parent is ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax) site = site.Parent;
        if (site.Parent is ArgumentSyntax argument)
        {
            if (model.GetOperation(argument) is IArgumentOperation { Parameter: { } parameter }) return Identifier(parameter.Name);
            if (argument.Parent?.Parent is { } call && model.GetSymbolInfo(call).Symbol is IMethodSymbol methodSymbol)
            {
                if (argument.NameColon is { } named) return Identifier(named.Name.Identifier.ValueText);
                var index = argument.Parent is ArgumentListSyntax arguments ? arguments.Arguments.IndexOf(argument) : -1;
                if (index >= 0 && index < methodSymbol.Parameters.Length) return Identifier(methodSymbol.Parameters[index].Name);
            }
        }
        if (expression is InvocationExpressionSyntax && model.GetSymbolInfo(expression).Symbol is IMethodSymbol method)
        {
            var name = method.Name;
            if (name.StartsWith("Get", System.StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3])) name = name.Substring(3);
            if (name.EndsWith("Async", System.StringComparison.Ordinal) && name.Length > 5) name = name.Substring(0, name.Length - 5);
            return Identifier(name);
        }
        var type = model.GetTypeInfo(expression).Type ?? model.GetTypeInfo(expression).ConvertedType;
        return type?.SpecialType switch
        {
            SpecialType.System_String => "text",
            SpecialType.System_Boolean => "condition",
            _ => Identifier(type?.Name ?? "value"),
        };
    }

    public static string Helper(ExpressionSyntax expression, SemanticModel model)
    {
        var stem = Stem(expression, model);
        return "Calculate" + char.ToUpperInvariant(stem[0]) + stem.Substring(1);
    }

    private static string Identifier(string name)
    {
        if (name.Length == 0) return "value";
        name = char.ToLowerInvariant(name[0]) + name.Substring(1);
        return SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : name + "Value";
    }
}

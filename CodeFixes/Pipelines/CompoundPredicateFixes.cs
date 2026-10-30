using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class CompoundPredicateFixes
{
    public static async Task<Document?> ChangeAsync(Document document, LambdaExpressionSyntax lambda,
        SemanticModel model, CancellationToken token)
    {
        var body = CompoundPredicateRewrite.Create(lambda, model, token);
        if (body is null)
        {
            return null;
        }

        var root = lambda.SyntaxTree.GetRoot(token);
        var changed = document.WithSyntaxRoot(root.ReplaceNode(lambda,
            lambda.WithBody(body.WithAdditionalAnnotations(Formatter.Annotation))));
        changed = await GeneratedSyntaxFormatter.FormatAsync(changed, document, token).ConfigureAwait(false);
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)lambda.SyntaxTree.Options,
            lambda.SyntaxTree.FilePath, token);
        var after = model.Compilation.ReplaceSyntaxTree(lambda.SyntaxTree, tree).GetSemanticModel(tree);
        var beforeMessages = Messages(model, token);
        if (Messages(after, token).Any(pair => !beforeMessages.TryGetValue(pair.Key, out var count)
            || pair.Value > count))
        {
            return null;
        }

        // Source-order occurrences, including enclosing calls and other members,
        // must bind identically after formatting, saving and reparsing. A set of
        // method names would miss changed overloads, repeated calls and defaults.
        if (!Bindings(root, model).SequenceEqual(Bindings(tree.GetRoot(token), after)))
        {
            return null;
        }

        return document.WithText(text);
    }

    private static Dictionary<string, int> Messages(SemanticModel model, CancellationToken token) =>
        model.GetDiagnostics(cancellationToken: token)
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .GroupBy(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage())
            .ToDictionary(group => group.Key, group => group.Count());

    private static IEnumerable<string> Bindings(SyntaxNode root, SemanticModel model)
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is LambdaExpressionSyntax lambda)
            {
                yield return "lambda:" + Type(model.GetTypeInfo(lambda).ConvertedType);
            }
            else if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
                or ElementAccessExpressionSyntax)
            {
                var operation = model.GetOperation(node);
                var symbol = operation switch
                {
                    IInvocationOperation call => (ISymbol)call.TargetMethod,
                    IObjectCreationOperation creation => creation.Constructor,
                    IPropertyReferenceOperation indexer => indexer.Property,
                    _ => model.GetSymbolInfo(node).Symbol,
                };
                var arguments = operation switch
                {
                    IInvocationOperation call => call.Arguments,
                    IObjectCreationOperation creation => creation.Arguments,
                    IPropertyReferenceOperation indexer => indexer.Arguments,
                    _ => default,
                };
                yield return Symbol(symbol) + ":" + Type(model.GetTypeInfo(node).Type) + ":"
                    + (arguments.IsDefault ? "" : string.Join(";", arguments.Select(argument =>
                        argument.Parameter?.Name + ":" + Type(argument.Parameter?.Type) + ":"
                        + argument.Parameter?.RefKind + ":" + argument.ArgumentKind + ":"
                        + argument.InConversion.IsIdentity + ":" + Symbol(argument.InConversion.MethodSymbol)
                        + ":" + argument.OutConversion.IsIdentity + ":" + Symbol(argument.OutConversion.MethodSymbol)
                        + (argument.IsImplicit && argument.Value.ConstantValue.HasValue
                            ? ":constant:" + argument.Value.ConstantValue.Value : ""))));
            }
            else if (node is MemberAccessExpressionSyntax or IdentifierNameSyntax)
            {
                yield return Symbol(model.GetSymbolInfo(node).Symbol);
            }
        }
    }

    private static string Symbol(ISymbol? symbol) => symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
        + ":" + symbol?.ContainingAssembly?.Identity;

    private static string Type(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
}

using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class NamedArgumentObservers
{
    private static readonly ConditionalWeakTable<SemanticModel, Scan> Scans = new();
    private sealed class Scan
    {
        public ImmutableArray<(SyntaxNode Node, bool Line)> Defaults { get; }
        public Scan(SemanticModel model, CancellationToken token)
        {
            var defaults = ImmutableArray.CreateBuilder<(SyntaxNode, bool)>();
            foreach (var node in model.SyntaxTree.GetRoot(token).DescendantNodes().Where(node => NamedArgumentSite.IsHost(node) || node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax))
            {
                token.ThrowIfCancellationRequested();
                var operation = model.GetOperation(node, token);
                if (operation is IAttributeOperation attributeOperation) operation = attributeOperation.Operation;
                var arguments = operation switch
                {
                    IInvocationOperation call => call.Arguments,
                    IObjectCreationOperation creation => creation.Arguments,
                    IPropertyReferenceOperation indexer => indexer.Arguments,
                    _ => default,
                };
                if (arguments.IsDefault) continue;
                foreach (var argument in arguments.Where(argument => argument.IsImplicit && argument.ArgumentKind == ArgumentKind.DefaultValue))
                {
                    foreach (var attribute in argument.Parameter!.GetAttributes())
                    {
                        var type = attribute.AttributeClass;
                        if (type?.ContainingNamespace.ToDisplayString() != "System.Runtime.CompilerServices") continue;
                        if (type.Name is "CallerArgumentExpressionAttribute" or "CallerMemberNameAttribute" or "CallerLineNumberAttribute" or "CallerFilePathAttribute")
                            defaults.Add((node, type.Name == "CallerLineNumberAttribute"));
                    }
                }
            }
            Defaults = defaults.ToImmutable();
        }
    }

    public static bool Affected(SyntaxNode selected, SemanticModel model, CancellationToken token)
    {
        if (!Scans.TryGetValue(model, out var scan))
        {
            scan = new Scan(model, token);
            token.ThrowIfCancellationRequested();
            scan = Scans.GetValue(model, _ => scan);
        }
        return scan.Defaults.Any(value => selected.Span.Contains(value.Node.Span) || value.Node.Span.Contains(selected.Span)
            || value.Line && value.Node.SpanStart >= selected.SpanStart);
    }
}

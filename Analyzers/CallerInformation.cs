using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class CallerInformation
{
    public static bool HasDefaults(SyntaxNode occurrence, SemanticModel model, bool lineOnly = false)
    {
        var arguments = model.GetOperation(occurrence) switch
        {
            IInvocationOperation invocation => invocation.Arguments,
            IObjectCreationOperation creation => creation.Arguments,
            _ => default,
        };
        return !arguments.IsDefault && arguments.Any(argument => argument.IsImplicit
            && argument.Parameter?.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                && (lineOnly ? attribute.AttributeClass.Name == "CallerLineNumberAttribute"
                    : attribute.AttributeClass.Name is "CallerArgumentExpressionAttribute" or "CallerMemberNameAttribute"
                        or "CallerLineNumberAttribute" or "CallerFilePathAttribute")) == true);
    }

    public static bool IsCall(SyntaxNode node) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;

    // A selected subtree can change enclosing argument text, and generated lines
    // can move later calls anywhere in this file, including other members.
    // These are occurrence boundaries, not just the selected callable's body.
    public static bool AffectedDefaults(SyntaxNode selected, SyntaxNode insertionSite, SemanticModel model) =>
        selected.DescendantNodesAndSelf().Concat(selected.Ancestors()).Where(IsCall).Any(call => HasDefaults(call, model))
        || selected.SyntaxTree.GetRoot().DescendantNodes().Where(IsCall)
            .Any(call => call.SpanStart >= insertionSite.SpanStart && HasDefaults(call, model, lineOnly: true));
}

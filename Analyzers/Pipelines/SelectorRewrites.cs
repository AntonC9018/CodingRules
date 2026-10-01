using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class SelectorRewrites
{
    public static SyntaxNode Rewrite(SelectorPlan plan)
    {
        var arguments = new List<ArgumentSyntax>();
        foreach (var argument in plan.Call.ArgumentList.Arguments)
        {
            var parameter = plan.Model.GetOperation(argument) as Microsoft.CodeAnalysis.Operations.IArgumentOperation;
            var name = parameter!.Parameter!.Name;
            var changed = argument;
            // Name every later supplied argument as well: insertion must remain
            // legal for old language versions and reordered named arguments.
            if (name is "keySelector" or "elementSelector" || plan.IdentityPair is not null)
                changed = argument.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(name)));
            arguments.Add(changed);
        }
        if (plan.IdentityPair is not null)
        {
            var names = new EvaluationNames(plan.Call.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax
                or TypeDeclarationSyntax) ?? plan.Call, plan.Model);
            var name = names.Fresh("element");
            var identity = SyntaxFactory.SimpleLambdaExpression(SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)), SyntaxFactory.IdentifierName(name));
            if (((CSharpParseOptions)plan.Call.SyntaxTree.Options).LanguageVersion >= LanguageVersion.CSharp9)
                identity = identity.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
            // Append rather than reorder source expressions. The pure identity
            // delegate has no capture or observable initialization.
            arguments.Add(SyntaxFactory.Argument(identity).WithNameColon(SyntaxFactory.NameColon("elementSelector")));
        }
        var changedCall = plan.Call.WithArgumentList(plan.Call.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)))
            .WithAdditionalAnnotations(ProjectionRewrites.Selected, ProjectionRewrites.Generated);
        if (plan.IdentityPair is not null)
        {
            var generic = SyntaxFactory.GenericName(SyntaxFactory.Identifier(plan.IdentityPair.Name), SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SeparatedList(plan.IdentityPair.TypeArguments.Select(EvaluationSyntax.Type))));
            changedCall = changedCall.WithExpression(changedCall.Expression switch
            {
                MemberAccessExpressionSyntax member => member.WithName(generic),
                SimpleNameSyntax => generic,
                _ => changedCall.Expression,
            });
        }
        return plan.Call.SyntaxTree.GetRoot().ReplaceNode(plan.Call, changedCall);
    }
}

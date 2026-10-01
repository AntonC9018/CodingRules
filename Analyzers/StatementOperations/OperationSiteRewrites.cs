using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class OperationSiteRewrites
{
    internal static readonly SyntaxAnnotation Generated = new("StatementOperationGenerated");
    internal static readonly SyntaxAnnotation Selected = new("StatementOperationSelected");

    public static SyntaxNode Rewrite(OperationPlan plan, bool extract)
    {
        var expression = extract ? plan.ExtractionRoot : plan.ExpansionRoot!;
        var evaluator = new OperationEvaluator(plan, expression);
        var root = expression.SyntaxTree.GetRoot();
        if (!extract) return Expand(plan, evaluator, root, expression);
        var name = evaluator.Names.Fresh("CalculateValue");
        var type = plan.Model.GetTypeInfo(expression).Type ?? plan.Model.GetTypeInfo(expression).ConvertedType!;
        var body = evaluator.ReturnBody(expression);
        var parameters = plan.BridgeVariables.Select(symbol => SyntaxFactory.Parameter(SyntaxFactory.Identifier(symbol.Name))
            .WithType(EvaluationSyntax.Type(OperationPlan.SymbolType(symbol)!)));
        var arguments = plan.BridgeVariables.Select(symbol => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(symbol.Name)));
        var call = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))).WithTriviaFrom(expression).WithAdditionalAnnotations(Selected);
        var helper = SyntaxFactory.LocalFunctionStatement(EvaluationSyntax.Type(type), name)
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters))).WithBody(body);
        if (plan.MemberInitializer is not null)
        {
            var declaration = expression.Ancestors().OfType<TypeDeclarationSyntax>().First();
            var method = SyntaxFactory.MethodDeclaration(helper.ReturnType, name).WithParameterList(helper.ParameterList).WithBody(body)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
            return root.ReplaceNode(declaration, declaration.ReplaceNode(expression, call).AddMembers(method).WithAdditionalAnnotations(Generated));
        }
        if (plan.ExpressionBody is not null)
        {
            var enclosing = plan.ExpressionBody is ArrowExpressionClauseSyntax arrow ? arrow.Expression
                : (ExpressionSyntax)((LambdaExpressionSyntax)plan.ExpressionBody).Body;
            var value = enclosing.ReplaceNode(expression, call);
            var completion = plan.Model.GetTypeInfo(enclosing).Type?.SpecialType == SpecialType.System_Void ? (StatementSyntax)SyntaxFactory.ExpressionStatement(value)
                : SyntaxFactory.ReturnStatement(value);
            return ConditionSiteRewrites.ReplaceExpressionBody(root, plan.ExpressionBody,
                SyntaxFactory.Block(helper, completion).WithAdditionalAnnotations(Generated));
        }
        var statement = plan.Statement!;
        var changed = statement.ReplaceNode(expression, call);
        if (statement is ForStatementSyntax { Declaration: not null } loop && plan.BridgeVariables.Count != 0)
        {
            return root.ReplaceNode(statement, SyntaxFactory.Block(SyntaxFactory.LocalDeclarationStatement(loop.Declaration), helper,
                ((ForStatementSyntax)changed).WithDeclaration(null)).WithTriviaFrom(statement).WithAdditionalAnnotations(Generated));
        }
        return ConditionSiteRewrites.ReplaceStatement(root, statement, new List<StatementSyntax> { helper, changed });
    }

    private static SyntaxNode Expand(OperationPlan plan, OperationEvaluator evaluator, SyntaxNode root, ExpressionSyntax expression)
    {
        if (plan.ExpressionBody is not null)
            return ConditionSiteRewrites.ReplaceExpressionBody(root, plan.ExpressionBody, evaluator.ReturnBody(expression).WithAdditionalAnnotations(Generated));
        if (plan.Statement is ReturnStatementSyntax)
            return ConditionSiteRewrites.ReplaceStatement(root, plan.Statement, evaluator.ReturnBody(expression).Statements.ToList());
        var statements = new List<StatementSyntax>();
        var value = evaluator.Lower(expression, statements);
        var changed = plan.Statement!.ReplaceNode(expression, value.WithTriviaFrom(expression).WithAdditionalAnnotations(Selected));
        statements.Add(changed);
        return ConditionSiteRewrites.ReplaceStatement(root, plan.Statement!, statements);
    }
}

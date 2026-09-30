using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>Places the shared evaluator at each original evaluation site.</summary>
internal static class ConditionSiteRewrites
{
    internal static readonly SyntaxAnnotation Generated = new("InlineConditionGenerated");

    public static bool SupportsExpansion(ConditionRewritePlan plan)
    {
        var expression = plan.Expression;
        if (expression.Parent is CatchFilterClauseSyntax or WhenClauseSyntax)
        {
            return false;
        }

        if (plan.NeedsFlowPreservingIf)
        {
            return true;
        }

        if (expression.Parent is ArrowExpressionClauseSyntax or LambdaExpressionSyntax)
        {
            return true;
        }

        var statement = plan.Statement;
        if (statement is null)
        {
            return false;
        }

        if (expression.Parent is ArgumentSyntax argument)
        {
            var call = argument.Parent?.Parent;
            if (call is not InvocationExpressionSyntax and not ObjectCreationExpressionSyntax
                || call.Parent is not ExpressionStatementSyntax and not EqualsValueClauseSyntax)
            {
                return false;
            }

            var operation = plan.Model.GetOperation(call);
            var arguments = operation switch
            {
                IInvocationOperation invocation => invocation.Arguments,
                IObjectCreationOperation creation => creation.Arguments,
                _ => default,
            };
            if (arguments.IsDefault || arguments.Any(item => item.Parameter?.RefKind != RefKind.None
                || item.ArgumentKind == ArgumentKind.ParamArray || item.Parameter?.Type.IsRefLikeType == true))
            {
                return false;
            }

            return operation is not IInvocationOperation { Instance.Type.IsValueType: true };
        }

        if (expression.Parent is AssignmentExpressionSyntax assignment)
        {
            return assignment.Parent is ExpressionStatementSyntax && plan.Model.GetSymbolInfo(assignment.Left).Symbol
                is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None };
        }

        if (expression.Parent is EqualsValueClauseSyntax)
        {
            return statement is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 };
        }

        if (expression.Parent is ConditionalExpressionSyntax conditional)
        {
            return conditional.Parent is EqualsValueClauseSyntax or ReturnStatementSyntax
                || conditional.Parent is AssignmentExpressionSyntax { Parent: ExpressionStatementSyntax };
        }

        return expression.Parent is IfStatementSyntax or ReturnStatementSyntax or WhileStatementSyntax
            or ForStatementSyntax or DoStatementSyntax;
    }

    public static SyntaxNode Rewrite(ConditionRewritePlan plan, bool extract)
    {
        var evaluator = new ConditionEvaluator(plan.Model, plan.DeclarationSpace);
        return extract ? Extract(plan, evaluator) : Expand(plan, evaluator);
    }

    private static SyntaxNode Extract(ConditionRewritePlan plan, ConditionEvaluator evaluator)
    {
        var expression = plan.Expression;
        var root = expression.SyntaxTree.GetRoot();
        var name = evaluator.FreshName("CheckCondition");
        var body = evaluator.ReturnBody(expression);
        var parameters = plan.BridgeVariables.Select(symbol => SyntaxFactory.Parameter(SyntaxFactory.Identifier(symbol.Name))
            .WithType(ConditionEvaluator.TypeSyntax(symbol.Type)));
        var arguments = plan.BridgeVariables.Select(symbol => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(symbol.Name)));
        var call = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))).WithTriviaFrom(expression);
        var helper = SyntaxFactory.LocalFunctionStatement(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), name)
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters))).WithBody(body);

        if (IsMemberInitializer(expression))
        {
            var type = expression.Ancestors().OfType<TypeDeclarationSyntax>().First();
            var memberHelper = SyntaxFactory.MethodDeclaration(helper.ReturnType, name)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(helper.ParameterList).WithBody(body);
            var changedType = type.ReplaceNode(expression, call).AddMembers(memberHelper).WithAdditionalAnnotations(Generated);
            return root.ReplaceNode(type, changedType);
        }

        var expressionBody = FindExpressionBody(expression);
        if (expressionBody is not null)
        {
            var originalBody = expressionBody is ArrowExpressionClauseSyntax arrow ? arrow.Expression : (ExpressionSyntax)((LambdaExpressionSyntax)expressionBody).Body;
            var result = originalBody.ReplaceNode(expression, call);
            var returnsVoid = expressionBody is LambdaExpressionSyntax lambda
                ? plan.Model.GetSymbolInfo(lambda).Symbol is IMethodSymbol { ReturnsVoid: true }
                : plan.Model.GetDeclaredSymbol(expressionBody.Parent!) is IMethodSymbol { ReturnsVoid: true };
            StatementSyntax completion = returnsVoid ? SyntaxFactory.ExpressionStatement(result) : SyntaxFactory.ReturnStatement(result);
            var block = SyntaxFactory.Block(helper, completion).WithAdditionalAnnotations(Generated);
            return ReplaceExpressionBody(root, expressionBody, block);
        }

        var statement = plan.Statement!;
        var changed = statement.ReplaceNode(expression, call);
        if (statement is ForStatementSyntax { Declaration: not null } forLoop && expression.Parent == statement)
        {
            var declaration = SyntaxFactory.LocalDeclarationStatement(forLoop.Declaration);
            var rewrittenLoop = ((ForStatementSyntax)changed).WithDeclaration(null);
            return root.ReplaceNode(statement, SyntaxFactory.Block(declaration, helper, rewrittenLoop)
                .WithTriviaFrom(statement).WithAdditionalAnnotations(Generated));
        }

        return ReplaceStatement(root, statement, new List<StatementSyntax> { helper, changed });
    }

    private static SyntaxNode Expand(ConditionRewritePlan plan, ConditionEvaluator evaluator)
    {
        var root = plan.Expression.SyntaxTree.GetRoot();
        var expressionBody = FindExpressionBody(plan.Expression);
        if (expressionBody is not null && (expressionBody is ArrowExpressionClauseSyntax arrow && arrow.Expression == plan.Expression
            || expressionBody is LambdaExpressionSyntax { Body: ExpressionSyntax body } && body == plan.Expression))
        {
            return ReplaceExpressionBody(root, expressionBody, evaluator.ReturnBody(plan.Expression).WithAdditionalAnnotations(Generated));
        }

        if (plan.Statement is ReturnStatementSyntax statement && statement.Expression == plan.Expression)
        {
            return ReplaceStatement(root, statement, evaluator.ReturnBody(plan.Expression).Statements.ToList());
        }

        if (plan.NeedsFlowPreservingIf)
        {
            return ExpandFlowIf(plan);
        }

        var result = evaluator.FreshName("conditionResult");
        var prefixes = new List<StatementSyntax>();
        var changedStatement = PrepareEarlierArguments(plan, evaluator, prefixes);
        prefixes.Add(ConditionEvaluator.DeclareBool(result));
        prefixes.AddRange(evaluator.Evaluate(plan.Expression, result));
        var replacementExpression = SyntaxFactory.IdentifierName(result).WithTriviaFrom(plan.Expression);
        changedStatement = changedStatement.ReplaceNode(changedStatement.DescendantNodesAndSelf().OfType<ExpressionSyntax>()
            .First(node => node.HasAnnotation(Selected)), replacementExpression);

        if (plan.Statement is WhileStatementSyntax whileLoop && plan.Expression.Parent == whileLoop)
        {
            prefixes.Add(FalseBreak(result));
            prefixes.Add(whileLoop.Statement);
            var loop = whileLoop.WithCondition(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))
                .WithStatement(SyntaxFactory.Block(prefixes)).WithAdditionalAnnotations(Generated);
            return root.ReplaceNode(whileLoop, loop);
        }

        if (plan.Statement is ForStatementSyntax forLoop && plan.Expression.Parent == forLoop)
        {
            prefixes.Add(FalseBreak(result));
            prefixes.Add(forLoop.Statement);
            return root.ReplaceNode(forLoop, forLoop.WithCondition(null).WithStatement(SyntaxFactory.Block(prefixes))
                .WithAdditionalAnnotations(Generated));
        }

        if (plan.Statement is DoStatementSyntax doLoop && plan.Expression.Parent == doLoop)
        {
            prefixes.Insert(0, doLoop.Statement);
            prefixes.Add(FalseBreak(result));
            var loop = SyntaxFactory.WhileStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression), SyntaxFactory.Block(prefixes));
            return root.ReplaceNode(doLoop, loop.WithTriviaFrom(doLoop).WithAdditionalAnnotations(Generated));
        }

        prefixes.Add(changedStatement);
        return ReplaceStatement(root, plan.Statement!, prefixes);
    }

    private static readonly SyntaxAnnotation Selected = new("InlineConditionSelected");

    private static StatementSyntax PrepareEarlierArguments(ConditionRewritePlan plan, ConditionEvaluator evaluator, List<StatementSyntax> prefixes)
    {
        var statement = plan.Statement!.ReplaceNode(plan.Expression, plan.Expression.WithAdditionalAnnotations(Selected));
        if (plan.Expression.Parent is not ArgumentSyntax selectedArgument)
        {
            return statement;
        }

        var call = selectedArgument.Parent!.Parent!;
        var replacements = new Dictionary<SyntaxNode, ExpressionSyntax>();
        if (call is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation
            && plan.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { IsStatic: false })
        {
            replacements.Add(access.Expression, evaluator.Save(access.Expression, access.Expression, prefixes));
        }

        foreach (var argument in ((BaseArgumentListSyntax)selectedArgument.Parent).Arguments)
        {
            if (argument == selectedArgument)
            {
                break;
            }

            replacements.Add(argument.Expression, evaluator.Save(argument.Expression, argument.Expression, prefixes,
                plan.Model.GetTypeInfo(argument.Expression).ConvertedType));
        }

        // Match original spans because annotating the selected node rebuilt ancestors.
        var nodes = statement.DescendantNodes().Where(node => replacements.Keys.Any(original => original.Span == node.Span && original.RawKind == node.RawKind)).ToList();
        return statement.ReplaceNodes(nodes, (node, _) => replacements.First(pair => pair.Key.Span == node.Span && pair.Key.RawKind == node.RawKind).Value);
    }

    private static SyntaxNode ExpandFlowIf(ConditionRewritePlan plan)
    {
        var statement = (IfStatementSyntax)plan.Statement!;
        var condition = ConditionFacts.Unwrap(statement.Condition);
        var conjunction = ((BinaryExpressionSyntax)condition).IsKind(SyntaxKind.LogicalAndExpression);
        var leaves = new List<ExpressionSyntax>();
        CollectHomogeneous(condition, conjunction ? SyntaxKind.LogicalAndExpression : SyntaxKind.LogicalOrExpression, leaves);
        if (conjunction)
        {
            StatementSyntax body = statement.Statement;
            for (var index = leaves.Count - 1; index >= 0; index--)
            {
                body = SyntaxFactory.IfStatement(leaves[index], index == leaves.Count - 1 ? body : SyntaxFactory.Block(body));
            }

            return statement.SyntaxTree.GetRoot().ReplaceNode(statement, body.WithTriviaFrom(statement).WithAdditionalAnnotations(Generated));
        }

        var guards = leaves.Select(leaf => (StatementSyntax)SyntaxFactory.IfStatement(leaf, statement.Statement.WithoutTrivia())).ToList();
        return ReplaceStatement(statement.SyntaxTree.GetRoot(), statement, guards);
    }

    private static void CollectHomogeneous(ExpressionSyntax expression, SyntaxKind kind, List<ExpressionSyntax> leaves)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(kind))
        {
            CollectHomogeneous(binary.Left, kind, leaves);
            CollectHomogeneous(binary.Right, kind, leaves);
            return;
        }

        leaves.Add(expression);
    }

    private static IfStatementSyntax FalseBreak(string result) => SyntaxFactory.IfStatement(
        ConditionEvaluator.Negate(SyntaxFactory.IdentifierName(result)), SyntaxFactory.Block(SyntaxFactory.BreakStatement()));

    private static bool IsMemberInitializer(ExpressionSyntax expression) => expression.Parent is EqualsValueClauseSyntax
        && expression.Ancestors().TakeWhile(node => node is not StatementSyntax).Any(node => node is FieldDeclarationSyntax or PropertyDeclarationSyntax);

    private static SyntaxNode? FindExpressionBody(ExpressionSyntax expression) => expression.Ancestors()
        .TakeWhile(node => node is not StatementSyntax and not MemberDeclarationSyntax)
        .FirstOrDefault(node => node is ArrowExpressionClauseSyntax or LambdaExpressionSyntax { Body: ExpressionSyntax });

    private static SyntaxNode ReplaceExpressionBody(SyntaxNode root, SyntaxNode expressionBody, BlockSyntax body)
    {
        if (expressionBody is LambdaExpressionSyntax lambda)
        {
            return root.ReplaceNode(lambda, lambda.WithBody(body));
        }

        var arrow = (ArrowExpressionClauseSyntax)expressionBody;
        SyntaxNode replacement = arrow.Parent switch
        {
            MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            OperatorDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            ConversionOperatorDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            LocalFunctionStatementSyntax function => function.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            PropertyDeclarationSyntax property => property.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
            IndexerDeclarationSyntax indexer => indexer.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
            _ => arrow.Parent!,
        };
        return root.ReplaceNode(arrow.Parent!, replacement.WithAdditionalAnnotations(Generated));
    }

    private static SyntaxNode ReplaceStatement(SyntaxNode root, StatementSyntax statement, List<StatementSyntax> statements)
    {
        statements[0] = statements[0].WithLeadingTrivia(statement.GetLeadingTrivia());
        statements[statements.Count - 1] = statements[statements.Count - 1].WithTrailingTrivia(statement.GetTrailingTrivia());
        if (statement.Parent is BlockSyntax block)
        {
            var index = block.Statements.IndexOf(statement);
            var changed = block.WithStatements(block.Statements.RemoveAt(index).InsertRange(index, statements)).WithAdditionalAnnotations(Generated);
            return root.ReplaceNode(block, changed);
        }

        if (statement.Parent is GlobalStatementSyntax global)
        {
            var compilationUnit = (CompilationUnitSyntax)global.Parent!;
            var index = compilationUnit.Members.IndexOf(global);
            var members = statements.Select(item => SyntaxFactory.GlobalStatement(item));
            return root.ReplaceNode(compilationUnit, compilationUnit.WithMembers(compilationUnit.Members.RemoveAt(index)
                .InsertRange(index, members)).WithAdditionalAnnotations(Generated));
        }

        return root.ReplaceNode(statement, SyntaxFactory.Block(statements).WithAdditionalAnnotations(Generated));
    }
}

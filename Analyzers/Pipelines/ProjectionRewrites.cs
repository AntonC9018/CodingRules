using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class ProjectionRewrites
{
    internal static readonly SyntaxAnnotation Selected = new("PipelineSelected");
    internal static readonly SyntaxAnnotation Generated = new("PipelineGenerated");
    internal const string OriginalCall = "PipelineOriginalCall";
    public static SyntaxNode Rewrite(ProjectionPlan plan, bool extract)
    {
        var lambda = plan.Lambda;
        var body = lambda.Body is ExpressionSyntax expression ? LinearBody(plan, expression)
            : Annotate((BlockSyntax)lambda.Body);
        if (extract)
        {
            var names = new EvaluationNames(lambda.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax or TypeDeclarationSyntax) ?? lambda, plan.Model);
            var name = names.Fresh("ProjectValue");
            var checkedContext = lambda.Ancestors().FirstOrDefault(node => node is CheckedStatementSyntax or CheckedExpressionSyntax);
            if (lambda.Body is BlockSyntax && checkedContext is not null)
                body = SyntaxFactory.Block(SyntaxFactory.CheckedStatement(checkedContext.Kind() is SyntaxKind.CheckedExpression or SyntaxKind.CheckedStatement
                    ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement, body));
            var helper = SyntaxFactory.LocalFunctionStatement(EvaluationSyntax.Type(plan.Callable.ReturnType), name).WithBody(body)
                .WithParameterList(SyntaxFactory.ParameterList());
            var parameters = plan.Callable.Parameters.Select(parameter => SyntaxFactory.Parameter(SyntaxFactory.Identifier(
                    SyntaxFacts.GetKeywordKind(parameter.Name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(parameter.Name) != SyntaxKind.None
                        ? "@" + parameter.Name : parameter.Name))
                .WithType(EvaluationSyntax.Type(parameter.Type)));
            var typedHelper = helper.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)));
            if (lambda.Modifiers.Any(SyntaxKind.StaticKeyword)) typedHelper = typedHelper.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
            var host = ProjectionPlan.InsertionSite(lambda);
            var flow = plan.Model.AnalyzeDataFlow(lambda.Body)!;
            var unavailable = flow.ReadInside.Concat(flow.WrittenInside).Except(flow.VariablesDeclared, SymbolEqualityComparer.Default)
                .Except(plan.Callable.Parameters, SymbolEqualityComparer.Default).Any(symbol => symbol is ILocalSymbol or IParameterSymbol
                    && !plan.Model.LookupSymbols(host?.SpanStart ?? lambda.SpanStart, name: symbol.Name).Contains(symbol, SymbolEqualityComparer.Default));
            var group = SyntaxFactory.IdentifierName(name).WithTriviaFrom(lambda).WithAdditionalAnnotations(Selected);
            var root = lambda.SyntaxTree.GetRoot();
            if (!unavailable && host is StatementSyntax statement)
                return ConditionSiteRewrites.ReplaceStatement(root, statement, new System.Collections.Generic.List<StatementSyntax>
                    { typedHelper.WithAdditionalAnnotations(Generated), statement.ReplaceNode(lambda, group) });
            if (!unavailable && host is ArrowExpressionClauseSyntax or LambdaExpressionSyntax)
            {
                var hostExpression = host is ArrowExpressionClauseSyntax arrow ? arrow.Expression : (ExpressionSyntax)((LambdaExpressionSyntax)host).Body;
                var returnsVoid = host is LambdaExpressionSyntax enclosing
                    ? plan.Model.GetSymbolInfo(enclosing).Symbol is IMethodSymbol { ReturnsVoid: true }
                    : plan.Model.GetDeclaredSymbol(host.Parent!) is IMethodSymbol { ReturnsVoid: true };
                StatementSyntax completion = returnsVoid
                    ? SyntaxFactory.ExpressionStatement(hostExpression.ReplaceNode(lambda, group))
                    : SyntaxFactory.ReturnStatement(hostExpression.ReplaceNode(lambda, group));
                return ConditionSiteRewrites.ReplaceExpressionBody(root, host, SyntaxFactory.Block(typedHelper, completion).WithAdditionalAnnotations(Generated));
            }
            if (host is EqualsValueClauseSyntax && !flow.ReadInside.Concat(flow.WrittenInside).Except(flow.VariablesDeclared, SymbolEqualityComparer.Default).Any(symbol => symbol is ILocalSymbol or IParameterSymbol
                    && !plan.Callable.Parameters.Contains(symbol, SymbolEqualityComparer.Default))
                && !PipelineFacts.StageNodes(lambda.Body).OfType<ExpressionSyntax>().Any(value => plan.Model.GetOperation(value) is Microsoft.CodeAnalysis.Operations.IInstanceReferenceOperation))
            {
                var type = lambda.Ancestors().OfType<TypeDeclarationSyntax>().First();
                var method = SyntaxFactory.MethodDeclaration(typedHelper.ReturnType, name).WithParameterList(typedHelper.ParameterList).WithBody(body)
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
                return root.ReplaceNode(type, type.ReplaceNode(lambda, group).AddMembers(method.WithAdditionalAnnotations(Generated)));
            }
            // Invocation-local capture keeps iteration/filter/pattern values in scope,
            // preserves delegate construction and reads all captures on each invocation.
            body = SyntaxFactory.Block(helper, SyntaxFactory.ReturnStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name))));
        }
        else if (plan.CanExtract)
        {
            // Named intermediates may themselves require helper placement. Keep
            // that implementation invocation-local rather than creating a new
            // CR0401 stage and repeated extraction after saving.
            var name = new EvaluationNames(lambda, plan.Model).Fresh("ProjectValue");
            var helper = SyntaxFactory.LocalFunctionStatement(EvaluationSyntax.Type(plan.Callable.ReturnType), name)
                .WithParameterList(SyntaxFactory.ParameterList()).WithBody(body);
            body = SyntaxFactory.Block(helper, SyntaxFactory.ReturnStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name))));
        }
        var replacement = lambda.WithBody(body).WithAdditionalAnnotations(Selected, Generated);
        return lambda.SyntaxTree.GetRoot().ReplaceNode(lambda, replacement);
    }

    internal static BlockSyntax Annotate(BlockSyntax body) => body.ReplaceNodes(body.DescendantNodes().OfType<ExpressionSyntax>()
        .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax),
        (node, rewritten) => rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(OriginalCall, OperationEvaluator.Key(node))));

    private static BlockSyntax LinearBody(ProjectionPlan plan, ExpressionSyntax expression)
    {
        if (plan.Lowering!.CanExpand) return new OperationEvaluator(plan.Lowering, expression).ReturnBody(expression);
        var evaluator = new OperationEvaluator(plan.Lowering, expression);
        var statements = new System.Collections.Generic.List<StatementSyntax>();
        if (expression is TupleExpressionSyntax tuple)
        {
            var arguments = tuple.Arguments.Select(argument => argument.WithExpression(evaluator.Save(argument.Expression,
                evaluator.Lower(argument.Expression, statements), statements, plan.Model.GetTypeInfo(argument.Expression).ConvertedType))).ToArray();
            statements.Add(SyntaxFactory.ReturnStatement(tuple.WithArguments(SyntaxFactory.SeparatedList(arguments))));
        }
        else if (expression is BaseObjectCreationExpressionSyntax creation)
        {
            var allocation = creation switch
            {
                ObjectCreationExpressionSyntax explicitCreation => (ExpressionSyntax)explicitCreation.WithInitializer(null).WithArgumentList(explicitCreation.ArgumentList ?? SyntaxFactory.ArgumentList()),
                ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.WithInitializer(null),
                _ => creation,
            };
            var target = evaluator.Save(creation, allocation.WithAdditionalAnnotations(new SyntaxAnnotation(OperationEvaluator.BindingAnnotation, OperationEvaluator.Key(creation))), statements);
            foreach (var assignment in creation.Initializer!.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                var value = evaluator.Lower(assignment.Right, statements);
                statements.Add(SyntaxFactory.ExpressionStatement(assignment.WithLeft(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    target, (IdentifierNameSyntax)assignment.Left)).WithRight(value)));
            }
            statements.Add(SyntaxFactory.ReturnStatement(target));
        }
        var body = SyntaxFactory.Block(statements);
        var checkedContext = expression.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
        if (checkedContext is not null) body = SyntaxFactory.Block(SyntaxFactory.CheckedStatement(checkedContext.Kind() is SyntaxKind.CheckedExpression or SyntaxKind.CheckedStatement
            ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement, body));
        return body;
    }
}

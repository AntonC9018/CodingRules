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

    public static bool SupportsExtraction(ExpressionSyntax expression) =>
        ConditionHosts.FindStatement(expression) is not null || IsMemberInitializer(expression) || FindExpressionBody(expression) is not null;

    /// <summary>
    /// Statement expansion never introduces a mutable result variable. A whole
    /// returned condition becomes guard clauses with literal returns, a leading
    /// mutation is hoisted before its <c>if</c>, and a condition whose declarations
    /// or nullable state flow onward becomes separate guards.
    /// </summary>
    public static bool SupportsExpansion(ConditionRewritePlan plan) =>
        IsWholeReturnedCondition(plan) || plan.HoistedMutation is not null || plan.NeedsFlowPreservingIf;

    private static bool IsWholeReturnedCondition(ConditionRewritePlan plan)
    {
        var expression = plan.Expression;
        return expression.Parent is ArrowExpressionClauseSyntax or LambdaExpressionSyntax
            || plan.Statement is ReturnStatementSyntax statement && statement.Expression == expression;
    }

    /// <summary>
    /// The simple assignment or prefix increment evaluated first by an <c>if</c>
    /// condition, when it can run as its own statement before the <c>if</c>.
    /// </summary>
    public static ExpressionSyntax? FindHoistableMutation(ExpressionSyntax condition, StatementSyntax? statement,
        SemanticModel model, SyntaxNode declarationSpace)
    {
        if (statement is not IfStatementSyntax ifStatement || ifStatement.Condition != condition
            || statement.Parent is not (BlockSyntax or SwitchSectionSyntax or GlobalStatementSyntax))
        {
            return null;
        }

        var node = Parentheses.Unwrap(condition);
        while (!IsHoistableMutationKind(node))
        {
            // Follow the operand C# evaluates first; anything else may be skipped or reordered.
            ExpressionSyntax? first = node switch
            {
                BinaryExpressionSyntax binary => binary.Left,
                IsPatternExpressionSyntax pattern => pattern.Expression,
                PrefixUnaryExpressionSyntax unary => unary.Operand,
                CastExpressionSyntax cast => cast.Expression,
                _ => null,
            };
            if (first is null) return null;
            node = Parentheses.Unwrap(first);
        }

        return new StableValues(model, declarationSpace, condition).HoistableTarget(node) is null ? null : node;
    }

    private static bool IsHoistableMutationKind(ExpressionSyntax node) => node.Kind() is SyntaxKind.SimpleAssignmentExpression
        or SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression;

    public static SyntaxNode Rewrite(ConditionRewritePlan plan, bool extract)
    {
        var evaluator = new ConditionEvaluator(plan.Model, plan.DeclarationSpace, plan.Expression);
        return extract ? Extract(plan, evaluator) : Expand(plan, evaluator);
    }

    private static SyntaxNode Extract(ConditionRewritePlan plan, ConditionEvaluator evaluator)
    {
        var expression = plan.Expression;
        var root = expression.SyntaxTree.GetRoot();
        var enumPattern = EnumMemberPattern.Create(expression, plan.Model);
        var name = enumPattern is null ? evaluator.FreshName(Placeholder.Condition) : evaluator.FreshName(enumPattern.PredicateName);
        var body = enumPattern is null ? evaluator.ReturnBody(expression)
            : SyntaxFactory.Block(SyntaxFactory.ReturnStatement(enumPattern.Pattern));
        foreach (var ancestor in expression.Ancestors())
        {
            if (ancestor is CheckedStatementSyntax checkedStatement)
            {
                var kind = checkedStatement.IsKind(SyntaxKind.CheckedStatement) ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement;
                body = SyntaxFactory.Block(SyntaxFactory.CheckedStatement(kind, body));
                break;
            }

            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
            {
                break;
            }
        }

        body = AddElasticLineBreaks(body);
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
            var rewrittenLoop = ((ForStatementSyntax)changed).WithDeclaration(null).WithoutLeadingTrivia().WithoutTrailingTrivia();
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

        if (plan.HoistedMutation is not null)
        {
            return HoistMutation(plan, root);
        }

        return ExpandFlowIf(plan, evaluator);
    }

    // if ((c = Compare(a, b)) != 0) becomes c = Compare(a, b); if (c != 0). An
    // immediately preceding uninitialized declaration of c takes the value instead.
    private static SyntaxNode HoistMutation(ConditionRewritePlan plan, SyntaxNode root)
    {
        var statement = (IfStatementSyntax)plan.Statement!;
        var mutation = plan.HoistedMutation!;
        var variable = (IdentifierNameSyntax)(mutation is AssignmentExpressionSyntax assignment
            ? assignment.Left : ((PrefixUnaryExpressionSyntax)mutation).Operand);
        SyntaxNode replaced = mutation;
        while (replaced.Parent is ParenthesizedExpressionSyntax parentheses) replaced = parentheses;
        var read = SyntaxFactory.IdentifierName(variable.Identifier.ValueText).WithTriviaFrom(replaced);
        var changedIf = statement.ReplaceNode(replaced, read);
        changedIf = changedIf.WithCondition(changedIf.Condition.WithAdditionalAnnotations(ConditionEvaluator.Linearized));
        if (mutation is AssignmentExpressionSyntax initializer
            && PrecedingUninitializedDeclaration(statement, variable, plan.Model) is { } declaration)
        {
            var declarator = declaration.Declaration.Variables[0].WithInitializer(
                SyntaxFactory.EqualsValueClause(initializer.Right.WithoutTrivia()));
            var initialized = declaration.WithDeclaration(declaration.Declaration.WithVariables(
                SyntaxFactory.SingletonSeparatedList(declarator)));
            return root.ReplaceNodes(new SyntaxNode[] { declaration, statement },
                (original, _) => original == declaration ? initialized : changedIf);
        }

        var hoisted = SyntaxFactory.ExpressionStatement(mutation.WithoutTrivia());
        return ReplaceStatement(root, statement, new List<StatementSyntax> { hoisted, changedIf });
    }

    private static LocalDeclarationStatementSyntax? PrecedingUninitializedDeclaration(StatementSyntax statement,
        IdentifierNameSyntax variable, SemanticModel model)
    {
        var statements = statement.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default,
        };
        var index = statements.IndexOf(statement);
        if (index <= 0 || statements[index - 1] is not LocalDeclarationStatementSyntax declaration
            || declaration.Declaration.Variables.Count != 1 || declaration.Declaration.Variables[0].Initializer is not null
            || declaration.Modifiers.Count != 0 || declaration.ContainsDirectives
            || statement.GetLeadingTrivia().Any(trivia => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia
                or SyntaxKind.MultiLineCommentTrivia))
        {
            return null;
        }

        var declared = model.GetDeclaredSymbol(declaration.Declaration.Variables[0]);
        return SymbolEqualityComparer.Default.Equals(declared, model.GetSymbolInfo(variable).Symbol) ? declaration : null;
    }

    private static SyntaxNode ExpandFlowIf(ConditionRewritePlan plan, ConditionEvaluator evaluator)
    {
        var statement = (IfStatementSyntax)plan.Statement!;
        var condition = Parentheses.Unwrap(statement.Condition);
        var conjunction = ((BinaryExpressionSyntax)condition).IsKind(SyntaxKind.LogicalAndExpression);
        var leaves = new List<ExpressionSyntax>();
        ConditionFacts.CollectChain(condition, conjunction ? SyntaxKind.LogicalAndExpression : SyntaxKind.LogicalOrExpression, leaves);
        if (conjunction)
        {
            StatementSyntax body = statement.Statement;
            for (var index = leaves.Count - 1; index >= 0; index--)
            {
                var prefixes = new List<StatementSyntax>();
                var leaf = evaluator.LowerCondition(leaves[index], prefixes);
                var guard = SyntaxFactory.IfStatement(leaf, index == leaves.Count - 1 ? body : SyntaxFactory.Block(body));
                prefixes.Add(guard);
                body = prefixes.Count == 1 ? guard : SyntaxFactory.Block(prefixes);
            }

            return statement.SyntaxTree.GetRoot().ReplaceNode(statement, body.WithTriviaFrom(statement).WithAdditionalAnnotations(Generated));
        }

        var guards = new List<StatementSyntax>();
        foreach (var leaf in leaves)
        {
            var conditionLeaf = evaluator.LowerCondition(leaf, guards);
            guards.Add(SyntaxFactory.IfStatement(conditionLeaf, statement.Statement.WithoutTrivia()));
        }

        return ReplaceStatement(statement.SyntaxTree.GetRoot(), statement, guards);
    }

    internal static BlockSyntax AddElasticLineBreaks(BlockSyntax body)
    {
        var statements = body.DescendantNodes().OfType<StatementSyntax>();
        body = body.ReplaceNodes(statements, (_, changed) => changed.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));
        return body.WithOpenBraceToken(body.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));
    }

    private static bool IsMemberInitializer(ExpressionSyntax expression) => expression.Ancestors()
        .TakeWhile(node => node is not StatementSyntax and not ArrowExpressionClauseSyntax and not AnonymousFunctionExpressionSyntax)
        .OfType<EqualsValueClauseSyntax>().Any(initializer => initializer.Parent is PropertyDeclarationSyntax
            || initializer.Parent?.Parent?.Parent is FieldDeclarationSyntax);

    private static SyntaxNode? FindExpressionBody(ExpressionSyntax expression) => expression.Ancestors()
        .TakeWhile(node => node is not StatementSyntax and not MemberDeclarationSyntax)
        .FirstOrDefault(node => node is ArrowExpressionClauseSyntax or LambdaExpressionSyntax { Body: ExpressionSyntax });

    internal static SyntaxNode ReplaceExpressionBody(SyntaxNode root, SyntaxNode expressionBody, BlockSyntax body)
    {
        if (expressionBody is LambdaExpressionSyntax lambda)
        {
            return root.ReplaceNode(lambda, lambda.WithBody(body));
        }

        var arrow = (ArrowExpressionClauseSyntax)expressionBody;
        SyntaxNode replacement = arrow.Parent switch
        {
            MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            ConstructorDeclarationSyntax constructor => constructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
            DestructorDeclarationSyntax destructor => destructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
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
            _ => throw new System.InvalidOperationException("Unsupported expression body"),
        };
        return root.ReplaceNode(arrow.Parent!, replacement.WithAdditionalAnnotations(Generated));
    }

    /// <summary>The whitespace that indents the line a statement with <paramref name="leading"/> trivia starts on.</summary>
    internal static SyntaxTriviaList Indentation(SyntaxTriviaList leading) => SyntaxFactory.TriviaList(
        leading.Reverse().TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());

    /// <summary>The whitespace that indents the source line <paramref name="node"/> starts on.</summary>
    internal static SyntaxTriviaList SourceIndentation(SyntaxNode node)
    {
        var text = node.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(node.SpanStart).ToString();
        var width = line.Length - line.TrimStart(' ', '\t').Length;
        return width == 0 ? SyntaxFactory.TriviaList() : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(line.Substring(0, width)));
    }

    internal static SyntaxNode ReplaceStatement(SyntaxNode root, StatementSyntax statement, List<StatementSyntax> statements)
    {
        // Each statement starts its own line at the original column, so the
        // formatter keeps the relative indentation of continuation lines.
        var indentation = Indentation(statement.GetLeadingTrivia());
        var lineBreak = statement.GetTrailingTrivia().LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        for (var index = 1; index < statements.Count; index++)
        {
            statements[index] = statements[index].WithLeadingTrivia(indentation);
            if (lineBreak.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                statements[index - 1] = statements[index - 1].WithTrailingTrivia(lineBreak);
            }
        }

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

        if (statement.Parent is SwitchSectionSyntax section)
        {
            var index = section.Statements.IndexOf(statement);
            var changedStatements = section.Statements.RemoveAt(index).InsertRange(index, statements);
            var changed = section.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(changedStatements)))
                .WithAdditionalAnnotations(Generated);
            return root.ReplaceNode(section, changed);
        }

        return root.ReplaceNode(statement, SyntaxFactory.Block(statements).WithAdditionalAnnotations(Generated));
    }
}

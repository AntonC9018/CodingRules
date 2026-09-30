using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>Evaluates the original tree once, in order, without Boolean algebra.</summary>
internal sealed class ConditionEvaluator
{
    private readonly SemanticModel model;
    private readonly HashSet<string> names;

    public ConditionEvaluator(SemanticModel model, SyntaxNode declarationSpace)
    {
        this.model = model;
        names = new HashSet<string>(declarationSpace.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText));
        var typeDeclaration = declarationSpace.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is not null && model.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                names.Add(member.Name);
            }
        }
    }

    public string FreshName(string stem)
    {
        var name = stem;
        var suffix = 2;
        while (!names.Add(name))
        {
            name = stem + suffix++;
        }

        return name;
    }

    public List<StatementSyntax> Evaluate(ExpressionSyntax expression, string result)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            var statements = Evaluate(binary.Left, result);
            var condition = binary.IsKind(SyntaxKind.LogicalAndExpression)
                ? SyntaxFactory.IdentifierName(result) : Negate(SyntaxFactory.IdentifierName(result));
            statements.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(Evaluate(binary.Right, result))));
            return statements;
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            var statements = Evaluate(unary.Operand, result);
            statements.Add(Assign(result, Negate(SyntaxFactory.IdentifierName(result))));
            return statements;
        }

        var prefixes = new List<StatementSyntax>();
        var leaf = LowerLeaf(expression, prefixes, saveProducer: false);
        prefixes.Add(Assign(result, leaf));
        return prefixes;
    }

    public BlockSyntax ReturnBody(ExpressionSyntax expression)
    {
        var leaves = new List<ExpressionSyntax>();
        var unwrapped = ConditionFacts.Unwrap(expression);
        if (unwrapped is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
            && CollectGuards(unwrapped, binary.Kind(), leaves))
        {
            var guards = new List<StatementSyntax>();
            var isAnd = binary.IsKind(SyntaxKind.LogicalAndExpression);
            foreach (var leaf in leaves)
            {
                var condition = LowerLeaf(leaf, guards, saveProducer: false);
                if (isAnd)
                {
                    condition = Negate(condition);
                }

                guards.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(SyntaxFactory.ReturnStatement(
                    SyntaxFactory.LiteralExpression(isAnd ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)))));
            }

            guards.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(
                isAnd ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)));
            return SyntaxFactory.Block(guards);
        }

        var result = FreshName("conditionResult");
        var statements = new List<StatementSyntax> { DeclareBool(result) };
        statements.AddRange(Evaluate(expression, result));
        statements.Add(SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName(result), SyntaxFactory.Block(
            SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))));
        statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));
        return SyntaxFactory.Block(statements);
    }

    private static bool CollectGuards(ExpressionSyntax expression, SyntaxKind kind, List<ExpressionSyntax> leaves)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            return binary.IsKind(kind) && CollectGuards(binary.Left, kind, leaves) && CollectGuards(binary.Right, kind, leaves);
        }

        leaves.Add(expression);
        return true;
    }

    private ExpressionSyntax LowerLeaf(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer)
    {
        expression = ConditionFacts.Unwrap(expression);
        // Semantic queries refer to original nodes, so keep them until recursion completes.
        return LowerOriginal(expression, statements, saveProducer);
    }

    private ExpressionSyntax LowerOriginal(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer)
    {
        var original = expression;
        expression = ConditionFacts.Unwrap(expression);
        var producer = ConditionFacts.IsProducer(expression, model);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            expression = unary.WithOperand(LowerOriginal(unary.Operand, statements, saveProducer: true));
        }
        else if (expression is BinaryExpressionSyntax binary
            && ConditionFacts.EvaluationNodes(binary).Skip(1).Any(node => ConditionFacts.IsProducer(node, model)))
        {
            var left = LowerOriginal(binary.Left, statements, saveProducer: true);
            if (ConditionFacts.EvaluationNodes(binary.Right).Any(node => ConditionFacts.IsProducer(node, model))
                && left is not LiteralExpressionSyntax && left is not IdentifierNameSyntax)
            {
                left = Save(binary.Left, left, statements);
            }

            var right = LowerOriginal(binary.Right, statements, saveProducer: true);
            expression = binary.WithLeft(left).WithRight(right);
        }
        else if (expression is InvocationExpressionSyntax invocation
            && invocation.ArgumentList.Arguments.Any(argument => ConditionFacts.EvaluationNodes(argument.Expression)
                .Any(node => ConditionFacts.IsProducer(node, model))))
        {
            var target = invocation.Expression;
            if (target is MemberAccessExpressionSyntax access
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { IsStatic: false })
            {
                var receiver = LowerOriginal(access.Expression, statements, saveProducer: true);
                receiver = Save(access.Expression, receiver, statements);
                target = access.WithExpression(receiver);
            }

            var arguments = new List<ArgumentSyntax>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var value = LowerOriginal(argument.Expression, statements, saveProducer: true);
                value = Save(argument.Expression, value, statements, model.GetTypeInfo(argument.Expression).ConvertedType);
                arguments.Add(argument.WithExpression(value));
            }

            expression = invocation.WithExpression(target).WithArgumentList(
                invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)));
        }
        else if (expression is MemberAccessExpressionSyntax member
            && ConditionFacts.EvaluationNodes(member.Expression).Any(node => ConditionFacts.IsProducer(node, model)))
        {
            expression = member.WithExpression(LowerOriginal(member.Expression, statements, saveProducer: true));
        }
        else if (expression is CastExpressionSyntax cast)
        {
            expression = cast.WithExpression(LowerOriginal(cast.Expression, statements, saveProducer: true));
        }

        if (producer && saveProducer)
        {
            return Save(original, expression, statements);
        }

        return expression.WithoutTrivia();
    }

    public ExpressionSyntax Save(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        var name = FreshName("conditionValue");
        type ??= model.GetTypeInfo(original).Type;
        var declaration = SyntaxFactory.VariableDeclaration(TypeSyntax(type), SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value.WithoutTrivia()))));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
        return SyntaxFactory.IdentifierName(name);
    }

    internal static TypeSyntax TypeSyntax(ITypeSymbol? type) => SyntaxFactory.ParseTypeName(
        type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "bool");

    internal static LocalDeclarationStatementSyntax DeclareBool(string name) => SyntaxFactory.LocalDeclarationStatement(
        SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))));

    private static ExpressionStatementSyntax Assign(string name, ExpressionSyntax expression) => SyntaxFactory.ExpressionStatement(
        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name), expression));

    internal static ExpressionSyntax Negate(ExpressionSyntax expression) => SyntaxFactory.PrefixUnaryExpression(
        SyntaxKind.LogicalNotExpression, SyntaxFactory.ParenthesizedExpression(expression));
}

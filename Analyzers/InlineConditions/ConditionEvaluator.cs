using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>Evaluates the original tree once, in order, without Boolean algebra.</summary>
internal sealed class ConditionEvaluator
{
    internal static readonly SyntaxAnnotation Linearized = new("InlineConditionLinearized");
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
            var leaves = new List<ExpressionSyntax>();
            ConditionFacts.CollectChain(binary, binary.Kind(), leaves);
            return EvaluateChain(leaves, 0, binary.Kind(), result);
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

    private List<StatementSyntax> EvaluateChain(List<ExpressionSyntax> leaves, int index, SyntaxKind kind, string result)
    {
        if (index == leaves.Count - 1)
        {
            return Evaluate(leaves[index], result);
        }

        var statements = new List<StatementSyntax>();
        ExpressionSyntax condition;
        if (IsAtomic(leaves[index]) && !model.GetConstantValue(leaves[index]).HasValue)
        {
            condition = LowerLeaf(leaves[index], statements, saveProducer: false);
        }
        else
        {
            statements.AddRange(Evaluate(leaves[index], result));
            condition = SyntaxFactory.IdentifierName(result);
        }

        var next = SyntaxFactory.Block(EvaluateChain(leaves, index + 1, kind, result));
        var terminal = SyntaxFactory.Block(Assign(result, SyntaxFactory.LiteralExpression(
            kind == SyntaxKind.LogicalAndExpression ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)));
        var trueBranch = kind == SyntaxKind.LogicalAndExpression ? next : terminal;
        var falseBranch = kind == SyntaxKind.LogicalAndExpression ? terminal : next;
        statements.Add(SyntaxFactory.IfStatement(condition, trueBranch, SyntaxFactory.ElseClause(falseBranch)));
        return statements;
    }

    private static bool IsAtomic(ExpressionSyntax expression)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            return false;
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return IsAtomic(unary.Operand);
        }

        return true;
    }

    public BlockSyntax ReturnBody(ExpressionSyntax expression)
    {
        var leaves = new List<ExpressionSyntax>();
        var unwrapped = ConditionFacts.Unwrap(expression);
        if (unwrapped is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
            && CollectGuards(unwrapped, binary.Kind(), leaves)
            && !leaves.Any(leaf => model.GetConstantValue(leaf).HasValue))
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
        ConditionFacts.CollectChain(expression, kind, leaves);
        return leaves.All(IsAtomic);
    }

    public ExpressionSyntax LowerCondition(ExpressionSyntax expression, List<StatementSyntax> statements)
    {
        if (IsAtomic(expression) && !model.GetConstantValue(expression).HasValue)
        {
            return LowerLeaf(expression, statements, saveProducer: false);
        }

        var result = FreshName("conditionResult");
        statements.Add(DeclareBool(result));
        statements.AddRange(Evaluate(expression, result));
        return SyntaxFactory.IdentifierName(result);
    }

    private ExpressionSyntax LowerLeaf(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer) =>
        LowerOriginal(expression, statements, saveProducer).WithAdditionalAnnotations(Linearized);

    private ExpressionSyntax LowerOriginal(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer)
    {
        var original = expression;
        if (expression is ParenthesizedExpressionSyntax parentheses)
        {
            return parentheses.WithExpression(LowerOriginal(parentheses.Expression, statements, saveProducer)).WithoutTrivia();
        }

        var producer = ConditionFacts.IsProducer(expression, model);
        if (expression is BinaryExpressionSyntax lazy && lazy.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            var result = FreshName("conditionResult");
            statements.Add(DeclareBool(result));
            statements.AddRange(Evaluate(lazy, result));
            return SyntaxFactory.IdentifierName(result);
        }

        if (expression is ConditionalExpressionSyntax conditional && HasProducer(conditional))
        {
            var result = FreshName("conditionValue");
            statements.Add(SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
                TypeSyntax(model.GetTypeInfo(conditional).Type), SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(result)))));
            var condition = LowerCondition(conditional.Condition, statements);
            var whenTrue = new List<StatementSyntax>();
            var trueValue = LowerOriginal(conditional.WhenTrue, whenTrue, saveProducer: true);
            whenTrue.Add(Assign(result, trueValue));
            var whenFalse = new List<StatementSyntax>();
            var falseValue = LowerOriginal(conditional.WhenFalse, whenFalse, saveProducer: true);
            whenFalse.Add(Assign(result, falseValue));
            statements.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(whenTrue),
                SyntaxFactory.ElseClause(SyntaxFactory.Block(whenFalse))));
            return SyntaxFactory.IdentifierName(result);
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.Kind() is SyntaxKind.LogicalNotExpression
            or SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression)
        {
            expression = unary.WithOperand(LowerOriginal(unary.Operand, statements, saveProducer: true));
        }
        else if (expression is BinaryExpressionSyntax binary
            && ConditionFacts.EvaluationNodes(binary).Skip(1).Any(node => ConditionFacts.IsProducer(node, model)))
        {
            var left = LowerOriginal(binary.Left, statements, saveProducer: true);
            if (ConditionFacts.EvaluationNodes(binary.Right).Any(node => ConditionFacts.IsProducer(node, model))
                && !model.GetConstantValue(binary.Left).HasValue)
            {
                left = Save(binary.Left, left, statements);
            }

            var right = LowerOriginal(binary.Right, statements, saveProducer: true);
            expression = binary.WithLeft(left).WithRight(right);
        }
        else if (expression is InvocationExpressionSyntax invocation
            && (invocation.ArgumentList.Arguments.Any(argument => ConditionFacts.EvaluationNodes(argument.Expression)
                .Any(node => ConditionFacts.IsProducer(node, model)))
                || invocation.Expression is MemberAccessExpressionSyntax receiverAccess
                    && ConditionFacts.EvaluationNodes(receiverAccess.Expression).Any(node => ConditionFacts.IsProducer(node, model))))
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
        else if (expression is ElementAccessExpressionSyntax element && HasProducer(element))
        {
            var receiver = LowerOriginal(element.Expression, statements, saveProducer: true);
            receiver = Save(element.Expression, receiver, statements);
            var arguments = new List<ArgumentSyntax>();
            foreach (var argument in element.ArgumentList.Arguments)
            {
                var value = LowerOriginal(argument.Expression, statements, saveProducer: true);
                value = Save(argument.Expression, value, statements, model.GetTypeInfo(argument.Expression).ConvertedType);
                arguments.Add(argument.WithExpression(value));
            }

            expression = element.WithExpression(receiver).WithArgumentList(
                element.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)));
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

    private bool HasProducer(ExpressionSyntax expression) =>
        ConditionFacts.EvaluationNodes(expression).Any(node => ConditionFacts.IsProducer(node, model));

    public ExpressionSyntax Save(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        var name = FreshName("conditionValue");
        type ??= model.GetTypeInfo(original).Type;
        var declaration = SyntaxFactory.VariableDeclaration(TypeSyntax(type), SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(
                value.WithoutTrivia().WithAdditionalAnnotations(Linearized)))));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
        return SyntaxFactory.IdentifierName(name);
    }

    internal static TypeSyntax TypeSyntax(ITypeSymbol? type) => SyntaxFactory.ParseTypeName(
        type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "bool");

    internal static LocalDeclarationStatementSyntax DeclareBool(string name) => SyntaxFactory.LocalDeclarationStatement(
        SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))));

    private static ExpressionStatementSyntax Assign(string name, ExpressionSyntax expression) => SyntaxFactory.ExpressionStatement(
        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name),
            expression.WithAdditionalAnnotations(Linearized)));

    internal static ExpressionSyntax Negate(ExpressionSyntax expression) => SyntaxFactory.PrefixUnaryExpression(
        SyntaxKind.LogicalNotExpression, SyntaxFactory.ParenthesizedExpression(expression));
}

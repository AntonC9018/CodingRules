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
        var statements = Guard(expression, exitWhenTrue: false, result: false);
        statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        return SyntaxFactory.Block(statements);
    }

    // Preserve the original short-circuit tree. Only literal terminal returns
    // are repeated; producers are still evaluated once, at their original point.
    private List<StatementSyntax> Guard(ExpressionSyntax expression, bool exitWhenTrue, bool result)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return Guard(unary.Operand, !exitWhenTrue, result);
        }
        if (expression is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            var disjunction = binary.IsKind(SyntaxKind.LogicalOrExpression);
            if (exitWhenTrue == disjunction)
            {
                var statements = Guard(binary.Left, exitWhenTrue, result);
                statements.AddRange(Guard(binary.Right, exitWhenTrue, result));
                return statements;
            }
            // To reject A || B, inspect B only if A was false. To accept
            // A && B, inspect B only if A was true.
            return Gate(binary.Left, !disjunction, Guard(binary.Right, exitWhenTrue, result));
        }
        var prefixes = new List<StatementSyntax>();
        var condition = LowerLeaf(expression, prefixes, saveProducer: false);
        if (model.GetConstantValue(expression).HasValue) condition = Save(expression, condition, prefixes);
        if (!exitWhenTrue) condition = NegateCondition(expression, condition);
        prefixes.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(SyntaxFactory.ReturnStatement(
            SyntaxFactory.LiteralExpression(result ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)))));
        return prefixes;
    }

    private List<StatementSyntax> Gate(ExpressionSyntax expression, bool whenTrue, List<StatementSyntax> body)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
            return Gate(unary.Operand, !whenTrue, body);
        if (expression is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            // A gate with a mixed left subtree must not duplicate its body.
            // A local function owns that decision and retains lazy evaluation.
            var name = FreshName("CheckCondition");
            var helper = SyntaxFactory.LocalFunctionStatement(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), name)
                .WithBody(ReturnBody(expression));
            var condition = (ExpressionSyntax)SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name));
            if (!whenTrue) condition = Negate(condition);
            return new List<StatementSyntax> { helper, SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(body)) };
        }
        var prefixes = new List<StatementSyntax>();
        var leaf = LowerLeaf(expression, prefixes, saveProducer: false);
        if (model.GetConstantValue(expression).HasValue) leaf = Save(expression, leaf, prefixes);
        if (!whenTrue) leaf = NegateCondition(expression, leaf);
        prefixes.Add(SyntaxFactory.IfStatement(leaf, SyntaxFactory.Block(body)));
        return prefixes;
    }

    internal ExpressionSyntax NegateCondition(ExpressionSyntax original, ExpressionSyntax lowered)
    {
        var unwrapped = ConditionFacts.Unwrap(original);
        var value = ConditionFacts.Unwrap(lowered);
        if (unwrapped is BinaryExpressionSyntax binary && value is BinaryExpressionSyntax rewritten
            && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
            && model.GetOperation(binary) is Microsoft.CodeAnalysis.Operations.IBinaryOperation { OperatorMethod: null })
        {
            var equal = binary.IsKind(SyntaxKind.EqualsExpression);
            return SyntaxFactory.BinaryExpression(equal ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression,
                rewritten.Left, rewritten.Right);
        }
        return Negate(lowered);
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
        else if (expression is IsPatternExpressionSyntax pattern && ConditionFacts.IsServiceNullTest(pattern, model))
        {
            expression = pattern.WithExpression(LowerOriginal(pattern.Expression, statements, saveProducer: true));
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
        var declaration = SyntaxFactory.VariableDeclaration(type is null ? TypeSyntax(type)
            : EvaluationSyntax.Type(type, model, original.SpanStart), SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(
                value.WithoutTrivia().WithAdditionalAnnotations(Linearized)))));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
        return SyntaxFactory.IdentifierName(name);
    }

    internal static TypeSyntax TypeSyntax(ITypeSymbol? type) => type is null
        ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)) : EvaluationSyntax.Type(type);

    internal static LocalDeclarationStatementSyntax DeclareBool(string name) => SyntaxFactory.LocalDeclarationStatement(
        SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))));

    private static ExpressionStatementSyntax Assign(string name, ExpressionSyntax expression) => SyntaxFactory.ExpressionStatement(
        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name),
            expression.WithAdditionalAnnotations(Linearized)));

    internal static ExpressionSyntax Negate(ExpressionSyntax expression)
    {
        var value = ConditionFacts.Unwrap(expression);
        if (value is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
            return ConditionFacts.Unwrap(unary.Operand);
        var operand = value is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            ? value : SyntaxFactory.ParenthesizedExpression(value);
        return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, operand);
    }
}

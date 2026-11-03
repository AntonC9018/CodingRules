using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>
/// Evaluates the original tree once, in order, without Boolean algebra. Decisions
/// become guard clauses with literal returns; no mutable result variable is used.
/// </summary>
internal sealed class ConditionEvaluator
{
    internal static readonly SyntaxAnnotation Linearized = new("InlineConditionLinearized");
    private readonly SemanticModel model;
    private readonly EvaluationNames names;
    private readonly StableValues? stable;
    private readonly HashSet<string> settled = new();

    public ConditionEvaluator(SemanticModel model, SyntaxNode declarationSpace, ExpressionSyntax? site = null)
    {
        this.model = model;
        names = new EvaluationNames(declarationSpace, model);
        stable = site is null ? null : new StableValues(model, declarationSpace, site);
    }

    public string FreshName(Placeholder placeholder) => names.Fresh(placeholder);

    public string FreshName(string domainName) => names.Fresh(domainName);

    /// <summary>A leaf that is not itself a short-circuit decision, possibly negated.</summary>
    internal static bool IsAtomic(ExpressionSyntax expression)
    {
        expression = Parentheses.Unwrap(expression);
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
        expression = Parentheses.Unwrap(expression);
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
        if (model.GetConstantValue(expression).HasValue) condition = Declare(expression, condition, prefixes);
        if (!exitWhenTrue) condition = NegateCondition(expression, condition);
        prefixes.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(SyntaxFactory.ReturnStatement(
            SyntaxFactory.LiteralExpression(result ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)))));
        return prefixes;
    }

    private List<StatementSyntax> Gate(ExpressionSyntax expression, bool whenTrue, List<StatementSyntax> body)
    {
        expression = Parentheses.Unwrap(expression);
        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
            return Gate(unary.Operand, !whenTrue, body);
        if (expression is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            // A gate with a mixed left subtree must not duplicate its body.
            // A local function owns that decision and retains lazy evaluation.
            var statements = new List<StatementSyntax>();
            var condition = Decide(expression, statements);
            if (!whenTrue) condition = Negate(condition);
            statements.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(body)));
            return statements;
        }
        var prefixes = new List<StatementSyntax>();
        var leaf = LowerLeaf(expression, prefixes, saveProducer: false);
        if (model.GetConstantValue(expression).HasValue) leaf = Declare(expression, leaf, prefixes);
        if (!whenTrue) leaf = NegateCondition(expression, leaf);
        prefixes.Add(SyntaxFactory.IfStatement(leaf, SyntaxFactory.Block(body)));
        return prefixes;
    }

    internal ExpressionSyntax NegateCondition(ExpressionSyntax original, ExpressionSyntax lowered)
    {
        var unwrapped = Parentheses.Unwrap(original);
        var value = Parentheses.Unwrap(lowered);
        if (unwrapped is BinaryExpressionSyntax binary && value is BinaryExpressionSyntax rewritten
            && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
            && model.GetOperation(binary) is Microsoft.CodeAnalysis.Operations.IBinaryOperation { OperatorMethod: null })
        {
            var equal = binary.IsKind(SyntaxKind.EqualsExpression);
            return SyntaxFactory.BinaryExpression(equal ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression,
                rewritten.Left, rewritten.Right);
        }
        if (unwrapped is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression)
            && model.GetOperation(unary) is Microsoft.CodeAnalysis.Operations.IUnaryOperation { OperatorMethod: not null })
        {
            return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(lowered));
        }
        return Negate(lowered);
    }

    /// <summary>A flow-preserving guard leaf; the plan admits only atomic, non-constant leaves.</summary>
    public ExpressionSyntax LowerCondition(ExpressionSyntax expression, List<StatementSyntax> statements) =>
        LowerLeaf(expression, statements, saveProducer: false);

    private ExpressionSyntax LowerLeaf(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer) =>
        LowerOriginal(expression, statements, saveProducer).WithAdditionalAnnotations(Linearized);

    private ExpressionSyntax LowerOriginal(ExpressionSyntax expression, List<StatementSyntax> statements, bool saveProducer)
    {
        var original = expression;
        if (expression is ParenthesizedExpressionSyntax parentheses)
        {
            return Parentheses.Rewrap(parentheses, LowerOriginal(parentheses.Expression, statements, saveProducer));
        }

        var producer = ConditionFacts.IsProducer(expression, model);
        // A nested decision or conditional value keeps its own short-circuiting.
        // A nested decision with work becomes a guard-clause local function called
        // in place; a nested conditional value with work is saved whole.
        if (expression is BinaryExpressionSyntax lazy && lazy.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            return HasProducer(expression) ? Decide(expression, statements) : expression.WithoutTrivia();
        }

        if (expression is ConditionalExpressionSyntax)
        {
            return saveProducer && HasProducer(expression) ? Save(original, expression, statements) : expression.WithoutTrivia();
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.Kind() is SyntaxKind.LogicalNotExpression
            or SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression)
        {
            // The operand is the last work before the operator, so it needs a name
            // only when the enclosing expression saves this value; a guard tests it in place.
            expression = unary.WithOperand(LowerOriginal(unary.Operand, statements, saveProducer));
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
            if (stable?.HoistableTarget(original) is { } target)
            {
                // Run the mutation as its own statement and read its result back,
                // rather than chaining it into another declaration.
                statements.Add(SyntaxFactory.ExpressionStatement(expression.WithoutTrivia().WithAdditionalAnnotations(Linearized)));
                settled.Add(target.Name);
                return SyntaxFactory.IdentifierName(target.Name);
            }

            return Save(original, expression, statements);
        }

        return expression.WithoutTrivia();
    }

    /// <summary>Defines a <c>TODO_CONDITION</c> guard-clause local function for a nested decision and calls it.</summary>
    private ExpressionSyntax Decide(ExpressionSyntax decision, List<StatementSyntax> statements)
    {
        var name = FreshName(Placeholder.Condition);
        statements.Add(SyntaxFactory.LocalFunctionStatement(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), name)
            .WithBody(ReturnBody(decision)));
        return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name));
    }

    // A value needs no copy when later work cannot change it: a constant, a
    // variable this evaluator already saved or hoisted, or an unchanged local.
    private bool IsSettled(ExpressionSyntax original, ExpressionSyntax value)
    {
        if (model.GetConstantValue(original).HasValue && original.IsEquivalentTo(value)) return true;
        if (Parentheses.Unwrap(value) is not IdentifierNameSyntax identifier) return false;
        if (settled.Contains(identifier.Identifier.ValueText)) return true;
        return stable is not null && original.IsEquivalentTo(value) && stable.IsStable(original);
    }

    private bool HasProducer(ExpressionSyntax expression) =>
        ConditionFacts.EvaluationNodes(expression).Any(node => ConditionFacts.IsProducer(node, model));

    public ExpressionSyntax Save(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        return IsSettled(original, value) ? value : Declare(original, value, statements, type);
    }

    // A constant guard leaf is declared too, so the guard does not test a constant.
    private ExpressionSyntax Declare(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        var name = FreshName(Placeholder.Variable);
        settled.Add(name);
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

    internal static ExpressionSyntax Negate(ExpressionSyntax expression)
    {
        var value = Parentheses.Unwrap(expression);
        if (value is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
            return Parentheses.Unwrap(unary.Operand);
        var operand = value is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            ? value : SyntaxFactory.ParenthesizedExpression(value);
        return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, operand);
    }
}

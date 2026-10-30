using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>Return-only lowering; never duplicates a producer or invents a helper.</summary>
internal sealed class CompoundPredicateRewrite
{
    private readonly SemanticModel model;

    private CompoundPredicateRewrite(SemanticModel model) => this.model = model;

    public static BlockSyntax? Create(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken token)
    {
        var catalog = PredicateCatalog.For(model.Compilation);
        if (!(CompoundPredicateFacts.IsCompound(lambda, model, catalog)
                || CompoundPredicateFacts.ExceedsCompactDepth(lambda, model, catalog))
            || lambda.Body is not ExpressionSyntax expression || lambda.AsyncKeyword.RawKind != 0
            || ConditionHosts.IsInsideExpressionTree(lambda, model)
            || lambda.ContainsDirectives
            || lambda.DescendantTrivia().Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            || CompilerDiagnostics.HasError(model, lambda, token)
            || NamedArgumentObservers.Affected(lambda, model, token))
        {
            return null;
        }

        // Keep conditional APIs in their original compiler-controlled context.
        // The first safe subset declines them rather than proving each host.
        if (lambda.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
            model.GetSymbolInfo(call, token).Symbol is IMethodSymbol method
            && method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
                == "System.Diagnostics.ConditionalAttribute")))
        {
            return null;
        }

        List<StatementSyntax>? statements;
        if (CompoundPredicateFacts.IsCompound(lambda, model, catalog))
        {
            statements = new CompoundPredicateRewrite(model).Decision(expression, invertResult: false);
        }
        else
        {
            if (!IsIdentityBoolean(expression, model))
            {
                // A dynamic/custom value can use a different truth operator in
                // an if than its original conversion to the delegate's bool.
                return null;
            }

            // Depth alone does not justify splitting the decision. Keep its exact
            // expression (including operators/conversions) in the original order.
            statements = new List<StatementSyntax>
            {
                SyntaxFactory.IfStatement(expression.WithoutTrivia(), SyntaxFactory.Block(Return(true))),
                Return(false),
            };
        }

        return statements is null ? null : ConditionSiteRewrites.AddElasticLineBreaks(SyntaxFactory.Block(statements))
            .WithTriviaFrom(expression);
    }

    private List<StatementSyntax>? Decision(ExpressionSyntax expression, bool invertResult)
    {
        expression = Parentheses.Unwrap(expression);
        if (BuiltinNot(expression) is { } operand)
        {
            return Decision(operand, !invertResult);
        }

        if (expression is BinaryExpressionSyntax binary && CompoundPredicateFacts.IsBuiltinLogical(binary, model))
        {
            var disjunction = binary.IsKind(SyntaxKind.LogicalOrExpression);
            var leaves = new List<ExpressionSyntax>();
            Collect(binary, binary.Kind(), leaves);
            var statements = new List<StatementSyntax>();
            for (var index = 0; index < leaves.Count; index++)
            {
                var leaf = Parentheses.Unwrap(leaves[index]);
                // The last decision owns the remaining outcomes. This keeps
                // A && (B || C) flat without an else or a Boolean scratch value.
                if (index == leaves.Count - 1 && (leaf is BinaryExpressionSyntax nested
                    && CompoundPredicateFacts.IsBuiltinLogical(nested, model) || BuiltinNot(leaf) is not null))
                {
                    var tail = Decision(leaf, invertResult);
                    if (tail is null)
                    {
                        return null;
                    }

                    statements.AddRange(tail);
                    return statements;
                }

                var guards = Exit(leaf, disjunction, disjunction ^ invertResult);
                if (guards is null)
                {
                    return null;
                }

                statements.AddRange(guards);
            }

            statements.Add(Return(!disjunction ^ invertResult));
            return statements;
        }

        var exits = Exit(expression, whenTrue: true, result: !invertResult);
        exits?.Add(Return(invertResult));
        return exits;
    }

    private List<StatementSyntax>? Exit(ExpressionSyntax expression, bool whenTrue, bool result)
    {
        expression = Parentheses.Unwrap(expression);
        if (BuiltinNot(expression) is { } operand)
        {
            return Exit(operand, !whenTrue, result);
        }

        if (expression is BinaryExpressionSyntax binary && CompoundPredicateFacts.IsBuiltinLogical(binary, model))
        {
            var disjunction = binary.IsKind(SyntaxKind.LogicalOrExpression);
            var right = Exit(binary.Right, whenTrue, result);
            if (right is null)
            {
                return null;
            }

            if (whenTrue != disjunction)
            {
                return Gate(binary.Left, !disjunction, right);
            }

            var left = Exit(binary.Left, whenTrue, result);
            left?.AddRange(right);
            return left;
        }

        return Gate(expression, whenTrue, new List<StatementSyntax> { Return(result) });
    }

    private List<StatementSyntax>? Gate(ExpressionSyntax expression, bool whenTrue, List<StatementSyntax> body)
    {
        expression = Parentheses.Unwrap(expression);
        if (BuiltinNot(expression) is { } operand)
        {
            return Gate(operand, !whenTrue, body);
        }

        if (expression is BinaryExpressionSyntax binary && CompoundPredicateFacts.IsBuiltinLogical(binary, model))
        {
            // The other direction would duplicate this continuation's calls or
            // need a helper/scratch tree. Keep its diagnostic, decline the fix.
            if (whenTrue != binary.IsKind(SyntaxKind.LogicalAndExpression))
            {
                return null;
            }

            var right = Gate(binary.Right, whenTrue, body);
            return right is null ? null : Gate(binary.Left, whenTrue, right);
        }

        if (!IsIdentityBoolean(expression, model))
        {
            return null;
        }

        var condition = expression.WithoutTrivia();
        if (!whenTrue)
        {
            // Only negate the resulting built-in bool. Never substitute a
            // different user-defined equality or floating-point comparison.
            condition = SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(condition));
        }

        return new List<StatementSyntax> { SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(body)) };
    }

    private static bool IsIdentityBoolean(ExpressionSyntax expression, SemanticModel model) =>
        model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Boolean
        && model.GetConversion(expression).IsIdentity;

    private ExpressionSyntax? BuiltinNot(ExpressionSyntax expression) =>
        expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression)
        && model.GetOperation(unary) is IUnaryOperation
        {
            OperatorMethod: null,
            Operand.Type.SpecialType: SpecialType.System_Boolean,
        } ? unary.Operand : null;

    private void Collect(ExpressionSyntax expression, SyntaxKind kind, List<ExpressionSyntax> leaves)
    {
        expression = Parentheses.Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(kind)
            && CompoundPredicateFacts.IsBuiltinLogical(binary, model))
        {
            Collect(binary.Left, kind, leaves);
            Collect(binary.Right, kind, leaves);
            return;
        }

        leaves.Add(expression);
    }

    private static ReturnStatementSyntax Return(bool result) => SyntaxFactory.ReturnStatement(
        SyntaxFactory.LiteralExpression(result ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression));
}

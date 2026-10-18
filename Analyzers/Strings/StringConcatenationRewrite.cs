using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class StringConcatenationRewrite
{
    public static ExpressionSyntax? Create(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken token)
    {
        if (((CSharpParseOptions)binary.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp6
            || !IsStringAddition(binary, model, token) || model.GetConstantValue(binary, token).HasValue
            || binary.ContainsDiagnostics || binary.ContainsDirectives
            || binary.DescendantTrivia().Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            || ConditionHosts.IsInsideExpressionTree(binary, model)
            || NamedArgumentObservers.Affected(binary, model, token)
            || HasImplicitCallerObserver(binary, model, token)
            // Implicit collection Add calls can observe their element's source text.
            || binary.Ancestors().Any(node => node.IsKind(SyntaxKind.CollectionInitializerExpression))
            || model.GetDiagnostics(binary.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return null;

        // One diagnostic owns a connected string-only tree, including parentheses.
        SyntaxNode outer = binary;
        while (outer.Parent is ParenthesizedExpressionSyntax) outer = outer.Parent;
        if (outer.Parent is BinaryExpressionSyntax parent && IsStringAddition(parent, model, token)) return null;

        var operands = new List<ExpressionSyntax>();
        Collect(binary, model, token, operands);
        var contents = new List<InterpolatedStringContentSyntax>();
        foreach (var operand in operands)
        {
            if (operand is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var value = literal.Token.ValueText;
                var quoted = SyntaxFactory.Literal(value).Text;
                var escaped = quoted.Substring(1, quoted.Length - 2).Replace("{", "{{").Replace("}", "}}");
                contents.Add(SyntaxFactory.InterpolatedStringText(SyntaxFactory.Token(default,
                    SyntaxKind.InterpolatedStringTextToken, escaped, value, default)));
                continue;
            }

            var hole = operand.WithoutTrivia();
            if (hole is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
                or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or ConditionalAccessExpressionSyntax))
                hole = SyntaxFactory.ParenthesizedExpression(hole);
            // An alias at interpolation depth zero can be parsed as a format
            // clause. Nested arguments/already parenthesized aliases need no wrap.
            if (hole.DescendantNodesAndSelf().OfType<AliasQualifiedNameSyntax>().Any())
            {
                var probe = SyntaxFactory.ParseExpression("$\"{" + hole.ToFullString() + "}\"",
                    options: (CSharpParseOptions)binary.SyntaxTree.Options) as InterpolatedStringExpressionSyntax;
                if (probe?.Contents.SingleOrDefault() is not InterpolationSyntax parsed
                    || !SyntaxFactory.AreEquivalent(hole, parsed.Expression))
                    hole = SyntaxFactory.ParenthesizedExpression(hole);
            }
            // Before C# 11 ordinary interpolation holes cannot contain newlines.
            if (((CSharpParseOptions)binary.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp11
                && hole.ToFullString().IndexOfAny(['\r', '\n']) >= 0) return null;
            contents.Add(SyntaxFactory.Interpolation(hole));
        }

        ExpressionSyntax replacement = SyntaxFactory.InterpolatedStringExpression(
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken), SyntaxFactory.List(contents));
        // Interpolation is target-sensitive. Keep a string expression so a handler,
        // FormattableString, span or user conversion cannot replace the old binding.
        replacement = SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)), replacement);
        if (outer.Parent is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax or ElementAccessExpressionSyntax)
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        return replacement.WithTriviaFrom(binary);
    }

    private static bool IsStringAddition(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken token) =>
        binary.IsKind(SyntaxKind.AddExpression)
        && model.GetOperation(binary, token) is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, IsLifted: false,
            Type.SpecialType: SpecialType.System_String,
            LeftOperand.Type.SpecialType: SpecialType.System_String,
            RightOperand.Type.SpecialType: SpecialType.System_String,
        }
        // Inspect source types too: an implicit user conversion must not become
        // formatting of the original custom value in an interpolation hole.
        && model.GetTypeInfo(binary.Left, token).Type?.SpecialType == SpecialType.System_String
        && model.GetTypeInfo(binary.Right, token).Type?.SpecialType == SpecialType.System_String;

    private static bool HasImplicitCallerObserver(SyntaxNode selected, SemanticModel model, CancellationToken token)
    {
        // Ordinary calls/indexers are covered above. Also inspect implicit Add
        // calls and indexer initializers, including later line-number observers.
        foreach (var node in selected.SyntaxTree.GetRoot(token).DescendantNodes().Where(node =>
            node.IsKind(SyntaxKind.CollectionInitializerExpression) || node is ImplicitElementAccessSyntax))
        {
            if (model.GetOperation(node, token) is not { } operation) continue;
            foreach (var argument in operation.DescendantsAndSelf().OfType<IArgumentOperation>().Where(argument => argument.IsImplicit))
            {
                var site = argument.Parent!.Syntax.Span;
                if (argument.Parameter?.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                    && (attribute.AttributeClass.Name == "CallerLineNumberAttribute" && site.End >= selected.SpanStart
                        || attribute.AttributeClass.Name is "CallerArgumentExpressionAttribute" or "CallerMemberNameAttribute" or "CallerFilePathAttribute"
                            && site.Contains(selected.Span))) == true) return true;
            }
        }
        return false;
    }

    private static void Collect(ExpressionSyntax expression, SemanticModel model, CancellationToken token, List<ExpressionSyntax> operands)
    {
        var unwrapped = expression;
        while (unwrapped is ParenthesizedExpressionSyntax parentheses) unwrapped = parentheses.Expression;
        if (unwrapped is BinaryExpressionSyntax binary && IsStringAddition(binary, model, token))
        {
            Collect(binary.Left, model, token, operands);
            Collect(binary.Right, model, token, operands);
            return;
        }
        operands.Add(expression);
    }
}

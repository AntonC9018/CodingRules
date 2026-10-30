using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>The rejected-action set reviewed in ScheduleLib #229 A15, not a general status policy.</summary>
internal static class ReviewedHttpStatusFilter
{
    private static readonly Dictionary<int, string> Members = new()
    {
        [400] = "BadRequest",
        [403] = "Forbidden",
        [404] = "NotFound",
        [409] = "Conflict",
        [412] = "PreconditionFailed",
        [422] = "UnprocessableEntity",
    };

    public static IsPatternExpressionSyntax? NamedPattern(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression.Parent is not CatchFilterClauseSyntax
            || Parentheses.Unwrap(expression) is not IsPatternExpressionSyntax pattern
            || Parentheses.Unwrap(pattern.Expression) is not CastExpressionSyntax cast
            || model.GetTypeInfo(cast).Type?.SpecialType != SpecialType.System_Int32)
        {
            return null;
        }

        var type = model.GetTypeInfo(cast.Expression).Type;
        if (type?.ToDisplayString() != "System.Net.HttpStatusCode"
            || type.ContainingAssembly.Identity.Name is not ("System.Net.Primitives" or "System"))
        {
            return null;
        }

        var constants = new List<ConstantPatternSyntax>();
        if (!Collect(pattern.Pattern, constants) || constants.Count != Members.Count)
        {
            return null;
        }

        var seen = new HashSet<int>();
        foreach (var constant in constants)
        {
            if (constant.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression)
                || model.GetConstantValue(literal).Value is not int value || !Members.TryGetValue(value, out var member)
                || !seen.Add(value)
                || !type.GetMembers(member).OfType<IFieldSymbol>().Any(field => field.ConstantValue is int code && code == value))
            {
                return null;
            }
        }

        var named = pattern.Pattern.ReplaceNodes(constants, (original, _) => original.WithExpression(
            SyntaxFactory.ParseExpression("global::System.Net.HttpStatusCode." + Members[(int)model.GetConstantValue(original.Expression).Value!])));
        return pattern.WithExpression(cast.Expression.WithoutTrivia()).WithPattern(named).WithoutTrivia();
    }

    private static bool Collect(PatternSyntax pattern, List<ConstantPatternSyntax> constants)
    {
        if (pattern is ConstantPatternSyntax constant)
        {
            constants.Add(constant);
            return true;
        }

        if (pattern is ParenthesizedPatternSyntax parentheses)
        {
            return Collect(parentheses.Pattern, constants);
        }

        return pattern is BinaryPatternSyntax binary && binary.IsKind(SyntaxKind.OrPattern)
            && Collect(binary.Left, constants) && Collect(binary.Right, constants);
    }
}

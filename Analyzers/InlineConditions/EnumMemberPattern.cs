using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>
/// A catch filter that tests an enum through its underlying number, such as
/// <c>(int)e.HttpStatusCode is 400 or 404</c>, where every literal names exactly
/// one declared member. Converting an enum to its own underlying type preserves
/// every value, so <c>e.HttpStatusCode is HttpStatusCode.BadRequest or
/// HttpStatusCode.NotFound</c> matches the same inputs.
/// </summary>
internal sealed class EnumMemberPattern
{
    private EnumMemberPattern(IsPatternExpressionSyntax pattern, string predicateName)
    {
        Pattern = pattern;
        PredicateName = predicateName;
    }

    /// <summary>The same test written with enum members.</summary>
    public IsPatternExpressionSyntax Pattern { get; }

    /// <summary>A name stem for the extracted predicate, such as MatchesHttpStatusCode.</summary>
    public string PredicateName { get; }

    public static EnumMemberPattern? Create(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression.Parent is not CatchFilterClauseSyntax
            || Parentheses.Unwrap(expression) is not IsPatternExpressionSyntax pattern
            || Parentheses.Unwrap(pattern.Expression) is not CastExpressionSyntax cast
            || model.GetTypeInfo(cast.Expression).Type is not INamedTypeSymbol
                { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } enumType
            || !SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(cast).Type, underlying)
            || !model.IsAccessible(cast.SpanStart, enumType))
        {
            return null;
        }

        var constants = new List<ConstantPatternSyntax>();
        if (!Collect(pattern.Pattern, constants))
        {
            return null;
        }

        var members = new Dictionary<ConstantPatternSyntax, IFieldSymbol>();
        var seen = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        foreach (var constant in constants)
        {
            var member = Member(constant, enumType, model, cast.SpanStart);
            if (member is null || !seen.Add(member))
            {
                return null;
            }

            members.Add(constant, member);
        }

        var typeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var named = pattern.Pattern.ReplaceNodes(constants, (original, _) => original.WithExpression(
            SyntaxFactory.ParseExpression(typeName + "." + members[original].Name)));
        var rewritten = pattern.WithExpression(cast.Expression.WithoutTrivia()).WithPattern(named).WithoutTrivia();
        return new EnumMemberPattern(rewritten, "Matches" + enumType.Name);
    }

    // Aliases (MultipleChoices/Ambiguous, UnprocessableEntity/UnprocessableContent)
    // share one constant, so any of them preserves behavior; the first declared,
    // usable member is chosen deterministically.
    private static IFieldSymbol? Member(ConstantPatternSyntax constant, INamedTypeSymbol enumType, SemanticModel model, int position)
    {
        if (constant.Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.NumericLiteralExpression)
            || model.GetConstantValue(literal).Value is not { } value)
        {
            return null;
        }

        return enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field =>
            field.HasConstantValue
            && SameNumber(field.ConstantValue!, value)
            && model.IsAccessible(position, field)
            && !field.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute"));
    }

    private static bool SameNumber(object left, object right)
    {
        try
        {
            return Convert.ToDecimal(left) == Convert.ToDecimal(right);
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    // Alternatives and negation of constants only; a conjunction of distinct
    // constants can never match and stays for review.
    private static bool Collect(PatternSyntax pattern, List<ConstantPatternSyntax> constants)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax constant:
                constants.Add(constant);
                return true;
            case ParenthesizedPatternSyntax parentheses:
                return Collect(parentheses.Pattern, constants);
            case UnaryPatternSyntax negation when negation.IsKind(SyntaxKind.NotPattern):
                return Collect(negation.Pattern, constants);
            case BinaryPatternSyntax binary when binary.IsKind(SyntaxKind.OrPattern):
                return Collect(binary.Left, constants) && Collect(binary.Right, constants);
            default:
                return false;
        }
    }
}

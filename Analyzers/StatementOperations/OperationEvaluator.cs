using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>Stages values at their original reached positions, in source order.</summary>
internal sealed class OperationEvaluator
{
    internal const string BindingAnnotation = "StatementOperationBinding";
    internal const string ConversionAnnotation = "StatementOperationConversion";
    internal const string LinearizedAnnotation = "StatementOperationLinearized";
    private readonly SemanticModel model;
    private readonly bool lowerConditionProducers;
    public EvaluationNames Names { get; }

    public OperationEvaluator(OperationPlan plan, ExpressionSyntax expression)
    {
        model = plan.Model;
        Names = new EvaluationNames(plan.DeclarationSpace, model);
        lowerConditionProducers = OperationPlan.HasConditionReason(expression, model);
    }

    public ExpressionSyntax Lower(ExpressionSyntax expression, List<StatementSyntax> statements, bool save = false)
    {
        var original = expression;
        if (expression is ParenthesizedExpressionSyntax parentheses)
        {
            return parentheses.WithExpression(Lower(parentheses.Expression, statements, save)).WithoutTrivia();
        }

        if (expression is ConditionalExpressionSyntax conditional)
        {
            var condition = Lower(conditional.Condition, statements);
            var type = model.GetTypeInfo(conditional).Type ?? model.GetTypeInfo(conditional).ConvertedType!;
            var name = Names.Fresh("operationValue");
            statements.Add(Declare(type, name));
            var whenTrue = Branch(conditional.WhenTrue, name, type);
            var whenFalse = Branch(conditional.WhenFalse, name, type);
            statements.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(whenTrue), SyntaxFactory.ElseClause(SyntaxFactory.Block(whenFalse))));
            return SyntaxFactory.IdentifierName(name);
        }

        if (expression is BinaryExpressionSyntax lazy && lazy.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            var left = Lower(lazy.Left, statements);
            var name = Names.Fresh("operationValue");
            statements.Add(Declare(model.GetTypeInfo(lazy).Type!, name));
            var rightStatements = new List<StatementSyntax>();
            var right = Lower(lazy.Right, rightStatements);
            rightStatements.Add(Assign(name, right));
            var terminal = SyntaxFactory.Block(Assign(name, SyntaxFactory.LiteralExpression(lazy.IsKind(SyntaxKind.LogicalAndExpression)
                ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)));
            var next = SyntaxFactory.Block(rightStatements);
            statements.Add(SyntaxFactory.IfStatement(left, lazy.IsKind(SyntaxKind.LogicalAndExpression) ? next : terminal,
                SyntaxFactory.ElseClause(lazy.IsKind(SyntaxKind.LogicalAndExpression) ? terminal : next)));
            return SyntaxFactory.IdentifierName(name);
        }

        if (expression is InvocationExpressionSyntax invocation && OperationPlan.NeedsArgumentLowering(invocation, model, lowerConditionProducers))
        {
            var target = invocation.Expression;
            var method = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
            if (target is MemberAccessExpressionSyntax member && !method.IsStatic)
            {
                var receiver = Lower(member.Expression, statements, save: true);
                receiver = Save(member.Expression, receiver, statements);
                target = member.WithExpression(receiver);
            }
            else if (method.MethodKind == MethodKind.DelegateInvoke)
            {
                target = Save(target, Lower(target, statements), statements);
            }

            expression = invocation.WithExpression(target).WithArgumentList(invocation.ArgumentList.WithArguments(
                SyntaxFactory.SeparatedList(LowerArguments(invocation.ArgumentList.Arguments, statements))));
        }
        else if (expression is BaseObjectCreationExpressionSyntax creation && creation.ArgumentList is not null
            && creation.ArgumentList.Arguments.Any(argument => HasWork(argument.Expression)))
        {
            var arguments = creation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(LowerArguments(creation.ArgumentList.Arguments, statements)));
            expression = creation switch
            {
                ObjectCreationExpressionSyntax explicitCreation => explicitCreation.WithArgumentList(arguments),
                ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.WithArgumentList(arguments),
                _ => expression,
            };
        }
        else if (expression is BinaryExpressionSyntax binary && HasWork(binary))
        {
            var operation = (IBinaryOperation)model.GetOperation(binary)!;
            var left = Lower(binary.Left, statements, save: true);
            if (HasWork(binary.Right)) left = Save(binary.Left, left, statements, operation.LeftOperand.Type);
            var right = Lower(binary.Right, statements, save: true);
            expression = binary.WithLeft(left).WithRight(right);
        }
        else if (expression is PrefixUnaryExpressionSyntax unary && !OperationFacts.IsMutation(unary))
        {
            expression = unary.WithOperand(Lower(unary.Operand, statements, save: true));
        }
        else if (expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = postfix.WithOperand(Lower(postfix.Operand, statements, save));
        }
        else if (expression is CastExpressionSyntax cast)
        {
            expression = cast.WithExpression(Lower(cast.Expression, statements, save: true));
        }
        else if (expression is MemberAccessExpressionSyntax member && HasWork(member.Expression))
        {
            expression = member.WithExpression(Lower(member.Expression, statements, save: true));
        }
        else if (expression is ElementAccessExpressionSyntax element && HasWork(element))
        {
            var receiver = Save(element.Expression, Lower(element.Expression, statements, save: true), statements);
            expression = element.WithExpression(receiver).WithArgumentList(element.ArgumentList.WithArguments(
                SyntaxFactory.SeparatedList(LowerArguments(element.ArgumentList.Arguments, statements))));
        }
        else if (expression is AssignmentExpressionSyntax assignment)
        {
            var left = LowerLeft(assignment.Left, statements);
            expression = assignment.WithLeft(left).WithRight(Lower(assignment.Right, statements, save: true));
        }
        else if (expression is InterpolatedStringExpressionSyntax interpolation)
        {
            var contents = new List<InterpolatedStringContentSyntax>();
            foreach (var content in interpolation.Contents)
            {
                if (content is InterpolationSyntax item)
                {
                    // String interpolation may format a value before evaluating the
                    // next hole. Staging all holes would advance later evaluations.
                    // The plan accepts only single-hole producer compositions.
                    contents.Add(item.WithExpression(Lower(item.Expression, statements, save: true)));
                }
                else contents.Add(content);
            }
            expression = interpolation.WithContents(SyntaxFactory.List(contents));
        }

        expression = PreserveCalls(original, expression);
        if (save && (OperationFacts.IsProducer(original, model) || OperationFacts.IsCalculation(original, model)
            || lowerConditionProducers && ConditionFacts.IsProducer(original, model)))
        {
            return Save(original, expression, statements);
        }

        return expression.WithoutTrivia();
    }

    private bool HasWork(ExpressionSyntax expression) => OperationPlan.HasLowering(expression, model, lowerConditionProducers);

    private List<StatementSyntax> Branch(ExpressionSyntax expression, string result, ITypeSymbol type)
    {
        var statements = new List<StatementSyntax>();
        if (expression is ThrowExpressionSyntax throwing)
        {
            statements.Add(SyntaxFactory.ThrowStatement(Lower(throwing.Expression, statements, save: true)));
        }
        else
        {
            var value = Lower(expression, statements, save: true);
            statements.Add(Assign(result, value.WithAdditionalAnnotations(Conversion(expression, type))));
        }
        return statements;
    }

    private IEnumerable<ArgumentSyntax> LowerArguments(SeparatedSyntaxList<ArgumentSyntax> arguments, List<StatementSyntax> statements)
    {
        var result = new List<ArgumentSyntax>();
        foreach (var argument in arguments)
        {
            var value = Lower(argument.Expression, statements, save: true);
            var type = model.GetTypeInfo(argument.Expression).ConvertedType ?? model.GetTypeInfo(argument.Expression).Type!;
            value = Save(argument.Expression, value, statements, type);
            result.Add(argument.WithExpression(value));
        }
        return result;
    }

    private ExpressionSyntax LowerLeft(ExpressionSyntax left, List<StatementSyntax> statements) => left switch
    {
        MemberAccessExpressionSyntax member when model.GetSymbolInfo(member).Symbol is not IFieldSymbol { IsStatic: true }
            and not IPropertySymbol { IsStatic: true } => member.WithExpression(Save(member.Expression, Lower(member.Expression, statements), statements)),
        ElementAccessExpressionSyntax element => element.WithExpression(Save(element.Expression, Lower(element.Expression, statements), statements))
            .WithArgumentList(element.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(LowerArguments(element.ArgumentList.Arguments, statements)))),
        _ => left,
    };

    public ExpressionSyntax Save(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        type ??= model.GetTypeInfo(original).Type ?? model.GetTypeInfo(original).ConvertedType!;
        var name = Names.Fresh("operationValue");
        var declaration = SyntaxFactory.VariableDeclaration(EvaluationSyntax.Type(type), SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value.WithoutTrivia()
                .WithAdditionalAnnotations(Conversion(original, type), new SyntaxAnnotation(LinearizedAnnotation))))));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
        return SyntaxFactory.IdentifierName(name);
    }

    public BlockSyntax ReturnBody(ExpressionSyntax expression)
    {
        var statements = new List<StatementSyntax>();
        var value = Lower(expression, statements);
        var type = model.GetTypeInfo(expression).Type ?? model.GetTypeInfo(expression).ConvertedType!;
        if (type.SpecialType == SpecialType.System_Void) statements.Add(SyntaxFactory.ExpressionStatement(value));
        else
        {
            value = Save(expression, value, statements, type);
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                statements.Add(SyntaxFactory.IfStatement(value, SyntaxFactory.Block(SyntaxFactory.ReturnStatement(
                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))));
                statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));
            }
            else statements.Add(SyntaxFactory.ReturnStatement(value));
        }
        var body = SyntaxFactory.Block(statements);
        var checkedContext = expression.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
        if (checkedContext is not null)
        {
            var kind = checkedContext.IsKind(SyntaxKind.CheckedExpression) || checkedContext.IsKind(SyntaxKind.CheckedStatement)
                ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement;
            body = SyntaxFactory.Block(SyntaxFactory.CheckedStatement(kind, body));
        }
        return body;
    }

    private static LocalDeclarationStatementSyntax Declare(ITypeSymbol type, string name) => SyntaxFactory.LocalDeclarationStatement(
        SyntaxFactory.VariableDeclaration(EvaluationSyntax.Type(type), SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))));

    private static ExpressionStatementSyntax Assign(string name, ExpressionSyntax value) => SyntaxFactory.ExpressionStatement(
        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name), value));

    internal static string Key(SyntaxNode node) => node.SpanStart.ToString(CultureInfo.InvariantCulture) + ":" + node.Span.Length + ":" + node.RawKind;

    private SyntaxAnnotation Conversion(ExpressionSyntax original, ITypeSymbol type) => new(ConversionAnnotation,
        ConversionFingerprint(model.ClassifyConversion(original, type)));

    internal static string ConversionFingerprint(Conversion conversion) => string.Join(":", conversion.Exists, conversion.IsIdentity,
        conversion.IsImplicit, conversion.IsNumeric, conversion.IsReference, conversion.IsBoxing, conversion.IsUnboxing,
        conversion.IsNullable, conversion.IsUserDefined, conversion.MethodSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    internal static ExpressionSyntax PreserveCalls(ExpressionSyntax original, ExpressionSyntax changed)
    {
        // Calls in opaque direct values (including allowed parameterless calls)
        // retain their original syntax and their own occurrence identities.
        if (ReferenceEquals(original, changed) || original.ToFullString() == changed.ToFullString())
        {
            var keys = original.DescendantNodes().OfType<ExpressionSyntax>()
                .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
                .ToDictionary(node => (node.SpanStart - original.SpanStart, node.Span.Length, node.RawKind), Key);
            var start = changed.SpanStart;
            changed = changed.ReplaceNodes(changed.DescendantNodes().OfType<ExpressionSyntax>()
                .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax),
                (node, rewritten) => rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(BindingAnnotation, keys[(node.SpanStart - start, node.Span.Length, node.RawKind)])));
        }
        if (original is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
            changed = changed.WithAdditionalAnnotations(new SyntaxAnnotation(BindingAnnotation, Key(original)));
        return changed;
    }
}

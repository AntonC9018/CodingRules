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
    private readonly Dictionary<string, ITypeSymbol> savedValues = new();
    private readonly SemanticModel model;
    private readonly bool lowerConditionProducers;
    private readonly StableValues stable;
    public EvaluationNames Names { get; }

    public OperationEvaluator(OperationPlan plan, ExpressionSyntax expression)
    {
        model = plan.Model;
        Names = new EvaluationNames(plan.DeclarationSpace, model);
        lowerConditionProducers = OperationPlan.HasConditionReason(expression, model);
        stable = new StableValues(model, plan.DeclarationSpace, expression);
    }

    public ExpressionSyntax Lower(ExpressionSyntax expression, List<StatementSyntax> statements, bool save = false)
    {
        var original = expression;
        if (expression is ParenthesizedExpressionSyntax parentheses)
        {
            return Parentheses.Rewrap(parentheses, Lower(parentheses.Expression, statements, save));
        }

        // A conditional value or decision with work in a lazily reached operand
        // becomes a local function called in place, so that work stays lazy.
        // Otherwise only the first operand is staged and the rest stays in place.
        if (expression is ConditionalExpressionSyntax conditional)
        {
            if (HasWork(conditional.WhenTrue) || HasWork(conditional.WhenFalse)
                || OperationFacts.HasComposedPipelineBranch(conditional, model))
            {
                return Defer(conditional, statements, Placeholder.Calculation);
            }

            // Branches without work are lowered only to keep their call identities.
            var value = conditional.WithCondition(Lower(conditional.Condition, statements))
                .WithWhenTrue(Lower(conditional.WhenTrue, statements))
                .WithWhenFalse(Lower(conditional.WhenFalse, statements)).WithoutTrivia();
            // A nested conditional value is named; a root one is already named by its host.
            return save ? Save(original, value, statements) : value;
        }

        if (expression is BinaryExpressionSyntax lazy && IsDecision(lazy))
        {
            return HasWork(lazy.Right) ? Defer(lazy, statements, Placeholder.Condition)
                : lazy.WithLeft(Lower(lazy.Left, statements)).WithRight(Lower(lazy.Right, statements)).WithoutTrivia();
        }

        if (expression is InvocationExpressionSyntax invocation && OperationPlan.NeedsArgumentLowering(invocation, model, lowerConditionProducers))
        {
            var target = invocation.Expression;
            var method = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
            var argumentsHaveWork = invocation.ArgumentList.Arguments.Any(argument => HasWork(argument.Expression));
            if (target is MemberAccessExpressionSyntax member && !method.IsStatic)
            {
                // The receiver is read before the arguments; it needs a name only
                // when argument work is staged ahead of the call.
                var receiver = Lower(member.Expression, statements, save: true);
                if (argumentsHaveWork) receiver = Save(member.Expression, receiver, statements);
                target = member.WithExpression(receiver);
            }
            else if (method.MethodKind == MethodKind.DelegateInvoke)
            {
                target = Save(target, Lower(target, statements), statements);
            }

            expression = invocation.WithExpression(target).WithArgumentList(invocation.ArgumentList.WithArguments(
                LowerArguments(invocation.ArgumentList.Arguments, statements)));
        }
        else if (expression is BaseObjectCreationExpressionSyntax creation && creation.ArgumentList is not null
            && creation.ArgumentList.Arguments.Any(argument => HasWork(argument.Expression)))
        {
            var arguments = creation.ArgumentList.WithArguments(LowerArguments(creation.ArgumentList.Arguments, statements));
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
        else if (expression is IsPatternExpressionSyntax pattern)
        {
            expression = pattern.WithExpression(Lower(pattern.Expression, statements, save: true));
        }
        else if (expression is MemberAccessExpressionSyntax member && HasWork(member.Expression))
        {
            expression = member.WithExpression(Lower(member.Expression, statements, save: true));
        }
        else if (expression is ElementAccessExpressionSyntax element && HasWork(element))
        {
            var receiver = Lower(element.Expression, statements, save: true);
            if (element.ArgumentList.Arguments.Any(argument => HasWork(argument.Expression)))
            {
                receiver = Save(element.Expression, receiver, statements);
            }

            expression = element.WithExpression(receiver).WithArgumentList(element.ArgumentList.WithArguments(
                LowerArguments(element.ArgumentList.Arguments, statements)));
        }
        else if (expression is AssignmentExpressionSyntax assignment)
        {
            // The assignment names its value; staged work goes before it, not into a chained copy.
            var left = LowerLeft(assignment.Left, statements, HasWork(assignment.Right));
            expression = assignment.WithLeft(left).WithRight(Lower(assignment.Right, statements));
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

    // A condition the inline-condition rules report still separates its operations.
    private bool IsAcceptedInline(ArgumentSyntax argument) => !lowerConditionProducers
        && OperationFacts.IsOrdinaryArgument(argument, model)
        && !OperationFacts.Create(argument.Expression, model).HasReason;

    private bool HasWork(ExpressionSyntax expression) => OperationPlan.HasLowering(expression, model, lowerConditionProducers);

    private static bool IsDecision(BinaryExpressionSyntax binary) =>
        binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression;

    /// <summary>Defines a placeholder-named local function returning <paramref name="expression"/> and calls it in place.</summary>
    private ExpressionSyntax Defer(ExpressionSyntax expression, List<StatementSyntax> statements, Placeholder placeholder)
    {
        var name = Names.Fresh(placeholder);
        var type = model.GetTypeInfo(expression).Type ?? model.GetTypeInfo(expression).ConvertedType!;
        statements.Add(SyntaxFactory.LocalFunctionStatement(EvaluationSyntax.Type(type, model, expression.SpanStart), name)
            .WithBody(ReturnBody(expression)));
        return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name));
    }

    // An argument value is named only when a later argument stages work that
    // would otherwise run before it.
    private SeparatedSyntaxList<ArgumentSyntax> LowerArguments(SeparatedSyntaxList<ArgumentSyntax> arguments, List<StatementSyntax> statements,
        bool laterWork = false)
    {
        var result = new List<ArgumentSyntax>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var snapshot = laterWork || arguments.Skip(index + 1).Any(later => HasWork(later.Expression));
            // An argument the rules accept inline, such as one simple call, stays in place.
            var value = Lower(argument.Expression, statements, save: snapshot || !IsAcceptedInline(argument));
            if (snapshot)
            {
                var type = model.GetTypeInfo(argument.Expression).ConvertedType ?? model.GetTypeInfo(argument.Expression).Type!;
                value = Save(argument.Expression, value, statements, type);
            }
            result.Add(argument.WithExpression(value.WithTriviaFrom(argument.Expression)));
        }
        return SyntaxFactory.SeparatedList(result, arguments.GetSeparators());
    }

    private ExpressionSyntax LowerLeft(ExpressionSyntax left, List<StatementSyntax> statements, bool valueHasWork)
    {
        ExpressionSyntax Receiver(ExpressionSyntax receiver)
        {
            var lowered = Lower(receiver, statements);
            return valueHasWork ? Save(receiver, lowered, statements) : lowered;
        }

        return left switch
        {
            MemberAccessExpressionSyntax member when model.GetSymbolInfo(member).Symbol is not IFieldSymbol { IsStatic: true }
                and not IPropertySymbol { IsStatic: true } => member.WithExpression(Receiver(member.Expression)),
            ElementAccessExpressionSyntax element => element.WithExpression(Receiver(element.Expression))
                .WithArgumentList(element.ArgumentList.WithArguments(LowerArguments(element.ArgumentList.Arguments, statements, valueHasWork))),
            _ => left,
        };
    }

    public ExpressionSyntax Save(ExpressionSyntax original, ExpressionSyntax value, List<StatementSyntax> statements, ITypeSymbol? type = null)
    {
        type ??= model.GetTypeInfo(original).Type ?? model.GetTypeInfo(original).ConvertedType!;
        // A constant needs no snapshot when its conversion is identity. Keep
        // actual calculations linearized, and retain conversions such as boxing
        // or user operators at their original argument-evaluation position.
        if (model.GetConstantValue(original).HasValue && !HasWork(original)
            && model.ClassifyConversion(original, type).IsIdentity)
            return value.WithAdditionalAnnotations(Conversion(original, type));
        // An unchanged local or parameter is read in place; copying it would only add an alias.
        if (original is IdentifierNameSyntax source && value is IdentifierNameSyntax same
            && same.Identifier.ValueText == source.Identifier.ValueText && stable.IsStable(original)) return value;
        // Lower may already have staged a target-typed construction. Its
        // construction conversion was checked there; copying that local would
        // introduce an identity conversion, not repeat the source conversion.
        var conversion = value is IdentifierNameSyntax identifier && savedValues.TryGetValue(identifier.Identifier.ValueText, out var savedType)
            ? ((CSharpCompilation)model.Compilation).ClassifyConversion(savedType, type) : (Conversion?)null;
        // A saved value is read in place when its conversion has no user code to
        // time; only a user-defined conversion keeps its own named step.
        if (conversion is { IsImplicit: true, IsUserDefined: false } direct)
            return value.WithAdditionalAnnotations(new SyntaxAnnotation(ConversionAnnotation, ConversionFingerprint(direct)));
        var annotation = conversion is { } staged ? new SyntaxAnnotation(ConversionAnnotation, ConversionFingerprint(staged))
            : Conversion(original, type);
        var name = Names.Fresh(Placeholder.Variable);
        var declaration = SyntaxFactory.VariableDeclaration(EvaluationSyntax.Type(type, model, original.SpanStart), SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value.WithoutTrivia()
                .WithAdditionalAnnotations(annotation, new SyntaxAnnotation(LinearizedAnnotation))))));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
        savedValues.Add(name, type);
        return SyntaxFactory.IdentifierName(name);
    }

    /// <summary>
    /// Statements that evaluate <paramref name="expression"/> and return it, as the
    /// <paramref name="returnType"/> of the enclosing function (by default its own type),
    /// or discard it.
    /// </summary>
    public BlockSyntax ReturnBody(ExpressionSyntax expression, bool discard = false, ITypeSymbol? returnType = null)
    {
        var statements = new List<StatementSyntax>();
        var type = returnType ?? model.GetTypeInfo(expression).Type ?? model.GetTypeInfo(expression).ConvertedType!;
        if (discard || type.SpecialType == SpecialType.System_Void) statements.Add(SyntaxFactory.ExpressionStatement(Lower(expression, statements)));
        else AddReturn(expression, type, statements);
        // Statements start at the column of the moved source line, so the formatter
        // keeps the relative indentation of its continuation lines.
        var indentation = ConditionSiteRewrites.SourceIndentation(expression);
        var body = SyntaxFactory.Block(statements.Select(statement => statement.WithLeadingTrivia(indentation)));
        var checkedContext = expression.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
        if (checkedContext is not null)
        {
            var kind = checkedContext.IsKind(SyntaxKind.CheckedExpression) || checkedContext.IsKind(SyntaxKind.CheckedStatement)
                ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement;
            body = SyntaxFactory.Block(SyntaxFactory.CheckedStatement(kind, body));
        }
        return ConditionSiteRewrites.AddElasticLineBreaks(body);
    }

    // Returns the value directly. A conditional value or a decision becomes guard
    // clauses, so its lazily reached operands keep their own evaluation.
    private void AddReturn(ExpressionSyntax expression, ITypeSymbol type, List<StatementSyntax> statements)
    {
        var value = Parentheses.Unwrap(expression);
        if (value is ConditionalExpressionSyntax conditional)
        {
            var condition = Lower(conditional.Condition, statements);
            var whenTrue = new List<StatementSyntax>();
            AddReturn(conditional.WhenTrue, type, whenTrue);
            statements.Add(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(whenTrue)));
            AddReturn(conditional.WhenFalse, type, statements);
            return;
        }

        if (value is BinaryExpressionSyntax decision && IsDecision(decision))
        {
            var conjunction = decision.IsKind(SyntaxKind.LogicalAndExpression);
            var left = Lower(decision.Left, statements);
            statements.Add(SyntaxFactory.IfStatement(conjunction ? ConditionEvaluator.Negate(left) : left,
                SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(conjunction
                    ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)))));
            AddReturn(decision.Right, type, statements);
            return;
        }

        if (value is ThrowExpressionSyntax throwing)
        {
            statements.Add(SyntaxFactory.ThrowStatement(Lower(throwing.Expression, statements)));
            return;
        }

        var lowered = Lower(value, statements);
        statements.Add(SyntaxFactory.ReturnStatement(lowered.WithAdditionalAnnotations(Conversion(value, type))));
    }

    internal static string Key(SyntaxNode node) => node.SpanStart.ToString(CultureInfo.InvariantCulture) + ":" + node.Span.Length + ":" + node.RawKind;

    private SyntaxAnnotation Conversion(ExpressionSyntax original, ITypeSymbol type) => new(ConversionAnnotation,
        ConversionFingerprint(SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(original).ConvertedType, type)
            ? model.GetConversion(original) : model.ClassifyConversion(original, type)));

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
                (node, rewritten) => Bind(rewritten, keys[(node.SpanStart - start, node.Span.Length, node.RawKind)]));
        }
        if (original is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
            changed = Bind(changed, Key(original));
        return changed;
    }

    // A call kept in place by an inner lowering already carries its identity.
    private static ExpressionSyntax Bind(ExpressionSyntax call, string key) => call.HasAnnotations(BindingAnnotation)
        ? call
        : call.WithAdditionalAnnotations(new SyntaxAnnotation(BindingAnnotation, key));
}

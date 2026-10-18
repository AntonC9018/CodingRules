using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class UnchangedAssignmentPlan
{
    private static readonly ConditionalWeakTable<SemanticModel, ObserverScan> ObserverScans = new();
    private sealed class ObserverScan
    {
        public bool Affected { get; }
        public ObserverScan(SemanticModel model, CancellationToken token) => Affected = ScanSourceObservers(model, token);
    }

    public SyntaxNode Owner { get; }
    public SyntaxNode Replacement { get; }

    private UnchangedAssignmentPlan(SyntaxNode owner, SyntaxNode replacement)
    {
        Owner = owner;
        Replacement = replacement;
    }

    public static UnchangedAssignmentPlan? Create(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken token)
    {
        if (Unwrap(assignment.Right) is not ConditionalExpressionSyntax conditional
            || model.GetOperation(assignment, token) is not ISimpleAssignmentOperation { IsRef: false } operation
            || Storage(operation.Target) is not { } storage
            || operation.Type is not { CanBeReferencedByName: true, IsRefLikeType: false } type
            || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic or TypeKind.Error
            || !SymbolEqualityComparer.Default.Equals(type, model.GetTypeInfo(conditional, token).Type)
            || model.GetTypeInfo(conditional.Condition, token).Type?.SpecialType != SpecialType.System_Boolean
            || ConditionHosts.IsInsideExpressionTree(assignment, model)) return null;

        bool trueUnchanged;
        if (SameStorage(conditional.WhenTrue, storage, model, token)) trueUnchanged = true;
        else if (SameStorage(conditional.WhenFalse, storage, model, token)) trueUnchanged = false;
        else if (!NullBranch(conditional, storage, type, model, token, out trueUnchanged)) return null;

        ExpressionSyntax expression = assignment;
        while (expression.Parent is ParenthesizedExpressionSyntax parentheses) expression = parentheses;
        var owner = expression.Parent;
        bool returns;
        SyntaxNode? declaration = null;
        if (owner is ExpressionStatementSyntax) returns = false;
        else if (owner is ReturnStatementSyntax) returns = true;
        else if (owner is ArrowExpressionClauseSyntax arrow)
        {
            declaration = arrow.Parent;
            var symbol = model.GetDeclaredSymbol(declaration!, token);
            if (symbol is IMethodSymbol { IsAsync: false, ReturnsByRef: false, ReturnsByRefReadonly: false } method)
                returns = !method.ReturnsVoid;
            else if (symbol is IPropertySymbol { ReturnsByRef: false, ReturnsByRefReadonly: false }) returns = true;
            else return null;
            owner = declaration;
        }
        else return null;

        if (owner is null || owner.ContainsDiagnostics || owner.ContainsDirectives
            || owner.Ancestors().Any(node => node is RefExpressionSyntax)
            || model.GetDiagnostics(owner.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || HasSourceObserver(model, token)) return null;

        var unchanged = trueUnchanged ? conditional.WhenTrue : conditional.WhenFalse;
        var changed = trueUnchanged ? conditional.WhenFalse : conditional.WhenTrue;
        // Keep the branch's original contextual conversion. In particular a null
        // literal must not bypass a conversion from the assignment's static type.
        var conversion = model.ClassifyConversion(unchanged, type);
        if (!conversion.IsImplicit || conversion.IsUserDefined) return null;
        var update = assignment.WithRight(changed.WithoutTrivia()).WithoutTrivia();
        var condition = conditional.Condition.WithoutTrivia();
        if (trueUnchanged)
            condition = SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(condition));
        StatementSyntax updateStatement = returns ? SyntaxFactory.ReturnStatement(update) : SyntaxFactory.ExpressionStatement(update);
        var statements = SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(updateStatement)));
        if (returns)
        {
            var targetType = SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                    | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)));
            ExpressionSyntax value = unchanged.WithoutTrivia();
            if (!conversion.IsIdentity)
                value = SyntaxFactory.CastExpression(targetType, SyntaxFactory.ParenthesizedExpression(value));
            statements = statements.Add(SyntaxFactory.ReturnStatement(value));
        }
        var block = SyntaxFactory.Block(statements);
        SyntaxNode? replacement = declaration switch
        {
            null when owner is ExpressionStatementSyntax { Parent: BlockSyntax } => statements[0],
            null => block,
            MethodDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            LocalFunctionStatementSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            AccessorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            PropertyDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(Getter(block)),
            IndexerDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(Getter(block)),
            _ => null,
        };
        if (replacement is null) return null;

        // Retain every internal comment exactly once, including comments on the
        // removed ?: and arrow tokens. Exterior documentation/trivia stays put.
        var comments = owner.DescendantTrivia().Where(trivia => owner.Span.Contains(trivia.Span)
            && (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))).ToArray();
        replacement = replacement.ReplaceTrivia(replacement.DescendantTrivia().Where(trivia =>
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)), (_, _) => default);
        if (comments.Length != 0)
        {
            var firstBlock = replacement.DescendantNodesAndSelf().OfType<BlockSyntax>().First();
            var trivia = SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed);
            foreach (var comment in comments) trivia = trivia.Add(comment).Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
            replacement = replacement.ReplaceNode(firstBlock, firstBlock.WithOpenBraceToken(firstBlock.OpenBraceToken.WithTrailingTrivia(trivia)));
        }
        return new UnchangedAssignmentPlan(owner, replacement.WithTriviaFrom(owner));
    }

    private static AccessorListSyntax Getter(BlockSyntax block) => SyntaxFactory.AccessorList(
        SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(block)));

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses) expression = parentheses.Expression;
        return expression;
    }

    private static ISymbol? Storage(IOperation? operation) => operation switch
    {
        ILocalReferenceOperation { Local.RefKind: RefKind.None } local => local.Local,
        IParameterReferenceOperation { Parameter.RefKind: RefKind.None } parameter => parameter.Parameter,
        // No receiver evaluation, null dereference, mutable struct copy, volatile
        // store, type initialization or setter may be omitted.
        IFieldReferenceOperation { Field: { IsStatic: false, IsVolatile: false, IsReadOnly: false, RefKind: RefKind.None },
            Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsReferenceType: true } } field => field.Field,
        _ => null,
    };

    private static bool SameStorage(ExpressionSyntax expression, ISymbol storage, SemanticModel model, CancellationToken token) =>
        SymbolEqualityComparer.Default.Equals(Storage(model.GetOperation(Unwrap(expression), token)), storage);

    private static bool NullBranch(ConditionalExpressionSyntax conditional, ISymbol storage, ITypeSymbol type,
        SemanticModel model, CancellationToken token, out bool trueUnchanged)
    {
        trueUnchanged = false;
        if (!type.IsReferenceType) return false;
        var test = Unwrap(conditional.Condition);
        bool trueIsNull;
        if (test is IsPatternExpressionSyntax pattern && SameStorage(pattern.Expression, storage, model, token))
        {
            var value = pattern.Pattern;
            trueIsNull = true;
            if (value is UnaryPatternSyntax unary && unary.IsKind(SyntaxKind.NotPattern))
            {
                value = unary.Pattern;
                trueIsNull = false;
            }
            if (value is not ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression }) return false;
        }
        else if (test is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
            && model.GetOperation(binary, token) is IBinaryOperation { OperatorMethod: null, IsLifted: false }
            && (SameStorage(binary.Left, storage, model, token) && Unwrap(binary.Right).IsKind(SyntaxKind.NullLiteralExpression)
                || SameStorage(binary.Right, storage, model, token) && Unwrap(binary.Left).IsKind(SyntaxKind.NullLiteralExpression)))
            trueIsNull = binary.IsKind(SyntaxKind.EqualsExpression);
        else return false;
        trueUnchanged = trueIsNull;
        return Unwrap(trueIsNull ? conditional.WhenTrue : conditional.WhenFalse).IsKind(SyntaxKind.NullLiteralExpression);
    }

    private static bool HasSourceObserver(SemanticModel model, CancellationToken token)
    {
        if (!ObserverScans.TryGetValue(model, out var scan))
        {
            scan = new ObserverScan(model, token);
            token.ThrowIfCancellationRequested();
            scan = ObserverScans.GetValue(model, _ => scan);
        }
        return scan.Affected;
    }

    private static bool ScanSourceObservers(SemanticModel model, CancellationToken token)
    {
        // A block changes source lines and property getter member names. Include
        // implicit collection Add/indexer arguments as well as ordinary calls.
        foreach (var node in model.SyntaxTree.GetRoot(token).DescendantNodes().Where(node =>
            node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ElementAccessExpressionSyntax
                or ImplicitElementAccessSyntax || node.IsKind(SyntaxKind.CollectionInitializerExpression)))
        {
            token.ThrowIfCancellationRequested();
            if (model.GetOperation(node, token) is not { } operation) continue;
            if (operation.DescendantsAndSelf().OfType<IArgumentOperation>().Any(argument => argument.IsImplicit
                && argument.Parameter?.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                    && attribute.AttributeClass.Name is "CallerArgumentExpressionAttribute" or "CallerLineNumberAttribute"
                        or "CallerMemberNameAttribute" or "CallerFilePathAttribute") == true)) return true;
        }
        return false;
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CodingRules;

internal static class SpanFixes
{
    private const string Original = "SpanOriginalOccurrence";
    private const string PreviousDiagnostic = "SpanPreviousDiagnostic";
    private static readonly SyntaxAnnotation Selected = new("SpanSelectedInspection");
    private static readonly SyntaxAnnotation Generated = new("SpanGeneratedHelper");
    private static readonly ImmutableArray<DiagnosticAnalyzer> Older = ImmutableArray.Create<DiagnosticAnalyzer>(new ExplicitReturnDecisionAnalyzer(),
        new NestedReturnDecisionAnalyzer(), new InlineConditionAnalyzer(), new StatementOperationAnalyzer(), new PipelineAnalyzer(),
        new NamedArgumentAnalyzer(), new PrimitiveSentinelAnalyzer());

    public static async Task<Document?> ChangeAsync(Document document, InvocationExpressionSyntax trim, SemanticModel model, CancellationToken token)
    {
        var analyzerOptions = document.Project.AnalyzerOptions;
        var plan = SpanPlan.Create(trim, model, SpanCatalog.For(model.Compilation), token, analyzerOptions);
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (plan is null || root is null)
        {
            return null;
        }

        var before = await Diagnostics(model.Compilation, analyzerOptions, token).ConfigureAwait(false);
        var tracked = Track(root, plan, model, before, token);
        var proposed = document.WithSyntaxRoot(Rewrite(plan, tracked.Root, model));
        proposed = await GeneratedSyntaxFormatter.FormatAsync(proposed, document, token).ConfigureAwait(false);
        var formatted = await proposed.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var saved = document.WithText(await proposed.GetTextAsync(token).ConfigureAwait(false));
        var after = await saved.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (formatted is null || after is null)
        {
            return null;
        }

        var parsed = after.SyntaxTree.GetRoot(token);
        var preservesBehavior = CompilerMessages(model.Compilation, token).SequenceEqual(CompilerMessages(after.Compilation, token))
            && UneditedBindingsSurvive(model.Compilation, after.Compilation, model.SyntaxTree, token)
            && OccurrencesKeepBindings(tracked.Bindings, formatted, parsed, after, token)
            && InspectionKeepsItsValue(plan, model, formatted, parsed, after, token)
            && RemovesAnInspection(plan, root, model, parsed, after, analyzerOptions, token);
        if (!preservesBehavior)
        {
            return null;
        }

        var afterDiagnostics = await Diagnostics(after.Compilation, analyzerOptions, token).ConfigureAwait(false);
        return KeepsAnalyzerDiagnostics(before, afterDiagnostics, tracked.Unchanged, formatted, model.SyntaxTree, after.SyntaxTree)
            ? saved
            : null;
    }

    private sealed record TrackedRoot(SyntaxNode Root, string[] Bindings, Diagnostic[] Unchanged);

    // Annotates every binding occurrence outside the inspection (plus the receiver
    // it keeps) and every analyzer diagnostic the rewrite should not touch, so
    // both can be found again after formatting.
    private static TrackedRoot Track(SyntaxNode root, SpanPlan plan, SemanticModel model, ImmutableArray<Diagnostic> before,
        CancellationToken token)
    {
        var occurrences = root.DescendantNodes()
            .Where(node => Occurrence(node, model, token)
                && (!plan.Inspection.Span.Contains(node.Span) || plan.Receiver.Span.Contains(node.Span)))
            .ToArray();
        var bindings = occurrences.Select(node => BindingIdentity.Call(model.GetOperation(node, token))).ToArray();
        var unchanged = before
            .Where(item => item.Location.SourceTree == model.SyntaxTree
                && !item.Location.SourceSpan.IntersectsWith(plan.Inspection.Span))
            .ToArray();
        var diagnosticNodes = unchanged
            .Select(item => root.FindNode(item.Location.SourceSpan, getInnermostNodeForTie: true))
            .ToArray();
        var annotated = root.ReplaceNodes(occurrences.Concat(diagnosticNodes).Distinct(), (node, changed) =>
        {
            var occurrence = System.Array.IndexOf(occurrences, node);
            if (occurrence >= 0)
            {
                changed = changed.WithAdditionalAnnotations(new SyntaxAnnotation(Original, occurrence.ToString()));
            }

            for (var index = 0; index < diagnosticNodes.Length; index++)
            {
                if (diagnosticNodes[index] == node)
                {
                    changed = changed.WithAdditionalAnnotations(new SyntaxAnnotation(PreviousDiagnostic, index.ToString()));
                }
            }

            return changed;
        });
        return new TrackedRoot(annotated, bindings, unchanged);
    }

    // Each tracked occurrence appears exactly once in the saved text with its original binding.
    private static bool OccurrencesKeepBindings(string[] bindings, SyntaxNode formatted, SyntaxNode parsed, SemanticModel after,
        CancellationToken token)
    {
        var seen = new HashSet<int>();
        foreach (var node in formatted.GetAnnotatedNodes(Original))
        {
            var index = int.Parse(node.GetAnnotations(Original).Single().Data!);
            var savedOccurrence = parsed.FindNode(node.Span, getInnermostNodeForTie: true).AncestorsAndSelf()
                .FirstOrDefault(value => value.RawKind == node.RawKind && value.Span == node.Span);
            if (savedOccurrence is null
                || !seen.Add(index)
                || BindingIdentity.Call(after.GetOperation(savedOccurrence, token)) != bindings[index])
            {
                return false;
            }
        }

        return seen.Count == bindings.Length;
    }

    // The helper call has the inspection's type, converted type and conversion.
    private static bool InspectionKeepsItsValue(SpanPlan plan, SemanticModel model, SyntaxNode formatted, SyntaxNode parsed,
        SemanticModel after, CancellationToken token)
    {
        var selected = formatted.GetAnnotatedNodes(Selected).Single();
        var call = parsed.FindNode(selected.Span, getInnermostNodeForTie: true);
        if (plan.StaticLocal && after.GetOperation(call, token) is not IInvocationOperation { TargetMethod.IsStatic: true })
        {
            return false;
        }

        var originalType = model.GetTypeInfo(plan.Inspection, token);
        var callType = after.GetTypeInfo(call, token);
        var originalConversion = OperationEvaluator.ConversionFingerprint(model.GetConversion(plan.Inspection, token));
        var callConversion = OperationEvaluator.ConversionFingerprint(after.GetConversion((ExpressionSyntax)call, token));
        return BindingIdentity.Type(originalType.Type) == BindingIdentity.Type(callType.Type)
            && BindingIdentity.Type(originalType.ConvertedType) == BindingIdentity.Type(callType.ConvertedType)
            && originalConversion == callConversion;
    }

    private static bool RemovesAnInspection(SpanPlan plan, SyntaxNode root, SemanticModel model, SyntaxNode parsed,
        SemanticModel after, AnalyzerOptions options, CancellationToken token)
    {
        var afterCatalog = SpanCatalog.For(after.Compilation);
        var remaining = parsed.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(node => SpanPlan.Create(node, after, afterCatalog, token, options) is not null);
        var original = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(node => SpanPlan.Create(node, model, plan.Catalog, token, options) is not null);
        return remaining < original;
    }

    // No analyzer diagnostic becomes more frequent, diagnostics in other files
    // stay in place, tracked diagnostics follow their nodes, and the generated
    // helper itself is clean.
    private static bool KeepsAnalyzerDiagnostics(ImmutableArray<Diagnostic> before, ImmutableArray<Diagnostic> after,
        Diagnostic[] unchanged, SyntaxNode formatted, SyntaxTree originalTree, SyntaxTree savedTree)
    {
        var counts = before.GroupBy(Key).ToDictionary(group => group.Key, group => group.Count());
        if (after.GroupBy(Key).Any(group => !counts.TryGetValue(group.Key, out var count) || group.Count() > count))
        {
            return false;
        }

        var otherFilesKept = before
            .Where(item => item.Location.SourceTree != originalTree)
            .All(diagnostic => after.Any(item => item.Location.SourceTree == diagnostic.Location.SourceTree
                && item.Location.SourceSpan == diagnostic.Location.SourceSpan
                && Key(item) == Key(diagnostic)));
        if (!otherFilesKept)
        {
            return false;
        }

        foreach (var node in formatted.GetAnnotatedNodes(PreviousDiagnostic))
        {
            foreach (var annotation in node.GetAnnotations(PreviousDiagnostic))
            {
                var original = unchanged[int.Parse(annotation.Data!)];
                var followed = after.Any(item => item.Location.SourceTree == savedTree
                    && item.Location.SourceSpan == node.Span
                    && Key(item) == Key(original));
                if (!followed)
                {
                    return false;
                }
            }
        }

        var helper = formatted.GetAnnotatedNodes(Generated).Single();
        return !after.Any(item => item.Location.SourceTree == savedTree && helper.Span.Contains(item.Location.SourceSpan));
    }

    private static SyntaxNode Rewrite(SpanPlan plan, SyntaxNode root, SemanticModel model)
    {
        var names = new EvaluationNames(plan.Space, model);
        var name = plan.MemberHelperName ?? names.Fresh(plan.Constant is null ? "InspectTextLength" : "InspectTextEquals");
        var source = names.Fresh("sourceText");
        var length = names.Fresh("textLength");
        var span = names.Fresh("textSpan");
        var trimmed = names.Fresh("trimmedText");
        var inspection = (ExpressionSyntax)root.FindNode(plan.Inspection.Span, getInnermostNodeForTie: true);
        var receiver = (ExpressionSyntax)inspection.DescendantNodesAndSelf().First(node => node.Span == plan.Receiver.Span && node.RawKind == plan.Receiver.RawKind);
        var parameterType = (TypeSyntax)PredefinedType(Token(SyntaxKind.StringKeyword));
        if (plan.Nullable) parameterType = NullableType(parameterType);
        ExpressionSyntax dereference = IdentifierName(source);
        if (plan.Nullable) dereference = PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, dereference);
        var statements = new List<StatementSyntax>
        {
            Declare(length, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, dereference, IdentifierName("Length"))),
            Declare(span, Call(plan.Catalog.FullSpan!, IdentifierName(source), LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0)), IdentifierName(length))),
            Declare(trimmed, Call(plan.SpanTrim, IdentifierName(span))),
        };
        ExpressionSyntax result;
        if (plan.Constant is null) result = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(trimmed), IdentifierName("Length"));
        else
        {
            var expected = names.Fresh("expectedText");
            statements.Add(Declare(expected, Call(plan.Catalog.ConstantSpan!, LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(plan.Constant)))));
            result = Call(plan.Catalog.Equal!, IdentifierName(trimmed), IdentifierName(expected));
            if (plan.Negate) result = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, result);
        }
        statements.Add(ReturnStatement(result));
        var body = ConditionSiteRewrites.AddElasticLineBreaks(Block(statements));
        var returnType = PredefinedType(Token(plan.Constant is null ? SyntaxKind.IntKeyword : SyntaxKind.BoolKeyword));
        var parameters = ParameterList(SingletonSeparatedList(Parameter(Identifier(source)).WithType(parameterType)));
        var call = InvocationExpression(IdentifierName(name), ArgumentList(SingletonSeparatedList(Argument(receiver.WithoutTrivia()))))
            .WithTriviaFrom(inspection).WithAdditionalAnnotations(Selected);
        if (plan.MemberType is not null)
        {
            var declaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First(node => node.Span == plan.MemberType.Span);
            var helper = MethodDeclaration(returnType, name).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(parameters).WithBody(body).WithAdditionalAnnotations(Generated, Formatter.Annotation);
            return root.ReplaceNode(declaration, declaration.ReplaceNode(inspection, call).AddMembers(helper));
        }
        var local = LocalFunctionStatement(returnType, name).WithParameterList(parameters).WithBody(body).WithAdditionalAnnotations(Generated, Formatter.Annotation);
        if (plan.StaticLocal) local = local.WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)));
        if (plan.ExpressionBody is not null)
        {
            var expressionBody = root.DescendantNodes().First(node => node.RawKind == plan.ExpressionBody.RawKind && node.Span == plan.ExpressionBody.Span);
            var enclosing = expressionBody is ArrowExpressionClauseSyntax arrow ? arrow.Expression : (ExpressionSyntax)((LambdaExpressionSyntax)expressionBody).Body;
            var discards = plan.ExpressionBody is LambdaExpressionSyntax lambda
                ? (model.GetTypeInfo(lambda).ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod?.ReturnsVoid == true
                : model.GetDeclaredSymbol(plan.ExpressionBody.Parent!) is IMethodSymbol { ReturnsVoid: true };
            var value = enclosing.ReplaceNode(inspection, call);
            StatementSyntax completion = discards ? ExpressionStatement(value) : ReturnStatement(value);
            return ConditionSiteRewrites.ReplaceExpressionBody(root, expressionBody,
                Block(local, completion).WithAdditionalAnnotations(Formatter.Annotation));
        }
        var statement = (StatementSyntax)root.DescendantNodes().First(node => node.RawKind == plan.Statement!.RawKind && node.Span == plan.Statement.Span);
        return ConditionSiteRewrites.ReplaceStatement(root, statement, new List<StatementSyntax> { local, statement.ReplaceNode(inspection, call) });
    }

    private static LocalDeclarationStatementSyntax Declare(string name, ExpressionSyntax expression) => LocalDeclarationStatement(
        VariableDeclaration(IdentifierName("var"), SingletonSeparatedList(VariableDeclarator(name).WithInitializer(EqualsValueClause(expression)))));
    private static InvocationExpressionSyntax Call(IMethodSymbol method, params ExpressionSyntax[] values)
    {
        SimpleNameSyntax name = method.Arity == 0 ? IdentifierName(method.Name) : GenericName(Identifier(method.Name),
            TypeArgumentList(SingletonSeparatedList<TypeSyntax>(PredefinedType(Token(SyntaxKind.CharKeyword)))));
        var expression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseName("global::System.MemoryExtensions"), name);
        var arguments = values.Select((value, index) => Argument(value).WithNameColon(NameColon(IdentifierName(method.Parameters[index].Name))));
        return InvocationExpression(expression, ArgumentList(SeparatedList(arguments)));
    }
    private static bool Occurrence(SyntaxNode node, SemanticModel model, CancellationToken token) => NamedArgumentSite.IsHost(node)
        || node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax
        || node is MemberAccessExpressionSyntax && model.GetOperation(node, token) is IPropertyReferenceOperation;
    private static bool UneditedBindingsSurvive(Compilation before, Compilation after, SyntaxTree edited, CancellationToken token)
    {
        foreach (var tree in before.SyntaxTrees.Where(tree => tree != edited))
        {
            token.ThrowIfCancellationRequested();
            if (!after.ContainsSyntaxTree(tree)) return false;
            var originalModel = before.GetSemanticModel(tree);
            var savedModel = after.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot(token).DescendantNodes())
            {
                token.ThrowIfCancellationRequested();
                if (Occurrence(node, originalModel, token) && BindingIdentity.Call(originalModel.GetOperation(node, token)) != BindingIdentity.Call(savedModel.GetOperation(node, token))) return false;
                if (node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } }
                    && !originalModel.GetConstantValue(node, token).Equals(savedModel.GetConstantValue(node, token))) return false;
            }
        }
        return true;
    }
    private static string[] CompilerMessages(Compilation compilation, CancellationToken token) => compilation.GetDiagnostics(token)
        .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(Key).OrderBy(value => value).ToArray();
    private static string Key(Diagnostic diagnostic) => diagnostic.Id + ":" + diagnostic.Severity + ":" + diagnostic.GetMessage();
    private static Task<ImmutableArray<Diagnostic>> Diagnostics(Compilation compilation, AnalyzerOptions options, CancellationToken token) =>
        compilation.WithAnalyzers(Older, options).GetAnalyzerDiagnosticsAsync(token);
}

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
        var plan = SpanPlan.Create(trim, model, SpanCatalog.For(model.Compilation), token);
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (plan is null || root is null) return null;
        var before = await Diagnostics(model.Compilation, document.Project.AnalyzerOptions, token).ConfigureAwait(false);
        var occurrences = root.DescendantNodes().Where(node => Occurrence(node, model, token)
            && (!plan.Inspection.Span.Contains(node.Span) || plan.Receiver.Span.Contains(node.Span))).ToArray();
        var bindings = occurrences.Select(node => Binding(model.GetOperation(node, token))).ToArray();
        var unchanged = before.Where(item => item.Location.SourceTree == model.SyntaxTree
            && !item.Location.SourceSpan.IntersectsWith(plan.Inspection.Span)).ToArray();
        var diagnosticNodes = unchanged.Select(item => root.FindNode(item.Location.SourceSpan, getInnermostNodeForTie: true)).ToArray();
        var annotated = root.ReplaceNodes(occurrences.Concat(diagnosticNodes).Distinct(), (node, changed) =>
        {
            var occurrence = System.Array.IndexOf(occurrences, node);
            if (occurrence >= 0) changed = changed.WithAdditionalAnnotations(new SyntaxAnnotation(Original, occurrence.ToString()));
            for (var index = 0; index < diagnosticNodes.Length; index++)
                if (diagnosticNodes[index] == node) changed = changed.WithAdditionalAnnotations(new SyntaxAnnotation(PreviousDiagnostic, index.ToString()));
            return changed;
        });
        var changedRoot = Rewrite(plan, annotated, model);
        var proposed = document.WithSyntaxRoot(changedRoot);
        var options = await document.GetOptionsAsync(token).ConfigureAwait(false);
        var originalText = (await document.GetTextAsync(token).ConfigureAwait(false)).ToString();
        var firstNewline = originalText.IndexOf('\n');
        var newline = firstNewline > 0 && originalText[firstNewline - 1] == '\r' ? "\r\n" : "\n";
        var config = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(trim.SyntaxTree);
        if (config.TryGetValue("end_of_line", out var configured)) newline = configured switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" };
        proposed = await Formatter.FormatAsync(proposed, Formatter.Annotation,
            options.WithChangedOption(FormattingOptions.NewLine, document.Project.Language, newline), token).ConfigureAwait(false);
        var formatted = await proposed.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var text = await proposed.GetTextAsync(token).ConfigureAwait(false);
        proposed = document.WithText(text);
        var after = await proposed.GetSemanticModelAsync(token).ConfigureAwait(false);
        var parsed = await proposed.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (formatted is null || after is null || parsed is null || !CompilerMessages(model, token).SequenceEqual(CompilerMessages(after, token))) return null;
        var seen = new HashSet<int>();
        foreach (var node in formatted.GetAnnotatedNodes(Original))
        {
            var index = int.Parse(node.GetAnnotations(Original).Single().Data!);
            var savedOccurrence = parsed.FindNode(node.Span, getInnermostNodeForTie: true).AncestorsAndSelf()
                .FirstOrDefault(value => value.RawKind == node.RawKind && value.Span == node.Span);
            if (savedOccurrence is null || !seen.Add(index) || Binding(after.GetOperation(savedOccurrence, token)) != bindings[index]) return null;
        }
        if (seen.Count != bindings.Length) return null;
        var selected = formatted.GetAnnotatedNodes(Selected).Single();
        var saved = parsed.FindNode(selected.Span, getInnermostNodeForTie: true);
        if (after.GetOperation(saved, token) is not IInvocationOperation { TargetMethod.IsStatic: true } && plan.StaticLocal
            || NamedArgumentIdentity.Type(model.GetTypeInfo(plan.Inspection, token).Type) != NamedArgumentIdentity.Type(after.GetTypeInfo(saved, token).Type)
            || NamedArgumentIdentity.Type(model.GetTypeInfo(plan.Inspection, token).ConvertedType) != NamedArgumentIdentity.Type(after.GetTypeInfo(saved, token).ConvertedType)
            || OperationEvaluator.ConversionFingerprint(model.GetConversion(plan.Inspection, token)) != OperationEvaluator.ConversionFingerprint(after.GetConversion((ExpressionSyntax)saved, token))) return null;
        var remaining = parsed.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(node => SpanPlan.Create(node, after, SpanCatalog.For(after.Compilation), token) is not null);
        var originalCount = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(node => SpanPlan.Create(node, model, plan.Catalog, token) is not null);
        if (remaining >= originalCount) return null;
        var afterDiagnostics = await Diagnostics(after.Compilation, document.Project.AnalyzerOptions, token).ConfigureAwait(false);
        var counts = before.GroupBy(Key).ToDictionary(group => group.Key, group => group.Count());
        if (afterDiagnostics.GroupBy(Key).Any(group => !counts.TryGetValue(group.Key, out var count) || group.Count() > count)) return null;
        foreach (var node in formatted.GetAnnotatedNodes(PreviousDiagnostic))
            foreach (var annotation in node.GetAnnotations(PreviousDiagnostic))
            {
                var original = unchanged[int.Parse(annotation.Data!)];
                if (!afterDiagnostics.Any(item => item.Location.SourceTree == after.SyntaxTree && item.Location.SourceSpan == node.Span && Key(item) == Key(original))) return null;
            }
        var helper = formatted.GetAnnotatedNodes(Generated).Single();
        if (afterDiagnostics.Any(item => item.Location.SourceTree == after.SyntaxTree && helper.Span.Contains(item.Location.SourceSpan))) return null;
        return proposed;
    }

    private static SyntaxNode Rewrite(SpanPlan plan, SyntaxNode root, SemanticModel model)
    {
        var names = new EvaluationNames(plan.Space, model);
        var name = names.Fresh(plan.Constant is null ? "InspectTextLength" : "InspectTextEquals");
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
    private static string Binding(IOperation? operation)
    {
        if (operation is IAttributeOperation attribute) operation = attribute.Operation;
        var symbol = operation switch { IInvocationOperation call => (ISymbol)call.TargetMethod, IObjectCreationOperation creation => creation.Constructor,
            IPropertyReferenceOperation property => property.Property, _ => null };
        var arguments = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments,
            IPropertyReferenceOperation property => property.Arguments, _ => default };
        var receiver = operation switch { IInvocationOperation call => call.Instance, IPropertyReferenceOperation property => property.Instance, _ => null };
        if (arguments.IsDefault) return NamedArgumentIdentity.Operation(operation);
        return NamedArgumentIdentity.Symbol(symbol) + ":" + NamedArgumentIdentity.Type(receiver?.Type)
            + ":" + (operation is IInvocationOperation invocation && invocation.IsVirtual) + ":"
            + string.Join(";", arguments.Select(argument => NamedArgumentIdentity.Symbol(argument.Parameter) + ":" + argument.ArgumentKind
                + ":" + NamedArgumentIdentity.Conversion(argument.InConversion) + ":" + NamedArgumentIdentity.Conversion(argument.OutConversion)
                + ":" + NamedArgumentIdentity.Type(argument.Value.Type) + ":" + argument.Value.ConstantValue));
    }
    private static string[] CompilerMessages(SemanticModel model, CancellationToken token) => model.GetDiagnostics(cancellationToken: token)
        .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(Key).OrderBy(value => value).ToArray();
    private static string Key(Diagnostic diagnostic) => diagnostic.Id + ":" + diagnostic.Severity + ":" + diagnostic.GetMessage();
    private static Task<ImmutableArray<Diagnostic>> Diagnostics(Compilation compilation, AnalyzerOptions options, CancellationToken token) =>
        compilation.WithAnalyzers(Older, options).GetAnalyzerDiagnosticsAsync(token);
}

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DecisionValueCodeFixProvider)), Shared]
public sealed class DecisionValueCodeFixProvider : CodeFixProvider
{
    private const string Generated = "DecisionValueFunction";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.DecisionValueFunction);

    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(async (context, document, diagnostics) =>
    {
        foreach (var diagnostic in diagnostics.OrderByDescending(item => item.Location.SourceSpan.Start))
        {
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var declaration = root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            if (declaration is not null)
            {
                document = await ApplyAsync(document, declaration, context.CancellationToken).ConfigureAwait(false);
            }
        }
        return document;
    });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var declaration = root?.FindToken(context.Span.Start).Parent?.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
        if (declaration is null) return;
        var changed = await ApplyAsync(context.Document, declaration, context.CancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(changed, context.Document)) return;
        context.RegisterCodeFix(CodeAction.Create("Extract the decision to a local function",
            _ => Task.FromResult(changed), DiagnosticIds.DecisionValueFunction), context.Diagnostics);
    }

    private static async Task<Document> ApplyAsync(Document document, LocalDeclarationStatementSyntax declaration, CancellationToken token)
    {
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (model is null || !DecisionValueAnalyzer.TryGetDecision(declaration, model, out var symbol, out var decision)
            || CallerInformation.AffectedDefaults(decision, declaration, model)) return document;
        var block = (BlockSyntax)declaration.Parent!;
        var space = declaration.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax) ?? block;
        var names = new EvaluationNames(space, model);
        var stem = "Calculate" + char.ToUpperInvariant(symbol.Name[0]) + symbol.Name.Substring(1);
        var name = names.Fresh(stem);
        var marker = new SyntaxAnnotation(Generated);
        var helper = SyntaxFactory.LocalFunctionStatement(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), name)
            .WithBody(SyntaxFactory.Block(WithReturns(decision).WithoutTrailingTrivia()))
            .WithLeadingTrivia(declaration.GetLeadingTrivia())
            .WithAdditionalAnnotations(marker, Formatter.Annotation);
        var variable = declaration.Declaration.Variables[0].WithInitializer(SyntaxFactory.EqualsValueClause(
            SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(name), SyntaxFactory.ArgumentList())));
        var initialized = declaration.WithDeclaration(declaration.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)))
            .WithoutLeadingTrivia().WithTrailingTrivia(declaration.GetTrailingTrivia().AddRange(decision.GetTrailingTrivia()))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var index = block.Statements.IndexOf(declaration);
        var statements = block.Statements.RemoveAt(index + 1).RemoveAt(index).InsertRange(index,
            new StatementSyntax[] { helper, initialized });
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var changed = document.WithSyntaxRoot(root.ReplaceNode(block, block.WithStatements(statements)));
        changed = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: token).ConfigureAwait(false);
        var changedRoot = (await changed.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var helperSpan = changedRoot.GetAnnotatedNodes(marker).Single().Span;
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)model.SyntaxTree.Options, model.SyntaxTree.FilePath, token);
        var saved = model.Compilation.ReplaceSyntaxTree(model.SyntaxTree, tree).GetSemanticModel(tree);
        var before = CompilerMessages(model, token);
        if (CompilerMessages(saved, token).Any(pair => !before.TryGetValue(pair.Key, out var count) || pair.Value > count)) return document;
        var parsedHelper = tree.GetRoot(token).FindNode(helperSpan, getInnermostNodeForTie: true);
        if (!Bindings(decision, model, token).SequenceEqual(Bindings(parsedHelper, saved, token))) return document;
        return changed;
    }

    private static StatementSyntax WithReturns(StatementSyntax statement) => statement switch
    {
        BlockSyntax block => block.WithStatements(SyntaxFactory.SingletonList(WithReturns(block.Statements[0]))),
        IfStatementSyntax decision => decision.WithStatement(WithReturns(decision.Statement))
            .WithElse(decision.Else!.WithStatement(WithReturns(decision.Else.Statement))),
        ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } value =>
            SyntaxFactory.ReturnStatement(assignment.Right).WithTriviaFrom(value),
        _ => throw new InvalidOperationException("The decision shape must be validated before rewriting."),
    };

    private static Dictionary<string, int> CompilerMessages(SemanticModel model, CancellationToken token) =>
        model.GetDiagnostics(cancellationToken: token)
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .GroupBy(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage())
            .ToDictionary(group => group.Key, group => group.Count());

    private static IEnumerable<string> Bindings(SyntaxNode root, SemanticModel model, CancellationToken token) =>
        root.DescendantNodesAndSelf().Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
            .Select(node => model.GetOperation(node, token) switch
            {
                IInvocationOperation call => call.TargetMethod.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IObjectCreationOperation creation => creation.Constructor?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
                _ => "",
            }).OrderBy(value => value, StringComparer.Ordinal);
}

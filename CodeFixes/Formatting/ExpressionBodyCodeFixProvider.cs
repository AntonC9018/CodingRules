using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

// Independently implemented for the packaged multiline-expression policy.
// Do not ship the upstream common fixer alongside this provider: CLI hosts can
// omit suppressors and would still offer its unsafe RCS1016 conversions.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExpressionBodyCodeFixProvider)), Shared]
public sealed class ExpressionBodyCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("RCS1016");

    // A CLI can supply no equivalence key when its first diagnostic is unsafe.
    // Process each document directly so that safe diagnostics are still fixed.
    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(async (context, document, diagnostics) =>
    {
        foreach (var diagnostic in diagnostics.OrderByDescending(item => item.Location.SourceSpan.Start))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            document = await ChangeAsync(document, diagnostic.Location.SourceSpan.Start, context.CancellationToken)
                .ConfigureAwait(false);
        }
        return await Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);
    });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var changed = await ChangeAsync(context.Document, context.Span.Start, context.CancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(changed, context.Document)) return;
        context.RegisterCodeFix(CodeAction.Create("Use block body",
            cancellationToken => Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken),
            "RCS1016.UseBlockBody"), context.Diagnostics);
    }

    private static async Task<Document> ChangeAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;
        var arrow = root.FindToken(position).Parent as ArrowExpressionClauseSyntax;
        if (arrow?.Parent is not { } declaration) return document;
        if (declaration.ContainsDiagnostics || arrow.ContainsDirectives) return document;
        if (UnsafeExpressionBodySuppressor.HasUnsafeArrowTrivia(arrow)) return document;
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null) return document;
        if (UnsafeExpressionBodySuppressor.HasUnsupportedAsyncReturn(arrow, model, cancellationToken)) return document;
        var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
        if (symbol is null) return document;
        var semicolon = declaration.GetLastToken();
        if (!semicolon.IsKind(SyntaxKind.SemicolonToken)) return document;

        var statement = CreateStatement(arrow.Expression, symbol, semicolon);
        var block = SyntaxFactory.Block(statement);
        SyntaxNode? replacement = declaration switch
        {
            MethodDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            LocalFunctionStatementSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            AccessorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            ConstructorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            DestructorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            OperatorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            ConversionOperatorDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            PropertyDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(Getter(block)),
            IndexerDeclarationSyntax node => node.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(Getter(block)),
            _ => null,
        };
        if (replacement is null) return document;
        var changedRoot = root.ReplaceNode(declaration, replacement.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(changedRoot);
    }

    private static StatementSyntax CreateStatement(ExpressionSyntax expression, ISymbol symbol, SyntaxToken semicolon)
    {
        if (expression is ThrowExpressionSyntax throwing)
            return SyntaxFactory.ThrowStatement(throwing.Expression).WithThrowKeyword(throwing.ThrowKeyword)
                .WithSemicolonToken(semicolon);
        if (symbol is IMethodSymbol method)
        {
            if (method.ReturnsVoid) return SyntaxFactory.ExpressionStatement(expression).WithSemicolonToken(semicolon);
            // The shared safety check limits non-void async returns to the four
            // framework Task/ValueTask contracts; only the generic pair returns a value.
            if (method.IsAsync && method.ReturnType is INamedTypeSymbol { Arity: 0 })
                return SyntaxFactory.ExpressionStatement(expression).WithSemicolonToken(semicolon);
        }
        return SyntaxFactory.ReturnStatement(expression).WithSemicolonToken(semicolon);
    }

    private static AccessorListSyntax Getter(BlockSyntax block) => SyntaxFactory.AccessorList(
        SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(block)));
}

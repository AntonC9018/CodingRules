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

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        var arrow = root.FindNode(context.Span).AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (arrow?.Parent is not { } declaration) return;
        if (declaration.ContainsDiagnostics || arrow.ContainsDirectives) return;
        if (UnsafeExpressionBodySuppressor.HasUnsafeArrowTrivia(arrow)) return;
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null) return;
        if (UnsafeExpressionBodySuppressor.HasUnsupportedAsyncReturn(arrow, model, context.CancellationToken)) return;
        var symbol = model.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (symbol is null) return;
        var semicolon = declaration.GetLastToken();
        if (!semicolon.IsKind(SyntaxKind.SemicolonToken)) return;

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
        if (replacement is null) return;
        var changedRoot = root.ReplaceNode(declaration, replacement.WithAdditionalAnnotations(Formatter.Annotation));
        context.RegisterCodeFix(CodeAction.Create("Use block body",
            cancellationToken => Formatter.FormatAsync(context.Document.WithSyntaxRoot(changedRoot),
                Formatter.Annotation, cancellationToken: cancellationToken), "RCS1016.UseBlockBody"), context.Diagnostics);
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

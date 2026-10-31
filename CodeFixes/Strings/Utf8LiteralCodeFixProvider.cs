using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

/// <summary>
/// Replaces <c>Encoding.UTF8.GetBytes("literal")</c> with <c>"literal"u8</c> where a
/// <c>ReadOnlySpan&lt;byte&gt;</c> is accepted. Elsewhere the call keeps receiving a
/// fresh array from <c>"literal"u8.ToArray()</c>, declared before its statement:
/// a returned array is named <c>ret</c>, any other use reads a <c>TODO_VARIABLE</c>.
/// The copy never appears inline at the use site.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Utf8LiteralCodeFixProvider)), Shared]
public sealed class Utf8LiteralCodeFixProvider : CodeFixProvider
{
    public const string Key = "Strings.Utf8Literal";
    public const string Title = "Use a UTF-8 string literal";
    private const string ReturnedName = "ret";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.Utf8StringLiteral);

    public override FixAllProvider GetFixAllProvider() => new SequentialFixAllProvider(Title, Key,
        DiagnosticIds.Utf8StringLiteral, () => new Utf8LiteralAnalyzer(), Change);

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var changed = await Change(context.Document, diagnostic, context.CancellationToken).ConfigureAwait(false);
        if (changed is not null)
        {
            context.RegisterCodeFix(CodeAction.Create(Title, _ => Task.FromResult(changed), Key), diagnostic);
        }
    }

    internal static async Task<Document?> Change(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (root is null || model is null || diagnostic.Location.SourceSpan.End > root.FullSpan.End
            || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax call
            || Utf8LiteralFacts.ReportedLiteral(call, model) is not { } literal)
        {
            return null;
        }

        // Replacing the call changes caller-argument text; moving code also shifts later lines.
        var rewrite = Rewrite(root, model, call, literal);
        if (rewrite is not ({ } changedRoot, var insertion)
            || (insertion is null
                ? call.Ancestors().Where(CallerInformation.IsCall).Any(outer => CallerInformation.HasDefaults(outer, model))
                : CallerInformation.AffectedDefaults(call, insertion, model)))
        {
            return null;
        }

        var changed = await GeneratedSyntaxFormatter.FormatAsync(document.WithSyntaxRoot(changedRoot), document, token)
            .ConfigureAwait(false);
        var saved = SavedSource.Compile(model, await changed.GetTextAsync(token).ConfigureAwait(false), token);
        return SavedSource.AddsCompilerMessages(model, saved, token) ? null : changed;
    }

    // The rewritten root and the first node whose line the rewrite can shift, if any.
    private static (SyntaxNode Root, SyntaxNode? Insertion)? Rewrite(SyntaxNode root, SemanticModel model,
        InvocationExpressionSyntax call, LiteralExpressionSyntax literal)
    {
        var bytes = SyntaxFactory.ParseExpression(literal.Token.Text + "u8");
        if (Utf8LiteralFacts.AcceptsSpan(call, model))
        {
            return (root.ReplaceNode(call, bytes.WithTriviaFrom(call)), null);
        }

        var copy = SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression, bytes, SyntaxFactory.IdentifierName("ToArray")));
        var value = Utf8LiteralFacts.Value(call);
        if (value.Parent is ArrowExpressionClauseSyntax arrow)
        {
            return ReturningMember(arrow, model) is { } member
                ? (root.ReplaceNode(member, WithReturnedBlock(member, arrow, Names(member, model).Fresh(ReturnedName), copy)), member)
                : null;
        }

        if (HoistingStatement(call) is not { } statement)
        {
            return null;
        }

        var names = Names(statement, model);
        var name = value.Parent is ReturnStatementSyntax ? names.Fresh(ReturnedName) : names.Fresh(Placeholder.Variable);
        return (Hoist(root, call, statement, name, copy), statement);
    }

    private static EvaluationNames Names(SyntaxNode site, SemanticModel model) =>
        new(site.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() ?? site, model);

    // A member or accessor whose expression body is its returned value.
    private static SyntaxNode? ReturningMember(ArrowExpressionClauseSyntax arrow, SemanticModel model)
    {
        var member = arrow.Parent;
        var returns = member switch
        {
            PropertyDeclarationSyntax or IndexerDeclarationSyntax => true,
            BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax =>
                model.GetDeclaredSymbol(member!) is IMethodSymbol { ReturnsVoid: false },
            _ => false,
        };
        return returns ? member : null;
    }

    private static SyntaxNode WithReturnedBlock(SyntaxNode member, ArrowExpressionClauseSyntax arrow, string name, ExpressionSyntax copy)
    {
        var semicolon = member.ChildTokens().Last(token => token.IsKind(SyntaxKind.SemicolonToken));
        var block = SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"byte[] {name} = {copy.ToFullString()};"),
                SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(name)))
            .WithTrailingTrivia(semicolon.TrailingTrivia)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var beforeArrow = arrow.GetFirstToken().GetPreviousToken();
        var trimmed = member.ReplaceToken(beforeArrow, beforeArrow.WithTrailingTrivia(
            beforeArrow.TrailingTrivia.Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia))));
        return trimmed switch
        {
            PropertyDeclarationSyntax property => property.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(Getter(block)),
            IndexerDeclarationSyntax indexer => indexer.WithExpressionBody(null).WithSemicolonToken(default)
                .WithAccessorList(Getter(block)),
            BaseMethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            LocalFunctionStatementSyntax function => function.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
            _ => member,
        };
    }

    private static AccessorListSyntax Getter(BlockSyntax block) => SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, block.WithoutTrailingTrivia())))
        .WithTrailingTrivia(block.GetTrailingTrivia())
        .WithAdditionalAnnotations(Formatter.Annotation);

    // A block statement that evaluates the call at most once each time it runs,
    // so one array declared before it is never shared by two evaluations.
    private static StatementSyntax? HoistingStatement(InvocationExpressionSyntax call)
    {
        var statement = call.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is not (ExpressionStatementSyntax or LocalDeclarationStatementSyntax or ReturnStatementSyntax
                or ThrowStatementSyntax or IfStatementSyntax or SwitchStatementSyntax)
            || statement.Parent is not (BlockSyntax or SwitchSectionSyntax))
        {
            return null;
        }

        var repeated = call.Ancestors().TakeWhile(node => node != statement)
            .Any(node => node is AnonymousFunctionExpressionSyntax or QueryExpressionSyntax);
        return repeated ? null : statement;
    }

    private static SyntaxNode Hoist(SyntaxNode root, InvocationExpressionSyntax call, StatementSyntax statement,
        string name, ExpressionSyntax copy)
    {
        var leading = statement.GetLeadingTrivia();
        var indentation = ConditionSiteRewrites.Indentation(leading);
        var lineBreak = statement.GetTrailingTrivia().LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        var separator = lineBreak.IsKind(SyntaxKind.EndOfLineTrivia) ? lineBreak : SyntaxFactory.Space;
        var declaration = SyntaxFactory.ParseStatement($"byte[] {name} = {copy.ToFullString()};")
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(separator);
        var use = statement.ReplaceNode(call, SyntaxFactory.IdentifierName(name).WithTriviaFrom(call))
            .WithLeadingTrivia(indentation);
        return root.ReplaceNode(statement, new[] { declaration, use });
    }
}

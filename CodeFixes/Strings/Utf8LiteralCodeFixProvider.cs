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

namespace CodingRules;

/// <summary>
/// Replaces <c>Encoding.UTF8.GetBytes("literal")</c> with <c>"literal"u8</c> where a
/// <c>ReadOnlySpan&lt;byte&gt;</c> is accepted. Elsewhere the call keeps receiving a
/// fresh array: <c>"literal"u8.ToArray()</c> replaces a whole assigned or returned
/// value, and any other use reads a <c>TODO_VARIABLE</c> declared before its statement.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Utf8LiteralCodeFixProvider)), Shared]
public sealed class Utf8LiteralCodeFixProvider : CodeFixProvider
{
    public const string Key = "Strings.Utf8Literal";
    public const string Title = "Use a UTF-8 string literal";

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
            || Utf8LiteralFacts.EncodedLiteral(call, model) is not { } literal)
        {
            return null;
        }

        var bytes = SyntaxFactory.ParseExpression(literal.Token.Text + "u8");
        if (AcceptsSpan(call, model))
        {
            return document.WithSyntaxRoot(root.ReplaceNode(call, bytes.WithTriviaFrom(call)));
        }

        var copy = SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression, bytes, SyntaxFactory.IdentifierName("ToArray")));
        if (IsWholeValue(call))
        {
            return document.WithSyntaxRoot(root.ReplaceNode(call, copy.WithTriviaFrom(call)));
        }

        return HoistingStatement(call) is { } statement
            ? document.WithSyntaxRoot(Hoist(root, model, call, statement, copy))
            : null;
    }

    private static bool AcceptsSpan(InvocationExpressionSyntax call, SemanticModel model) =>
        model.GetTypeInfo(call).ConvertedType is INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Byte }] } type
        && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            model.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1"));

    // The array is the whole value an initializer, assignment or return produces.
    private static bool IsWholeValue(InvocationExpressionSyntax call)
    {
        SyntaxNode value = call;
        while (value.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            value = parentheses;
        }

        return value.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax } => true,
            AssignmentExpressionSyntax assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == value,
            ReturnStatementSyntax or ArrowExpressionClauseSyntax => true,
            _ => false,
        };
    }

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

    private static SyntaxNode Hoist(SyntaxNode root, SemanticModel model, InvocationExpressionSyntax call,
        StatementSyntax statement, ExpressionSyntax copy)
    {
        SyntaxNode space = statement.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() ?? (SyntaxNode)statement;
        var name = new EvaluationNames(space, model).Fresh(Placeholder.Variable);
        var leading = statement.GetLeadingTrivia();
        var indentation = SyntaxFactory.TriviaList(leading.Reverse().TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());
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

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class NamedArgumentSite
{
    public SyntaxNode Owner { get; }
    public SyntaxNode List { get; }
    public IOperation Operation { get; }
    public IMethodSymbol Method { get; }
    public ImmutableArray<SyntaxNode> Arguments { get; }
    public ImmutableArray<IArgumentOperation> BoundArguments { get; }
    private NamedArgumentSite(SyntaxNode owner, SyntaxNode list, IOperation operation, IMethodSymbol method,
        ImmutableArray<SyntaxNode> arguments, ImmutableArray<IArgumentOperation> bound)
    {
        Owner = owner;
        List = list;
        Operation = operation;
        Method = method;
        Arguments = arguments;
        BoundArguments = bound;
    }

    public static NamedArgumentSite? Create(SyntaxNode owner, SemanticModel model, CancellationToken token)
    {
        var list = owner switch
        {
            InvocationExpressionSyntax call => (SyntaxNode)call.ArgumentList,
            BaseObjectCreationExpressionSyntax creation => creation.ArgumentList,
            ConstructorInitializerSyntax initializer => initializer.ArgumentList,
            PrimaryConstructorBaseTypeSyntax primary => primary.ArgumentList,
            AttributeSyntax attribute => attribute.ArgumentList,
            _ => null,
        };
        if (list is null) return null;
        var operation = model.GetOperation(owner, token);
        if (operation is IAttributeOperation attributeOperation) operation = attributeOperation.Operation;
        var method = operation switch { IInvocationOperation call => call.TargetMethod, IObjectCreationOperation creation => creation.Constructor, _ => null };
        var bound = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments, _ => default };
        if (method is null || bound.IsDefault || operation!.DescendantsAndSelf().Any(value => value is IInvalidOperation)) return null;
        var arguments = list.ChildNodes().Where(node => node is ArgumentSyntax || node is AttributeArgumentSyntax { NameEquals: null }).ToImmutableArray();
        var mapped = ImmutableArray.CreateBuilder<IArgumentOperation>();
        foreach (var argument in arguments)
        {
            token.ThrowIfCancellationRequested();
            var expression = Expression(argument);
            var match = bound.Where(value => !value.IsImplicit && value.Syntax == argument
                || value.Value.DescendantsAndSelf().Any(child => child.Syntax == expression)).ToArray();
            if (match.Length != 1 || match[0].Parameter is null) return null;
            mapped.Add(match[0]);
        }
        return new NamedArgumentSite(owner, list, operation!, method, arguments, mapped.ToImmutable());
    }

    public static ExpressionSyntax Expression(SyntaxNode argument) => argument is ArgumentSyntax ordinary ? ordinary.Expression : ((AttributeArgumentSyntax)argument).Expression;
    public static bool Named(SyntaxNode argument) => argument is ArgumentSyntax ordinary ? ordinary.NameColon is not null : ((AttributeArgumentSyntax)argument).NameColon is not null;
    public static SyntaxNode Name(SyntaxNode argument, string name)
    {
        var escaped = SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
        var colon = SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(escaped)).WithColonToken(
            SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space));
        // Argument leading trivia belongs before the new name, while all expression
        // trivia remains exactly where the author put it.
        return argument is ArgumentSyntax ordinary ? ordinary.WithNameColon(colon) : ((AttributeArgumentSyntax)argument).WithNameColon(colon);
    }

    public static bool IsHost(SyntaxNode node) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
        or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or AttributeSyntax;
}

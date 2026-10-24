using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>Conservative eager rendering of immutable, already evaluated context.</summary>
internal sealed class ExceptionContextPlan
{
    public BaseObjectCreationExpressionSyntax Creation { get; }
    public ClassDeclarationSyntax Container { get; }
    public string Name { get; }
    public MemberDeclarationSyntax Declaration { get; }
    public ExpressionSyntax Replacement { get; }

    private ExceptionContextPlan(BaseObjectCreationExpressionSyntax creation, ClassDeclarationSyntax container,
        string name, MemberDeclarationSyntax declaration, ExpressionSyntax replacement)
    {
        Creation = creation;
        Container = container;
        Name = name;
        Declaration = declaration;
        Replacement = replacement;
    }

    public static ExceptionContextPlan? Create(InterpolatedStringExpressionSyntax message, SemanticModel model)
    {
        if (!ExceptionContextAnalyzer.IsMessage(message, model)
            || message.Parent?.Parent?.Parent is not BaseObjectCreationExpressionSyntax creation
            || creation.ArgumentList?.Arguments.Count != 1 || creation.Initializer is not null
            || message.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is not { } container
            || model.GetSymbolInfo(creation).Symbol is not IMethodSymbol { Parameters.Length: 1 } constructor
            || constructor.Parameters[0].Type.SpecialType != SpecialType.System_String
            || constructor.ContainingType is not { IsSealed: false, ContainingNamespace: { } ns } exception
            || ns.ToDisplayString() != "System"
            || exception.ContainingAssembly.Name is not ("mscorlib" or "System.Private.CoreLib" or "System.Runtime" or "netstandard")) return null;
        if (CallerInformation.AffectedDefaults(creation, container, model)) return null;
        var names = new EvaluationNames(container, model);
        var words = new string(message.Contents.OfType<InterpolatedStringTextSyntax>().FirstOrDefault()?.TextToken.ValueText
            .TakeWhile(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)).ToArray() ?? []);
        var stem = string.Concat(words.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Take(4)
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
        if (stem.Length == 0 || !SyntaxFacts.IsValidIdentifier(stem)) stem = "Contextual";
        var name = names.Fresh(stem + "Exception");
        var fields = new List<(ISymbol Symbol, string Parameter, string Property, string Type, ExpressionSyntax Value)>();
        var replacements = new Dictionary<ExpressionSyntax, ExpressionSyntax>();
        var reserved = new HashSet<string> { name };
        for (var current = exception; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers()) reserved.Add(member.Name);
        foreach (var hole in message.Contents.OfType<InterpolationSyntax>())
        {
            // Evaluating arbitrary expressions first could change formatting order,
            // exceptions, or mutable-object observations. Leave those cases manual.
            if (hole.Expression is not IdentifierNameSyntax identifier
                || model.GetSymbolInfo(identifier).Symbol is not { } symbol
                || symbol is not (ILocalSymbol or IParameterSymbol)
                || model.GetTypeInfo(identifier).Type is not { } type || !SnapshotSafe(type)) return null;
            var index = fields.FindIndex(field => SymbolEqualityComparer.Default.Equals(field.Symbol, symbol));
            if (index < 0)
            {
                var parameter = identifier.Identifier.Text;
                var propertyStem = char.ToUpperInvariant(identifier.Identifier.ValueText[0]) + identifier.Identifier.ValueText.Substring(1);
                var property = propertyStem;
                for (var suffix = 2; !reserved.Add(property); suffix++) property = propertyStem + suffix;
                fields.Add((symbol, parameter, property, EvaluationSyntax.Type(type).ToString(), identifier.WithoutTrivia()));
                index = fields.Count - 1;
            }
            replacements.Add(identifier, SyntaxFactory.IdentifierName(fields[index].Parameter));
        }
        var rendered = message.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]).WithoutTrivia().ToFullString();
        var parameters = string.Join(", ", fields.Select(field => field.Type + " " + field.Parameter));
        var properties = string.Join("\n", fields.Select(field => "public " + field.Type + " " + field.Property + " { get; }"));
        var assignments = string.Join("\n", fields.Select(field => field.Property + " = " + field.Parameter + ";"));
        var declaration = SyntaxFactory.ParseMemberDeclaration("internal sealed class " + name + " : "
            + exception.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " + properties
            + " public " + name + "(" + parameters + ") : base(" + rendered + ") { " + assignments + " } }");
        if (declaration is null || declaration.ContainsDiagnostics) return null;
        var replacement = SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName(name))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(fields.Select(field => SyntaxFactory.Argument(field.Value)))))
            .WithTriviaFrom(creation);
        return new ExceptionContextPlan(creation, container, name, declaration, replacement);
    }

    private static bool SnapshotSafe(ITypeSymbol type) => type.SpecialType is SpecialType.System_String or SpecialType.System_Char
        or SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
        or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
        or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
        || type.TypeKind == TypeKind.Enum;
}

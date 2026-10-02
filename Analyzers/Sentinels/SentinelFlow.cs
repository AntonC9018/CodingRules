using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

[Flags]
internal enum SearchOutcomes { Unknown = 0, Success = 1, Sentinel = 2, Both = 3 }
internal sealed class SearchValue
{
    public SearchValue(SyntaxNode origin, SearchOutcomes outcomes, bool helper) { Origin = origin; Outcomes = outcomes; Helper = helper; }
    public SyntaxNode Origin { get; }
    public SearchOutcomes Outcomes { get; }
    public bool Helper { get; }
    public SearchValue Refine(SearchOutcomes outcomes) => new(Origin, Outcomes & outcomes, Helper);
}
internal sealed class SentinelReturn
{
    public SentinelReturn(ExpressionSyntax expression, SearchValue? value) { Expression = expression; Value = value; }
    public ExpressionSyntax Expression { get; }
    public SearchValue? Value { get; }
}
internal sealed class SentinelResult
{
    public SentinelResult(SearchOutcomes outcomes, ImmutableArray<SentinelReturn> returns) { Outcomes = outcomes; Returns = returns; }
    public SearchOutcomes Outcomes { get; }
    public ImmutableArray<SentinelReturn> Returns { get; }
    public static readonly SentinelResult Unknown = new(SearchOutcomes.Unknown, ImmutableArray<SentinelReturn>.Empty);
}

// This cache lives only in one compilation-start closure. Each uncached walk
// has its own recursion set and deterministic limits (32 callees / 4000 nodes).
// Concurrent walks compute independently; cancellation is never cached.
internal sealed class SentinelSummaries
{
    private readonly Compilation compilation;
    private readonly ConcurrentDictionary<IMethodSymbol, SentinelResult> summaries = new(SymbolEqualityComparer.Default);
    public SentinelSummaries(Compilation compilation) { this.compilation = compilation; }
    public SentinelResult Get(SentinelHost host, SemanticModel model, CancellationToken token)
    {
        var key = Normalize(host.Symbol);
        if (summaries.TryGetValue(key, out var result)) return result;
        result = Walk(host, model, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), 0, token);
        token.ThrowIfCancellationRequested();
        return summaries.GetOrAdd(key, result);
    }
    private SentinelResult Walk(SentinelHost host, SemanticModel model, HashSet<IMethodSymbol> visiting, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var symbol = Normalize(host.Symbol);
        if (depth >= 32 || !visiting.Add(symbol)) return SentinelResult.Unknown;
        try
        {
            if (host.Body.DescendantNodes().Take(4001).Count() > 4000) return SentinelResult.Unknown;
            if (host.Imposed(model, token) || SentinelHost.External(symbol)) return SentinelResult.Unknown;
            if (SentinelSearch.Loop(host, model, token)) return new SentinelResult(SearchOutcomes.Both, ImmutableArray<SentinelReturn>.Empty);
            return new SentinelFlow(host, model, token, method =>
            {
                method = Normalize(method);
                if (method.IsVirtual || method.IsAbstract || method.IsOverride
                    || method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction)
                    || method.DeclaredAccessibility != Accessibility.Private && method.MethodKind != MethodKind.LocalFunction
                    || method.DeclaringSyntaxReferences.Length == 0) return SearchOutcomes.Unknown;
                var syntax = method.DeclaringSyntaxReferences[0].GetSyntax(token);
                var calleeModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var callee = SentinelHost.Create(syntax, calleeModel, token);
                return callee is null ? SearchOutcomes.Unknown : Walk(callee, calleeModel, visiting, depth + 1, token).Outcomes;
            }).Analyze();
        }
        finally { visiting.Remove(symbol); }
    }
    private static IMethodSymbol Normalize(IMethodSymbol symbol) => (symbol.PartialImplementationPart ?? symbol.ReducedFrom ?? symbol).OriginalDefinition;
}

internal sealed class SentinelFlow
{
    private readonly SentinelHost host;
    private readonly SemanticModel model;
    private readonly CancellationToken token;
    private readonly Func<IMethodSymbol, SearchOutcomes> helper;
    private readonly List<SentinelReturn> returns = new();
    private readonly HashSet<ISymbol> unstable = new(SymbolEqualityComparer.Default);
    private int work;
    private bool unsupported;
    private sealed class State : Dictionary<ISymbol, SearchValue>
    {
        public State() : base(SymbolEqualityComparer.Default) { }
        public State Copy() { var copy = new State(); foreach (var pair in this) copy.Add(pair.Key, pair.Value); return copy; }
    }
    public SentinelFlow(SentinelHost host, SemanticModel model, CancellationToken token, Func<IMethodSymbol, SearchOutcomes> helper)
    { this.host = host; this.model = model; this.token = token; this.helper = helper; }
    public SentinelResult Analyze()
    {
        foreach (var node in host.Body.DescendantNodes())
        {
            if (++work > 4000) return SentinelResult.Unknown;
            token.ThrowIfCancellationRequested();
            if (node is ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument
                && SentinelSearch.Variable(model.GetOperation(argument.Expression, token)) is { } escaped) unstable.Add(escaped);
            if (node is IdentifierNameSyntax identifier && node.Ancestors().TakeWhile(parent => parent != host.Body).Any(SentinelHost.Nested)
                && model.GetSymbolInfo(identifier, token).Symbol is ILocalSymbol captured) unstable.Add(captured);
        }
        var state = new State();
        if (host.Body is BlockSyntax block) Statement(block, state);
        else if (host.Body is ExpressionSyntax expression) Record(expression, state);
        if (unsupported) return SentinelResult.Unknown;
        var outcomes = returns.Count == 0 || returns.Any(item => item.Value is null) ? SearchOutcomes.Unknown
            : returns.Aggregate(SearchOutcomes.Unknown, (value, item) => value | item.Value!.Outcomes);
        return new SentinelResult(outcomes, returns.ToImmutableArray());
    }

    private State? Statement(StatementSyntax statement, State state)
    {
        token.ThrowIfCancellationRequested();
        if (++work > 4000) { unsupported = true; return null; }
        switch (statement)
        {
            case BlockSyntax block:
                State? next = state;
                foreach (var item in block.Statements) { if (next is null) break; next = Statement(item, next); }
                return next;
            case LocalFunctionStatementSyntax: return state;
            case ReturnStatementSyntax { Expression: { } expression }:
                Record(expression, state); return null;
            case ThrowStatementSyntax: return null;
            case LocalDeclarationStatementSyntax declaration when declaration.UsingKeyword.RawKind == 0 && declaration.AwaitKeyword.RawKind == 0:
                foreach (var variable in declaration.Declaration.Variables)
                {
                    var symbol = model.GetDeclaredSymbol(variable, token);
                    var value = variable.Initializer is null ? null : Value(variable.Initializer.Value, state);
                    Effects(variable.Initializer?.Value, state);
                    Set(state, symbol, value);
                }
                return state;
            case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                var assigned = Value(assignment.Right, state);
                Effects(assignment.Right, state);
                Set(state, model.GetSymbolInfo(assignment.Left, token).Symbol, assigned); return state;
            case ExpressionStatementSyntax expressionStatement:
                Effects(expressionStatement.Expression, state); return state;
            case IfStatementSyntax branch:
                var condition = Test(branch.Condition, state);
                Effects(branch.Condition, state);
                var yes = state.Copy(); var no = state.Copy();
                if (condition is { } known) { Refine(yes, known.Value, known.True); Refine(no, known.Value, SearchOutcomes.Both ^ known.True); }
                var trueEnd = Statement(branch.Statement, yes);
                var falseEnd = branch.Else is null ? no : Statement(branch.Else.Statement, no);
                return Merge(trueEnd, falseEnd);
            case CheckedStatementSyntax checkedStatement: return Statement(checkedStatement.Block, state);
            case EmptyStatementSyntax: return state;
            default: unsupported = true; return null;
        }
    }
    private void Record(ExpressionSyntax expression, State state)
    {
        var value = Value(expression, state);
        if (value is null && Constant(expression) == -1 && state.Values.Any(item => item.Outcomes == SearchOutcomes.Sentinel))
            value = new SearchValue(expression, SearchOutcomes.Sentinel, false);
        if (value?.Outcomes == SearchOutcomes.Unknown) return; // unreachable refined arm
        returns.Add(new SentinelReturn(expression, value));
    }
    private SearchValue? Value(ExpressionSyntax expression, State state)
    {
        var operation = SentinelSearch.Operation(expression, model, token);
        if (operation?.Type?.SpecialType != SpecialType.System_Int32) return null;
        if (operation is ILocalReferenceOperation local) return state.TryGetValue(local.Local, out var saved) ? saved : null;
        if (operation is not IInvocationOperation { IsVirtual: false } call) return null;
        var type = model.GetTypeInfo(expression, token);
        if (!SymbolEqualityComparer.Default.Equals(type.Type, type.ConvertedType)) return null;
        if (SentinelSearch.Catalogue(call.TargetMethod, model.Compilation)) return new SearchValue(expression, SearchOutcomes.Both, false);
        var result = helper(call.TargetMethod);
        return result == SearchOutcomes.Unknown ? null : new SearchValue(expression, result, true);
    }
    private (SearchValue Value, SearchOutcomes True)? Test(ExpressionSyntax expression, State state)
    {
        var operation = SentinelSearch.Operation(expression, model, token);
        if (operation is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null, Operand.Syntax: ExpressionSyntax operand })
        {
            var inner = Test(operand, state);
            return inner is { } negated ? (negated.Value, SearchOutcomes.Both ^ negated.True) : null;
        }
        if (operation is not IBinaryOperation { OperatorMethod: null, IsLifted: false } binary) return null;
        var left = binary.LeftOperand.Syntax as ExpressionSyntax; var right = binary.RightOperand.Syntax as ExpressionSyntax;
        if (left is null || right is null) return null;
        var saved = Value(left, state); var constant = Constant(right); var kind = binary.OperatorKind;
        if (saved is null)
        {
            saved = Value(right, state); constant = Constant(left);
            kind = kind switch { BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan, BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
                BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual, BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual, _ => kind };
        }
        // Only an alias can establish dominance; another evaluation is another value.
        if (saved is null || !state.Values.Any(value => value.Origin == saved.Origin)) return null;
        SearchOutcomes? outcome = (constant, kind) switch
        {
            (-1, BinaryOperatorKind.Equals) or (0, BinaryOperatorKind.LessThan) or (-1, BinaryOperatorKind.LessThanOrEqual) => SearchOutcomes.Sentinel,
            (-1, BinaryOperatorKind.NotEquals) or (0, BinaryOperatorKind.GreaterThanOrEqual) or (-1, BinaryOperatorKind.GreaterThan) => SearchOutcomes.Success,
            _ => null,
        };
        return outcome is { } proven ? (saved, proven) : null;
    }
    private int? Constant(ExpressionSyntax expression) => model.GetConstantValue(expression, token) is { HasValue: true, Value: int value } ? value : null;
    private void Effects(ExpressionSyntax? expression, State state)
    {
        if (expression is null) return;
        foreach (var node in expression.DescendantNodesAndSelf().Where(node => !node.Ancestors().TakeWhile(parent => parent != expression).Any(SentinelHost.Nested)))
        {
            var operation = model.GetOperation(node, token);
            ISymbol? target = operation switch
            { IAssignmentOperation assignment => SentinelSearch.Variable(assignment.Target), IIncrementOrDecrementOperation update => SentinelSearch.Variable(update.Target), _ => null };
            if (target is not null) state.Remove(target);
        }
    }
    private void Set(State state, ISymbol? symbol, SearchValue? value)
    {
        if (symbol is not ILocalSymbol) return;
        if (value is null || unstable.Contains(symbol)) state.Remove(symbol); else state[symbol] = value;
    }
    private static void Refine(State state, SearchValue value, SearchOutcomes outcomes)
    {
        foreach (var symbol in state.Keys.ToArray()) if (state[symbol].Origin == value.Origin) state[symbol] = state[symbol].Refine(outcomes);
    }
    private static State? Merge(State? yes, State? no)
    {
        if (yes is null) return no; if (no is null) return yes;
        var result = new State();
        foreach (var pair in yes)
            if (no.TryGetValue(pair.Key, out var other) && pair.Value.Origin == other.Origin)
                result[pair.Key] = new SearchValue(pair.Value.Origin, pair.Value.Outcomes | other.Outcomes, pair.Value.Helper);
        return result;
    }
}

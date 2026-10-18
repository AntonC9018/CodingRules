using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class UnchangedAssignmentTests
{
    [Fact]
    public async Task SharedA13FixturePreservesReflectionAccessorsAndAcceptedSetterEffects()
    {
        var source = Read("UnchangedAssignment", "Consumer.cs");
        var document = StatementOperationTestFixture.Document(source, Read("UnchangedAssignment", ".editorconfig"));
        var expected = await StatementOperationTestFixture.Run(document);
        Assert.Equal("null|reflected|old|old|old|2|1|2|property|getter|setter|4|2|1", expected);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
        document = await FixAll(document);
        Assert.Equal(Read("Expected", "UnchangedAssignment.Fixed.cs.txt"), (await document.GetTextAsync()).ToString());
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Empty(await Diagnostics(document));
        Assert.Equal((await document.GetTextAsync()).ToString(), (await (await FixAll(document)).GetTextAsync()).ToString());
    }

    private static string Read(string directory, string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, directory, name));

    [Theory]
    [InlineData("current is null ? null : Next(current)")]
    [InlineData("current is not null ? Next(current) : null")]
    [InlineData("current == null ? null : Next(current)")]
    [InlineData("null != current ? Next(current) : null")]
    [InlineData("flag ? current : Next(current)")]
    [InlineData("flag ? Next(current) : current")]
    [InlineData("flag ? (current) : Next(current)")]
    public async Task PreservesBothOrientationsNullsAndReachedCalls(string expression)
    {
        var source = """
            using System.Collections.Generic;
            public static class C
            {
                static readonly List<string> trace = new();
                static object? Next(object? value) { trace.Add(value?.ToString() ?? "null"); return "next"; }
                static string Update(object? current, bool flag)
                {
                    current = EXPRESSION;
                    return current?.ToString() ?? "null";
                }
                public static string Run()
                {
                    trace.Clear();
                    var result = string.Join("/", Update(null, true), Update("old", true), Update(null, false), Update("old", false));
                    return string.Join("|", result, string.Join(",", trace));
                }
            }
            """.Replace("EXPRESSION", expression);
        var document = StatementOperationTestFixture.Document(source);
        var expected = await StatementOperationTestFixture.Run(document);
        Assert.Single(await Diagnostics(document));
        document = await Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Empty(await Diagnostics(document));
    }

    [Theory]
    [InlineData("current = flag ? current : next;", "int current, int next, bool flag", true)]
    [InlineData("current = flag ? next : current;", "int current, int next, bool flag", true)]
    [InlineData("current = flag ? other : next;", "int current, int other, int next, bool flag", false)]
    [InlineData("current = current == next ? next : 1;", "int current, int next", false)]
    [InlineData("current = flag ? null : next;", "object? current, object? next, bool flag", false)]
    [InlineData("current = current is null ? next : null;", "object? current, object? next", false)]
    [InlineData("current = flag ? current : next;", "ref int current, int next, bool flag", false)]
    [InlineData("current = flag ? current : next;", "dynamic current, dynamic next, bool flag", false)]
    [InlineData("current = flag ? current : next;", "int current, int next, dynamic flag", false)]
    public async Task ReportsOnlyProvenStorageIdentity(string statement, string parameters, bool reported)
    {
        var document = StatementOperationTestFixture.Document($"class C {{ void M({parameters}) {{ {statement} }} }}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Equal(reported ? 1 : 0, (await Diagnostics(document)).Length);
        if (reported) Assert.Empty(await Diagnostics(await Fix(document)));
    }

    [Fact]
    public async Task LeavesEffectfulTargetsAndRefAliases()
    {
        var document = StatementOperationTestFixture.Document("""
            class C
            {
                volatile int field;
                int ordinary;
                static int shared;
                int Property { get; set; }
                int this[int index] { get => ordinary; set => ordinary = value; }
                C Receiver() => this;
                int Index() => 0;
                void M(bool flag, int next, int[] array, C other)
                {
                    Property = flag ? Property : next;
                    this[Index()] = flag ? this[Index()] : next;
                    array[Index()] = flag ? array[Index()] : next;
                    Receiver().ordinary = flag ? Receiver().ordinary : next;
                    other.ordinary = flag ? other.ordinary : next;
                    field = flag ? field : next;
                    shared = flag ? shared : next;
                    ref int alias = ref ordinary;
                    alias = flag ? alias : next;
                }
            }
            struct S { int field; void M(bool flag) { field = flag ? field : 1; } }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Fact]
    public async Task LeavesOverloadedNullEqualityAndUnprovedEquivalentValues()
    {
        var document = StatementOperationTestFixture.Document("""
            class C
            {
                public static bool operator ==(C? a, C? b) => true;
                public static bool operator !=(C? a, C? b) => false;
                public override bool Equals(object? obj) => false;
                public override int GetHashCode() => 0;
                void M(C? current, C? next, bool flag)
                {
                    current = current == null ? null : next;
                    current = current != null ? next : null;
                    current = flag ? Identity(current) : next;
                }
                static C? Identity(C? value) => value;
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Theory]
    [InlineData("return current = flag ? current : Next();")]
    [InlineData("current = flag ? current : Next(); return current;")]
    public async Task KeepsConditionMutationBranchMutationAndExceptions(string body)
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            using System.Collections.Generic;
            public class C
            {
                int current;
                bool flag;
                readonly List<string> trace = new();
                int Next() { trace.Add("next"); current = 20; if (flag) throw new Exception(); return 7; }
                int Update() { BODY }
                bool Decide() { trace.Add("condition"); current = 42; return flag; }
                public static string Run()
                {
                    var c = new C();
                    var results = new List<int>();
                    foreach (var flag in new[] { true, false })
                    {
                        c.flag = flag;
                        results.Add(c.Update());
                        c.current = c.Decide() ? c.current : c.Next();
                        results.Add(c.current);
                    }
                    try { c.flag = true; c.current = c.flag ? c.Next() : c.current; }
                    catch (Exception) { c.trace.Add("caught"); }
                    return string.Join(",", results) + "|" + string.Join(",", c.trace) + "|" + c.current;
                }
            }
            """.Replace("BODY", body));
        // Access through c is intentionally not a stable target; Update alone is fixed.
        var expected = await StatementOperationTestFixture.Run(document);
        Assert.Single(await Diagnostics(document));
        document = await Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Theory]
    [InlineData("int Property => current = flag ? current : Next();")]
    [InlineData("int Property { get => current = flag ? current : Next(); }")]
    [InlineData("int Property { set => current = value == 0 ? current : value; }")]
    [InlineData("int Property { init => current = value == 0 ? current : value; }")]
    [InlineData("int this[int index] => current = index == 0 ? current : Next();")]
    [InlineData("int Method() => current = flag ? current : Next();")]
    [InlineData("void Method() => current = flag ? current : Next();")]
    [InlineData("int Method() { int Local() => current = flag ? current : Next(); return Local(); }")]
    public async Task ExpandsArrowHostsWithNecessaryReturns(string member)
    {
        var document = StatementOperationTestFixture.Document("class C { int current; bool flag = true; int Next() => 7; " + member + " }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Single(await Diagnostics(document));
        document = await Fix(document);
        Assert.Empty(await Diagnostics(document));
        var root = await document.GetSyntaxRootAsync();
        Assert.DoesNotContain(root!.DescendantNodes().OfType<ArrowExpressionClauseSyntax>(), arrow => arrow.Expression is AssignmentExpressionSyntax);
    }

    [Fact]
    public async Task PreservesCommentsOnRemovedTokensAndDanglingElse()
    {
        var document = StatementOperationTestFixture.Document("""
            class C
            {
                int M(int current, int next, bool flag, bool outer)
                {
                    // exterior
                    if (outer)
                        current /*target*/ = /*condition*/ flag /*question*/ ? /*unchanged*/ current /*colon*/ : /*update*/ next; // tail
                    else
                        current = next;
                    return current;
                }
                int field;
                int Property /*declaration*/ => /*arrow*/ field = true ? /*branch*/ field : 7; // property tail
            }
            """);
        var comments = await Comments(document);
        Assert.Equal(2, (await Diagnostics(document)).Length);
        document = await FixAll(document);
        Assert.Equal(comments.OrderBy(value => value), (await Comments(document)).OrderBy(value => value));
        Assert.Empty(await Diagnostics(document));
    }

    [Theory]
    [InlineData("none", false, 0)]
    [InlineData("error", false, 1)]
    [InlineData("warning", true, 0)]
    public async Task HonorsConfigurationAndGeneratedCode(string severity, bool generated, int count)
    {
        var document = StatementOperationTestFixture.Document((generated ? "// <auto-generated/>\n" : "")
            + "class C { void M(int current, bool flag) { current = flag ? current : 1; } }",
            "root = true\n[*.cs]\ndotnet_diagnostic.CR1200.severity = " + severity + "\n");
        var diagnostics = await Diagnostics(document);
        Assert.Equal(count, diagnostics.Length);
        if (count != 0) Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async Task HonorsPragmaAndSuppressionAttribute()
    {
        var document = StatementOperationTestFixture.Document("""
            using System.Diagnostics.CodeAnalysis;
            class C
            {
            #pragma warning disable CR1200
                void A(int current, bool flag) { current = flag ? current : 1; }
            #pragma warning restore CR1200
                [SuppressMessage("Readability", "CR1200", Justification = "Accepted here")]
                void B(int current, bool flag) { current = flag ? current : 1; }
                void D(int current, bool flag) { current = flag ? current : 1; }
            }
            """);
        Assert.Single(await Diagnostics(document));
    }

    [Fact]
    public async Task LeavesExpressionTreesAndConsumedAssignments()
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            using System.Linq.Expressions;
            class C
            {
                void M(int current, int next, bool flag)
                {
                    Expression<Func<int, int>> tree = value => flag ? value : next;
                    Use(current = flag ? current : next);
                    var result = current = flag ? current : next;
                    Func<int> lambda = () => current = flag ? current : next;
                }
                void Use(int value) { }
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Theory]
    [InlineData("current = flag ? current : ;")]
    [InlineData("missing = flag ? missing : 1;")]
    public async Task LeavesInvalidCode(string statement)
    {
        Assert.Empty(await Diagnostics(StatementOperationTestFixture.Document("class C { void M(int current, bool flag) { " + statement + " } }")));
    }

    [Theory]
    [InlineData("Missing Broken() => null;", "CS0246")]
    [InlineData("string Broken() => null;", "CS8603")]
    public async Task FixRemainsAvailableWithUnrelatedExistingDiagnostics(string member, string diagnosticId)
    {
        var document = StatementOperationTestFixture.Document("""
            class C
            {
                int M(int current, bool flag) => current = flag ? current : 1;
                MEMBER
            }
            """.Replace("MEMBER", member));
        var before = (await document.GetSemanticModelAsync())!.GetDiagnostics();
        Assert.Contains(before, diagnostic => diagnostic.Id == diagnosticId);
        var diagnostic = Assert.Single(await Diagnostics(document));
        var actions = new List<CodeAction>();
        await new UnchangedAssignmentCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document,
            diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        var after = (await changed.GetSemanticModelAsync())!.GetDiagnostics();
        Assert.Equal(before.Select(item => item.Id + ":" + item.GetMessage()).OrderBy(value => value),
            after.Select(item => item.Id + ":" + item.GetMessage()).OrderBy(value => value));
        Assert.Empty(await Diagnostics(changed));
        Assert.Contains("if (!(flag))", (await changed.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task PreservesConditionMutationAndThrowingUpdateAtTheirOriginalSites()
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            public class C
            {
                int current;
                bool keep;
                bool fail;
                string trace = "";
                bool Decide() { trace += "condition,"; current = 42; return keep; }
                int Next() { trace += "next,"; current = 20; if (fail) throw new Exception(); return 7; }
                int Update() => current = Decide() ? current : Next();
                public static string Run()
                {
                    var c = new C { keep = true };
                    var a = c.Update();
                    c.keep = false;
                    var b = c.Update();
                    c.fail = true;
                    try { c.Update(); } catch (Exception) { c.trace += "caught,"; }
                    return $"{a}|{b}|{c.current}|{c.trace}";
                }
            }
            """);
        const string expected = "42|7|20|condition,condition,next,condition,next,caught,";
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        document = await Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Fact]
    public async Task DoesNotOmitWholeConditionalUserConversionsOrChangeReturnConversions()
    {
        var document = StatementOperationTestFixture.Document("""
            public class A { public static implicit operator A?(B? value) => new A(); }
            public class B { }
            public class Result
            {
                public string Text = "";
                public static implicit operator Result(C? value) => new Result { Text = value is null ? "converted-null" : "converted-value" };
            }
            public class C
            {
                C? current;
                A? Unsafe(A? current, B? next) { current = current is null ? null : next; return current; }
                Result Property => current = current is null ? null : new C();
                public static string Run() => new C().Property.Text;
            }
            """);
        const string expected = "converted-null";
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Single(await Diagnostics(document));
        document = await Fix(document);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
    }

    [Fact]
    public async Task LeavesCallerInformationAndImplicitCollectionObservers()
    {
        var document = StatementOperationTestFixture.Document("""
            using System;
            using System.Collections;
            using System.Runtime.CompilerServices;
            class Values : IEnumerable
            {
                public void Add(int value, [CallerLineNumber] int line = 0) { }
                public IEnumerator GetEnumerator() => Array.Empty<int>().GetEnumerator();
            }
            class C
            {
                void M(int current, bool flag)
                {
                    current = flag ? current : Observe();
                    var values = new Values { 1 };
                }
                static int Observe([CallerMemberName] string member = "") => 1;
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Theory]
    [InlineData(FixAllScope.Document)]
    [InlineData(FixAllScope.Project)]
    [InlineData(FixAllScope.Solution)]
    public async Task FixAllReplansMultipleSitesAndDocuments(FixAllScope scope)
    {
        const string source = "partial class C { int M(int current, bool flag) { current = flag ? current : 1; return current = flag ? 2 : current; } }";
        var document = StatementOperationTestFixture.Document(source);
        var other = document.Project.AddDocument("Other.cs", source.Replace("M(", "N("), filePath: "/Other.cs");
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = other.Project.Solution.AddProject(projectId, "Other", "Other", LanguageNames.CSharp)
            .WithProjectMetadataReferences(projectId, document.Project.MetadataReferences)
            .WithProjectParseOptions(projectId, document.Project.ParseOptions!)
            .WithProjectCompilationOptions(projectId, document.Project.CompilationOptions!)
            .AddDocument(documentId, "Third.cs", source, filePath: "/Third.cs");
        document = solution.GetDocument(document.Id)!;
        var changed = await FixAll(document, scope);
        foreach (var item in changed.Project.Solution.Projects.SelectMany(project => project.Documents))
        {
            var included = scope == FixAllScope.Solution || item.Project.Id == document.Project.Id && (scope != FixAllScope.Document || item.Id == document.Id);
            if (!included)
            {
                Assert.Equal((await solution.GetDocument(item.Id)!.GetTextAsync()).ToString(), (await item.GetTextAsync()).ToString());
                continue;
            }
            var saved = await StatementOperationTestFixture.Reparse(item);
            await StatementOperationTestFixture.Compiles(saved);
            var tree = await saved.GetSyntaxTreeAsync();
            Assert.DoesNotContain(await Diagnostics(saved), diagnostic => diagnostic.Location.SourceTree == tree);
            Assert.Equal((await saved.GetTextAsync()).ToString(), (await (await FixAll(saved)).GetTextAsync()).ToString());
        }
    }

    private static async Task<string[]> Comments(Document document) => (await document.GetSyntaxRootAsync())!.DescendantTrivia()
        .Where(trivia => trivia.ToString().StartsWith("//", StringComparison.Ordinal) || trivia.ToString().StartsWith("/*", StringComparison.Ordinal))
        .Select(trivia => trivia.ToString()).ToArray();

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new UnchangedAssignmentAnalyzer()), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static async Task<Document> Fix(Document document)
    {
        var diagnostic = (await Diagnostics(document))[0];
        var actions = new List<CodeAction>();
        var provider = new UnchangedAssignmentCodeFixProvider();
        Assert.NotNull(provider.GetType().GetCustomAttribute<ExportCodeFixProviderAttribute>());
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        document = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(document);
        return document;
    }

    private static async Task<Document> FixAll(Document document, FixAllScope scope = FixAllScope.Document)
    {
        var provider = new UnchangedAssignmentCodeFixProvider();
        var context = new FixAllContext(document, provider, scope, DiagnosticIds.UnchangedAssignment,
            provider.FixableDiagnosticIds, new DiagnosticProvider(), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        if (action is null) return document;
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        document = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        await StatementOperationTestFixture.Compiles(document);
        return document;
    }

    private sealed class DiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token)
        {
            var tree = await document.GetSyntaxTreeAsync(token);
            return (await Diagnostics(document)).Where(diagnostic => diagnostic.Location.SourceTree == tree);
        }
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token)
        {
            var result = new List<Diagnostic>();
            foreach (var document in project.Documents) result.AddRange(await GetDocumentDiagnosticsAsync(document, token));
            return result;
        }
    }
}

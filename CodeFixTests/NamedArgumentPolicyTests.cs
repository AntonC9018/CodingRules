using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class NamedArgumentPolicyTests
{
    [Theory]
    [InlineData("class C { [CodingRules.AllowPositionalArguments] static void M(int x,int y) {} static void F() => M(1,2); }", 0)]
    [InlineData("class C { [CodingRules.AllowPositionalArguments] static void M(int x,int y) {} static void M(string x,string y) {} static void F() => M(\"a\",\"b\"); }", 1)]
    [InlineData("class C { [CodingRules.AllowPositionalArguments] static void M<T>(T x,T y) {} static void F() => M(1,2); }", 0)]
    [InlineData("class C { static void F() { [CodingRules.AllowPositionalArguments] void M(int x,int y) {} M(1,2); } }", 0)]
    [InlineData("static class E { [CodingRules.AllowPositionalArguments] public static void M(this string s,int x,int y) {} } class C { static void F() => \"a\".M(1,2); }", 0)]
    [InlineData("partial class C { [CodingRules.AllowPositionalArguments] static partial void M(int x,int y); static partial void M(int x,int y) {} static void F() => M(1,2); }", 0)]
    [InlineData("partial class C { static partial void M(int x,int y); [CodingRules.AllowPositionalArguments] static partial void M(int x,int y) {} static void F() => M(1,2); }", 0)]
    [InlineData("interface I { void M(int x,int y); } class C : I { [CodingRules.AllowPositionalArguments] public void M(int x,int y) {} static void F(I c) => c.M(1,2); }", 1)]
    [InlineData("interface I { [CodingRules.AllowPositionalArguments] void M(int x,int y); } class C : I { public void M(int x,int y) {} static void F(C c) => c.M(1,2); }", 1)]
    [InlineData("interface I { [CodingRules.AllowPositionalArguments] void M(int x,int y); } class C { static void F(I c) => c.M(1,2); }", 0)]
    [InlineData("class B { [CodingRules.AllowPositionalArguments] public virtual void M(int x,int y) {} } class C : B { public override void M(int x,int y) {} static void F(C c) => c.M(1,2); }", 1)]
    [InlineData("class B { [CodingRules.AllowPositionalArguments] public void M(int x,int y) {} } class C : B { static void F(C c) => c.M(1,2); }", 0)]
    [InlineData("class C { [CodingRules.AllowPositionalArguments] public C(int x,int y) {} static C F() => new(1,2); }", 0)]
    [InlineData("[method: CodingRules.AllowPositionalArguments] class C(int x,int y) { static C F() => new(1,2); }", 0)]
    [InlineData("[method: CodingRules.AllowPositionalArguments] struct C(int x,int y) { static C F() => new(1,2); }", 0)]
    [InlineData("[method: CodingRules.AllowPositionalArguments] record C(int x,int y) { static C F() => new(1,2); }", 0)]
    [InlineData("class B { [CodingRules.AllowPositionalArguments] public B(int x,int y) {} } class C() : B(1,2);", 0)]
    [InlineData("class A : System.Attribute { [CodingRules.AllowPositionalArguments] public A(int x,int y) {} } [A(1,2)] class C {}", 0)]
    [InlineData("delegate void D(int x,int y); class C { [CodingRules.AllowPositionalArguments] static void M(int x,int y) {} static void F() { D d=M; d(1,2); } }", 1)]
    [InlineData("class C { [CodingRules.AllowPositionalArguments] static void F() { M(1,2); } static void M(int x,int y) {} }", 1)]
    [InlineData("namespace CodingRules { class AllowPositionalArgumentsAttribute : System.Attribute {} } class C { [CodingRules.AllowPositionalArguments] static void M(int x,int y) {} static void F() => M(1,2); }", 1)]
    public async Task ExactShippedAnnotation(string source, int expected)
    {
        var document = NamedArgumentTestFixture.Document(source);
        var diagnostics = await NamedArgumentTestFixture.Diagnostics(document);
        Assert.Equal(expected, diagnostics.Length);
        foreach (var diagnostic in diagnostics) Assert.Single(await NamedArgumentTestFixture.Actions(document, diagnostic));
    }

    [Fact]
    public async Task AnnotationSurvivesProducerMetadataAndIsBclOnly()
    {
        var producer = NamedArgumentTestFixture.Document("public class P { [CodingRules.AllowPositionalArguments] public static void M(int x,int y) {} public static void N(int x,int y) {} }");
        var compilation = await producer.Project.GetCompilationAsync();
        using var stream = new MemoryStream();
        var result = compilation!.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var consumer = NamedArgumentTestFixture.Document("class C { static void F() { P.M(1,2); P.N(1,2); } }");
        consumer = consumer.Project.AddMetadataReference(MetadataReference.CreateFromImage(stream.ToArray())).GetDocument(consumer.Id)!;
        var diagnostic = Assert.Single(await NamedArgumentTestFixture.Diagnostics(consumer));
        Assert.Contains("P.N", (await consumer.GetTextAsync()).ToString().Substring(diagnostic.Location.SourceSpan.Start - 3, 3));
        await NamedArgumentTestFixture.Fix(consumer, diagnostic);
        var model = await consumer.GetSemanticModelAsync();
        Assert.Equal("CodingRules.Shared", model!.Compilation.GetTypeByMetadataName("P")!.GetMembers("M").Single().GetAttributes().Single().AttributeClass!.ContainingAssembly.Name);
        Assert.DoesNotContain(typeof(AllowPositionalArgumentsAttribute).Assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("CodeAnalysis"));
        Assert.Null(typeof(AllowPositionalArgumentsAttribute).GetCustomAttributes(typeof(System.Diagnostics.ConditionalAttribute), false).SingleOrDefault());
    }

    [Theory]
    [InlineData("static int M(int x,int y) => x;", "M(1,2)")]
    [InlineData("public C(int x,int y) {}", "new C(1,2)")]
    [InlineData("static T M<T>(T x,T y) => x;", "M(1,2)")]
    [InlineData("delegate int D(int x,int y); static D d = (x,y) => x;", "d(1,2)")]
    public async Task ConfigUsesCanonicalApiIds(string declaration, string call)
    {
        var document = NamedArgumentTestFixture.Document("class C { " + declaration + " static object F() => " + call + "; }");
        var model = await document.GetSemanticModelAsync();
        var root = await document.GetSyntaxRootAsync();
        var owner = root!.DescendantNodes().First(node => node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax);
        var symbol = (IMethodSymbol)model!.GetSymbolInfo(owner).Symbol!;
        var id = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition)!;
        var entry = symbol.ContainingAssembly.Name + "::" + id.Replace("%", "%25").Replace("#", "%23").Replace(";", "%3B").Replace("|", "%7C");
        Assert.Single(await NamedArgumentTestFixture.Diagnostics(document));
        var config = "root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = malformed | Bad::M:%ZZ | " + entry + " | M:*\n";
        var configured = NamedArgumentTestFixture.Document((await document.GetTextAsync()).ToString(), config);
        Assert.True(configured.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions((await configured.GetSyntaxTreeAsync())!)
            .TryGetValue("dotnet_code_quality.CR0500.allow_positional_arguments", out var effective));
        Assert.Contains(entry, effective);
        var configuredCompilation = await configured.Project.GetCompilationAsync();
        Assert.Contains(DocumentationCommentId.GetSymbolsForDeclarationId(id, configuredCompilation!), candidate => DocumentationCommentId.CreateDeclarationId(candidate) == id);
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(configured));
        if (id.Contains('~'))
        {
            configured = NamedArgumentTestFixture.Document((await document.GetTextAsync()).ToString(), config.Replace(entry, symbol.ContainingAssembly.Name + "::" + id.Substring(0, id.IndexOf('~'))));
            Assert.Single(await NamedArgumentTestFixture.Diagnostics(configured));
        }
    }

    [Fact]
    public async Task FileValueReplacesGlobalAndCanceledResolutionDoesNotPoisonCache()
    {
        var document = NamedArgumentTestFixture.Document("class C { static int M(int x,int y) => x; static int F() => M(1,2); }");
        var compilation = await document.Project.GetCompilationAsync();
        var id = DocumentationCommentId.CreateDeclarationId(compilation!.GetTypeByMetadataName("C")!.GetMembers("M").Single())!;
        var entry = compilation.Assembly.Name + "::" + id;
        var options = new Options(entry, null);
        Assert.Empty(await Analyze(compilation, options));
        Assert.Single(await Analyze(compilation, new Options(entry, "")));
        Assert.Single(await Analyze(compilation, new Options(entry, "wrong")));
        Assert.Empty(await Analyze(compilation, new Options("", entry)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, options)).GetAnalyzerDiagnosticsAsync(cancellation.Token));
        Assert.Empty(await Analyze(compilation, options));
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(Compilation compilation, AnalyzerConfigOptionsProvider options) => compilation.WithAnalyzers(
        ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()), new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, options)).GetAnalyzerDiagnosticsAsync();

    [Fact]
    public async Task NativeReservedIdTransportAndExplicitImplementationIsolation()
    {
        const string source = "interface I { int M(int x,int y); } class C : I { int I.M(int x,int y) => x; static int F(I c) => c.M(1,2); }";
        var document = NamedArgumentTestFixture.Document(source);
        var compilation = (await document.Project.GetCompilationAsync())!;
        var implementation = compilation.GetTypeByMetadataName("C")!.GetMembers().OfType<IMethodSymbol>()
            .Single(method => method.MethodKind == MethodKind.ExplicitInterfaceImplementation);
        var id = DocumentationCommentId.CreateDeclarationId(implementation)!;
        Assert.Contains("I#M", id);
        var raw = NamedArgumentTestFixture.Document(source, "root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = " + compilation.Assembly.Name + "::" + id);
        raw.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions((await raw.GetSyntaxTreeAsync())!)
            .TryGetValue("dotnet_code_quality.CR0500.allow_positional_arguments", out var truncated);
        Assert.Equal(compilation.Assembly.Name + "::M:C.I", truncated);
        var encoded = id.Replace("#", "%23");
        var configured = NamedArgumentTestFixture.Document(source, "root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = " + compilation.Assembly.Name + "::" + encoded);
        configured.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions((await configured.GetSyntaxTreeAsync())!)
            .TryGetValue("dotnet_code_quality.CR0500.allow_positional_arguments", out var preserved);
        Assert.Equal(compilation.Assembly.Name + "::" + encoded, preserved);
        var current = (await configured.Project.GetCompilationAsync())!;
        Assert.Contains(DocumentationCommentId.GetSymbolsForDeclarationId(Uri.UnescapeDataString(encoded), current),
            symbol => symbol is IMethodSymbol { MethodKind: MethodKind.ExplicitInterfaceImplementation } && DocumentationCommentId.CreateDeclarationId(symbol) == id);
        await NamedArgumentTestFixture.Fix(configured, Assert.Single(await NamedArgumentTestFixture.Diagnostics(configured)));
    }
    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions global;
        private readonly AnalyzerConfigOptions file;
        public Options(string global, string? file) { this.global = new Values(global); this.file = new Values(file); }
        public override AnalyzerConfigOptions GlobalOptions => global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => file;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => file;
    }
    private sealed class Values(string? value) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string result) { result = value ?? ""; return value is not null && key == "dotnet_code_quality.CR0500.allow_positional_arguments"; }
    }
}

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class NamedArgumentReferenceTests
{
    [Theory]
    [InlineData("2.0")]
    [InlineData("2.1")]
    public async Task RealNetstandardReferenceDefinitions(string version)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var folder = version == "2.0" ? Path.Combine(repository, "artifacts/nuget-packages/netstandard.library/2.0.3/build/netstandard2.0/ref")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet/packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1");
        Assert.True(Directory.Exists(folder), folder);
        var references = Directory.GetFiles(folder, "*.dll").Select(path => MetadataReference.CreateFromFile(path));
        var document = NamedArgumentTestFixture.Document("class C { static string F() { System.Math.Max(1,2); string.Equals(\"a\",\"b\"); \"a\".Replace('a','b'); System.IO.Path.Combine(\"a\",\"b\"); return System.IO.Path.GetRelativePath(\"a\",\"b\"); } }");
        // GetRelativePath is unavailable on netstandard2.0. Use String.Compare as
        // the eligible genuine framework control on that reference surface.
        if (version == "2.0") document = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From("class C { static int F() { System.Math.Max(1,2); string.Equals(\"a\",\"b\"); \"a\".Replace('a','b'); System.IO.Path.Combine(\"a\",\"b\"); return string.Compare(\"a\",\"b\"); } }"));
        document = document.Project.WithMetadataReferences(references).GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(document);
        var model = await document.GetSemanticModelAsync();
        var framework = model!.Compilation.GetSpecialType(SpecialType.System_String).ContainingAssembly.Identity;
        Assert.Equal("netstandard", framework.Name);
        Assert.Equal("cc7b13ffcd2ddd51", string.Concat(framework.PublicKeyToken.Select(value => value.ToString("x2"))));
        await NamedArgumentTestFixture.Fix(document, Assert.Single(await NamedArgumentTestFixture.Diagnostics(document)));
    }

    [Fact]
    public async Task MetadataConstructorRegistrationRoundtripsInNativeConfig()
    {
        var producer = NamedArgumentTestFixture.Document("public class P { public P(int x,int y) {} }");
        producer = producer.Project.WithAssemblyName("ExternalProducer").GetDocument(producer.Id)!;
        var compilation = (await producer.Project.GetCompilationAsync())!;
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream).Success);
        var consumer = NamedArgumentTestFixture.Document("class C { static object F() => new P(1,2); }");
        consumer = consumer.Project.AddMetadataReference(MetadataReference.CreateFromImage(stream.ToArray())).GetDocument(consumer.Id)!;
        var imported = (await consumer.Project.GetCompilationAsync())!.GetTypeByMetadataName("P")!.InstanceConstructors.Single();
        var id = DocumentationCommentId.CreateDeclarationId(imported)!;
        Assert.Equal("M:P.#ctor(System.Int32,System.Int32)", id);
        Assert.Single(await NamedArgumentTestFixture.Diagnostics(consumer));
        var configured = consumer.Project.AddAnalyzerConfigDocument(".editorconfig", Microsoft.CodeAnalysis.Text.SourceText.From(
            "root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = ExternalProducer::" + id.Replace("#", "%23")), filePath: "/.editorconfig").Project.GetDocument(consumer.Id)!;
        Assert.Empty(await NamedArgumentTestFixture.Diagnostics(configured));
    }

    [Fact]
    public async Task SameSimpleNameDifferentIdentityCannotGrantRegistration()
    {
        var first = await Reference("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] public class P { public static int M(int x,int y) => x; }", "A");
        var second = await Reference("[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")] public class Q {}", "B");
        var document = NamedArgumentTestFixture.Document("extern alias A; extern alias B; class C { static int F() => A::P.M(1,2); }");
        document = document.Project.AddMetadataReferences(new[] { first, second }).GetDocument(document.Id)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        Assert.Equal(2, compilation.SourceModule.ReferencedAssemblySymbols.Count(assembly => assembly.Name == "PolicyLibrary"));
        var method = compilation.SourceModule.ReferencedAssemblySymbols.First(assembly => assembly.Identity.Version.Major == 1 && assembly.Name == "PolicyLibrary")
            .GetTypeByMetadataName("P")!.GetMembers("M").Single();
        var id = DocumentationCommentId.CreateDeclarationId(method)!;
        var configured = document.Project.AddAnalyzerConfigDocument(".editorconfig", Microsoft.CodeAnalysis.Text.SourceText.From(
            "root=true\n[*.cs]\ndotnet_code_quality.CR0500.allow_positional_arguments = PolicyLibrary::" + id), filePath: "/.editorconfig").Project.GetDocument(document.Id)!;
        var diagnostic = Assert.Single(await NamedArgumentTestFixture.Diagnostics(configured));
        Assert.Single(await NamedArgumentTestFixture.Actions(configured, diagnostic));
    }

    private static async Task<MetadataReference> Reference(string source, string alias)
    {
        var document = NamedArgumentTestFixture.Document(source);
        var project = document.Project.WithAssemblyName("PolicyLibrary");
        var key = (alias == "A" ? typeof(object).Assembly : typeof(Compilation).Assembly).GetName().GetPublicKey()!;
        project = project.WithCompilationOptions(((CSharpCompilationOptions)project.CompilationOptions!)
            .WithCryptoPublicKey(ImmutableArray.Create(key)).WithPublicSign(true));
        var compilation = (await project.GetCompilationAsync())!;
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray(), new MetadataReferenceProperties(aliases: ImmutableArray.Create(alias)));
    }
}

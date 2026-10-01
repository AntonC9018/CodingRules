using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class PipelineInteractionTests
{
    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey)]
    [InlineData(PipelineCodeFixProvider.BlockKey)]
    public async Task FrozenOperationsRemainIndependentAndBothFixOrdersProgress(string key)
    {
        const string source="using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs)=>xs.Select(x=>F(F(x)));static bool Other(bool a){if(a)return false;return a?true:false;}}";
        var document=PipelineTestFixture.Document(source);
        Assert.Equal(new[]{"CR0001","CR0300","CR0400"},(await PipelineTestFixture.Diagnostics(document,true)).Select(item=>item.Id).OrderBy(id=>id));
        var projectionFirst=await PipelineTestFixture.Fix(document,key);
        Assert.Equal("CR0001",Assert.Single(await PipelineTestFixture.Diagnostics(projectionFirst,true)).Id);
        foreach(var operationKey in new[]{StatementOperationCodeFixProvider.ExtractKey,StatementOperationCodeFixProvider.ExpandKey}){
            var operationFirst=await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document,operationKey));
            for(var pass=0;pass<3&&(await PipelineTestFixture.Diagnostics(operationFirst)).Length!=0;pass++)operationFirst=await PipelineTestFixture.Fix(operationFirst,key);
            Assert.Equal("CR0001",Assert.Single(await PipelineTestFixture.Diagnostics(operationFirst,true)).Id);
        }
    }
    [Theory]
    [InlineData("dotnet_diagnostic.CR0400.severity = none",new[]{"CR0300"})]
    [InlineData("dotnet_diagnostic.CR0300.severity = none",new[]{"CR0400"})]
    public async Task DisablingOneFamilyLeavesTheOtherEnabled(string config,string[] ids)
    {
        var document=PipelineTestFixture.Document("using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs)=>xs.Select(x=>F(F(x)));}","root=true\n[*.cs]\n"+config);
        Assert.Equal(ids,(await PipelineTestFixture.Diagnostics(document,true)).Select(item=>item.Id));
    }
    [Fact]
    public async Task MaterializerAndStageFixOrdersPreserveEachIndependentOwner()
    {
        const string source="using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs)=>xs.Select(x=>F(F(x))).ToDictionary(x=>x);}";
        var document=PipelineTestFixture.Document(source);
        var shapeFirst=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.SelectorsKey,"CR0402");
        Assert.Equal("CR0400",Assert.Single(await PipelineTestFixture.Diagnostics(shapeFirst)).Id);
        var finished=await PipelineTestFixture.Fix(shapeFirst,PipelineCodeFixProvider.ExtractKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(finished,true));
        var stageFirst=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.BlockKey,"CR0400");
        Assert.Equal("CR0402",Assert.Single(await PipelineTestFixture.Diagnostics(stageFirst)).Id);
        finished=await PipelineTestFixture.Fix(stageFirst,PipelineCodeFixProvider.SelectorsKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(finished,true));
    }
    [Fact]
    public async Task ConditionAndStageFixOrdersPreserveIndependentReporting()
    {
        const string source="using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs,string text)=>xs.Select(x=>string.IsNullOrEmpty(text.Trim()) ? F(x) : F(F(x)));}";
        var document=PipelineTestFixture.Document(source);
        Assert.Contains(await PipelineTestFixture.Diagnostics(document,true),diagnostic=>diagnostic.Id=="CR0203");
        var stageFirst=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.BlockKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(stageFirst,true));
        var conditionDiagnostic=Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        var conditionActions=await InlineConditionTestFixture.Actions(document,conditionDiagnostic);Assert.NotEmpty(conditionActions);
        foreach(var key in conditionActions.Select(action=>action.EquivalenceKey!)){
            var conditionFirst=await InlineConditionTestFixture.Fix(document,key);
            for(var pass=0;pass<4;pass++){
                if((await PipelineTestFixture.Diagnostics(conditionFirst)).Length!=0)conditionFirst=await PipelineTestFixture.Fix(conditionFirst,PipelineCodeFixProvider.ExtractKey);
                else if((await StatementOperationTestFixture.Diagnostics(conditionFirst)).Length!=0)conditionFirst=await StatementOperationTestFixture.Fix(conditionFirst,StatementOperationCodeFixProvider.ExtractKey);
                else break;
            }
            conditionFirst=await StatementOperationTestFixture.Reparse(conditionFirst);
            await StatementOperationTestFixture.Compiles(conditionFirst);Assert.Empty(await PipelineTestFixture.Diagnostics(conditionFirst,true));
        }
    }
    [Theory]
    [InlineData("#pragma warning disable CR0400\n","#pragma warning restore CR0400\n")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Readability\",\"CR0400\",Justification=\"Retain projection\")]\n","")]
    public async Task OrdinaryDriverSuppressionsRemainScoped(string before,string after)
    {
        var document=PipelineTestFixture.Document("using System.Linq;class C{static int F(int x)=>x;\n"+before+"static object Kept(int[] xs)=>xs.Select(x=>F(F(x)));\n"+after+"static object Neighbor(int[] xs)=>xs.Select(x=>F(F(x)));}");
        Assert.Single(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal(2,(await StatementOperationTestFixture.Diagnostics(document)).Length);
        var compilation=await document.Project.GetCompilationAsync();
        var options=new CompilationWithAnalyzersOptions(document.Project.AnalyzerOptions,null,true,false,reportSuppressedDiagnostics:true);
        var all=await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer()),options).GetAnalyzerDiagnosticsAsync();
        Assert.Equal(2,all.Length);Assert.Single(all.Where(item=>item.IsSuppressed));
    }
    [Theory]
    [InlineData(PipelineCodeFixProvider.ExtractKey, FixAllScope.Document)]
    [InlineData(PipelineCodeFixProvider.ExtractKey, FixAllScope.Project)]
    [InlineData(PipelineCodeFixProvider.ExtractKey, FixAllScope.Solution)]
    [InlineData(PipelineCodeFixProvider.BlockKey, FixAllScope.Document)]
    [InlineData(PipelineCodeFixProvider.BlockKey, FixAllScope.Project)]
    [InlineData(PipelineCodeFixProvider.BlockKey, FixAllScope.Solution)]
    [InlineData(PipelineCodeFixProvider.SelectorsKey, FixAllScope.Document)]
    [InlineData(PipelineCodeFixProvider.SelectorsKey, FixAllScope.Project)]
    [InlineData(PipelineCodeFixProvider.SelectorsKey, FixAllScope.Solution)]
    public async Task SequentialFixAllMultipleDocumentsOverlapFreshNamesAndSecondPass(string key, FixAllScope scope)
    {
        const string source="using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs){int ProjectValue=1;var first=xs.Select(x=>F(F(x))).ToDictionary(x=>x);var second=xs.Select(x=>F(x)+1);return second;}}";
        var document=PipelineTestFixture.Document(source);
        var second=document.Project.AddDocument("Second.cs",source.Replace("class C","class D"),filePath:"/Second.cs");
        var otherProject=ProjectId.CreateNewId();
        var otherDocument=DocumentId.CreateNewId(otherProject);
        var solution=second.Project.Solution.AddProject(otherProject,"Other","Other",LanguageNames.CSharp)
            .WithProjectMetadataReferences(otherProject,document.Project.MetadataReferences)
            .WithProjectParseOptions(otherProject,document.Project.ParseOptions!)
            .WithProjectCompilationOptions(otherProject,document.Project.CompilationOptions!)
            .AddDocument(otherDocument,"Other.cs",Microsoft.CodeAnalysis.Text.SourceText.From(source.Replace("class C","class E")),filePath:"/Other.cs");
        document=solution.GetDocument(document.Id)!;
        using var timeout=new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
        var provider=new PipelineCodeFixProvider();
        var context=new FixAllContext(document,provider,scope,key,provider.FixableDiagnosticIds,new Diagnostics(),timeout.Token);
        var action=await provider.GetFixAllProvider().GetFixAsync(context);
        var operation=Assert.Single((await action!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
        foreach(var item in operation.ChangedSolution.Projects.SelectMany(project=>project.Documents)){
            if(scope!=FixAllScope.Solution&&(item.Project.Id!=document.Project.Id||scope==FixAllScope.Document&&item.Id!=document.Id)){
                Assert.Equal((await solution.GetDocument(item.Id)!.GetTextAsync()).ToString(),(await item.GetTextAsync()).ToString());continue;
            }
            var saved=await StatementOperationTestFixture.Reparse(item);await StatementOperationTestFixture.Compiles(saved);
            var model=(await saved.GetSemanticModelAsync())!;
            var remaining=(await PipelineTestFixture.Diagnostics(saved)).Where(diagnostic=>diagnostic.Location.SourceTree==model.SyntaxTree);
            Assert.DoesNotContain(remaining,diagnostic=>key==PipelineCodeFixProvider.SelectorsKey?diagnostic.Id=="CR0402":diagnostic.Id=="CR0400");
            var again=new FixAllContext(saved,provider,FixAllScope.Document,key,provider.FixableDiagnosticIds,new Diagnostics(),timeout.Token);
            var repeated=await provider.GetFixAllProvider().GetFixAsync(again);
            var repeatOperation=Assert.Single((await repeated!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
            Assert.Equal((await saved.GetTextAsync()).ToString(),(await repeatOperation.ChangedSolution.GetDocument(saved.Id)!.GetTextAsync()).ToString());
        }
    }
    [Fact]
    public async Task FixAllHonorsCancellation()
    {
        var document=PipelineTestFixture.Document("using System.Linq;class C{static int F(int x)=>x;static object M(int[] xs)=>xs.Select(x=>F(F(x)));}");
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        var provider=new PipelineCodeFixProvider();
        var context=new FixAllContext(document,provider,FixAllScope.Document,PipelineCodeFixProvider.ExtractKey,provider.FixableDiagnosticIds,new Diagnostics(),cancellation.Token);
        var action=await provider.GetFixAllProvider().GetFixAsync(context);
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(()=>action!.GetOperationsAsync(cancellation.Token));
    }
    private sealed class Diagnostics:FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,CancellationToken cancellationToken)=>await PipelineTestFixture.Diagnostics(document);
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,CancellationToken cancellationToken)=>Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,CancellationToken cancellationToken){var result=new List<Diagnostic>();foreach(var document in project.Documents)result.AddRange(await PipelineTestFixture.Diagnostics(document));return result;}
    }
}

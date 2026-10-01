using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class PipelineSourceTests
{
    [Fact]
    public async Task PinnedWeightedTagProjectionTransitiveLocalsAndOneLocalNegative()
    {
        const string source="""
            using System.Linq; using System.Collections.Generic; using System.Collections.Immutable;
            class C {
              sealed record Tag(string Name);
              sealed record RequiredTagGroup(Tag CanonicalTag,ImmutableArray<Tag> ConfiguredTags,float MaximumWeight);
              sealed record TagMatchOrigin(RequiredTagGroup Group,float Weight,bool isDirect);
              sealed record WeightedTagProjection(Tag TargetTag,float MaximumWeight,float BaseWeight,ImmutableArray<TagMatchOrigin> Origins);
              sealed record Builder(Tag Tag,List<Tag> ConfiguredTags,float MaximumWeight);
              static object M(List<Builder> groupOrder){
            #line 1269 "Core/Database/ExperienceDatabase.cs"
                var groups=groupOrder.Select(x=>{var configuredTags=x.ConfiguredTags.ToImmutableArray();return new RequiredTagGroup(x.Tag,configuredTags,x.MaximumWeight);}).ToImmutableArray();
            #line 1279 "Core/Database/ExperienceDatabase.cs"
                var projections=groups.Select(group=>{var origin=new TagMatchOrigin(group,group.MaximumWeight,isDirect:true);ImmutableArray<TagMatchOrigin> origins=[origin];return new WeightedTagProjection(group.CanonicalTag,group.MaximumWeight,group.MaximumWeight,origins);}).ToImmutableArray();
            #line default
                return projections;
              }
            }
            """;
        var document=PipelineTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic=Assert.Single(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal("CR0401",diagnostic.Id);Assert.Equal(1278,diagnostic.Location.GetMappedLineSpan().StartLinePosition.Line);
        var changed=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.ExtractKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
    }
    [Fact]
    public async Task PinnedFormattableRendererWholeStageTypingIsPreserved()
    {
        const string source="""
            using System;using System.Linq;using System.Collections.Generic;using System.Collections.Immutable;
            class C{sealed record Skill(string Text);sealed record Language(string Name);sealed record Level(string Value);sealed record Info(Language Language,Level GeneralProficiencyLevel,ImmutableArray<Skill> Skills);
            static string Convert(string x)=>x;static FormattableString Join(IEnumerable<FormattableString> xs,string separator)=>$"{string.Join(separator,xs)}";
            static object M(ImmutableArray<Info> languages){
            #line 110 "Core/LatexMeasurement/CvLatexFragmentRenderer.cs"
            var rows=languages.Select(static language=>{var languageName=Convert(language.Language.Name);var proficiency=Convert(language.GeneralProficiencyLevel.Value);var renderedSkills=language.Skills.Select(static skill=>{var renderedSkill=Convert(skill.Text);return (FormattableString)$"{renderedSkill}";});var skills=Join(renderedSkills,", ");return (FormattableString)$"{languageName} & {proficiency} & {skills}";});
            #line default
            return rows;}}
            """;
        var document=PipelineTestFixture.Document(source);
        var diagnostic=Assert.Single(await PipelineTestFixture.Diagnostics(document));
        Assert.Equal("CR0401",diagnostic.Id);
        var changed=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.ExtractKey);
        Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
        Assert.Equal((await StatementOperationTestFixture.Diagnostics(document)).Select(item=>item.Id),(await StatementOperationTestFixture.Diagnostics(changed)).Select(item=>item.Id));
    }
    [Fact]
    public async Task PinnedRowInitializerAndSimpleTagConstructionRemainAllowed()
    {
        const string source="""
            using System.Linq;using System.Collections.Generic;
            class C{sealed record Field(string Value);sealed class Row{public string[] Fields{get;init;}=null!;public int LineNumber{get;init;}}sealed record Tag(string Name);
            static object M(List<List<Field>> rows){
            #line 230 "WebUi/ApplicationIndexStore.cs"
            return rows.Select((fields,index)=>new Row{Fields=fields.Select(static field=>field.Value).ToArray(),LineNumber=index+1}).ToList();
            #line default
            }
            static object Tags(Tag[] tags)=>tags.Select(static tag=>new Tag(tag.Name));}
            """;
        var document=PipelineTestFixture.Document(source);await StatementOperationTestFixture.Compiles(document);Assert.Empty(await PipelineTestFixture.Diagnostics(document));
    }
    [Theory]
    [InlineData("ToImmutableDictionary(x=>x.TargetTag)","Core/Database/ExperienceDatabase.cs",1239)]
    [InlineData("ToDictionary(static tag=>tag.Name,StringComparer.OrdinalIgnoreCase)","Core/Database/Tag.cs",1046)]
    [InlineData("ToImmutableDictionary(x=>x.TargetTag,x=>x.Value)","Core/Database/ExperienceDatabase.cs",1388)]
    public async Task PinnedMaterializersUseTheirActualSelectorParameterNames(string expression,string file,int line)
    {
        var source="using System;using System.Linq;using System.Collections.Immutable;class C{sealed record Tag(string Name,string TargetTag,int Value);static object M(ImmutableArray<Tag> tags){\n#line "+line+" \""+file+"\"\nreturn tags."+expression+";\n#line default\n}}";
        var document=PipelineTestFixture.Document(source);await StatementOperationTestFixture.Compiles(document);
        var diagnostic=Assert.Single(await PipelineTestFixture.Diagnostics(document));Assert.Equal("CR0402",diagnostic.Id);Assert.Equal(file,diagnostic.Location.GetMappedLineSpan().Path);
        var changed=await PipelineTestFixture.Fix(document,PipelineCodeFixProvider.SelectorsKey);Assert.Empty(await PipelineTestFixture.Diagnostics(changed));
    }
}

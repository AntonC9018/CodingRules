using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class CompoundPredicateSafetyTests
{
    [Theory]
    [InlineData("static bool M(int[] ids) => ids.Any(id => id > 0 /* retained */ && ids.Any(p => p == id));")]
    [InlineData("static bool M(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));\n"
        + "static int Later() => Line();")]
    [InlineData("static bool M(int[] ids) => ids.Any(id => Line() > 0 && ids.Any(p => p == id));")]
    [InlineData("static bool M(int[] ids) => Observe(ids.Any(id => id > 0 && ids.Any(p => p == id)));")]
    [InlineData("static bool M(int[] ids) => ids.Any(id => Observe(id > 0) && ids.Any(p => p == id));")]
    [InlineData("static System.Linq.Expressions.Expression<System.Func<int[], bool>> M() => "
        + "ids => ids.Any(id => id > 0 && ids.Any(p => p == id));")]
    [InlineData("static System.Linq.Expressions.Expression<System.Func<int[], bool>> M() => "
        + "ids => ids.Any(a => ids.Any(b => ids.Any(c => c == a)));")]
    public async Task ConfidentViolationsRemainReportedWhenFixIsUnsafe(string member)
    {
        var document = CompoundPredicateTestFixture.Document("""
            using System.Linq;
            class C {
                static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
                static bool Observe(bool condition,
                    [System.Runtime.CompilerServices.CallerArgumentExpression("condition")] string text = "") => condition;
            """ + member + "}");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Empty(await CompoundPredicateTestFixture.Actions(document, diagnostic));
    }

    [Fact]
    public async Task DownstreamIndexerCallerLineAlsoDeclinesFix()
    {
        var document = CompoundPredicateTestFixture.Document("""
            using System.Linq;
            class C {
                public int this[int key, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0] => line;
                static bool M(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));
                static int Later(C value) => value[0];
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Empty(await CompoundPredicateTestFixture.Actions(document, diagnostic));
    }

    [Fact]
    public async Task ExplicitCallerInformationAllowsSafeRewrite()
    {
        var document = CompoundPredicateTestFixture.Document("""
            using System.Linq;
            class C {
                static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
                static bool M(int[] ids) => ids.Any(id => Line(17) > 0 && ids.Any(p => p == id));
                static int Later() => Line(19);
            }
            """);
        var changed = await CompoundPredicateTestFixture.Fix(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task UserDefinedLogicalOperatorsAreOutsideCompoundCatalogue()
    {
        var document = CompoundPredicateTestFixture.Document("""
            using System.Linq;
            class C {
                readonly struct Flag {
                    public static bool operator true(Flag value) => true;
                    public static bool operator false(Flag value) => false;
                    public static Flag operator &(Flag left, Flag right) => left;
                    public static implicit operator bool(Flag value) => true;
                    public static implicit operator Flag(bool value) => new();
                }
                static bool M(int[] ids) => ids.Any(id => new Flag() && (Flag)ids.Any(p => p == id));
                static bool Nullable(bool? flag, int[] ids) => ids.Any(id => (flag & ids.Any(p => p == id)) == true);
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("Flag")]
    public async Task DepthOnlyFixDeclinesDifferentReturnAndConditionalTruthBindings(string type)
    {
        var source = """
            using System.Linq;
            class C {
                readonly struct Flag {
                    public static bool operator true(Flag value) => false;
                    public static bool operator false(Flag value) => true;
                    public static implicit operator bool(Flag value) => true;
                }
                static TYPE Wrap(bool value) => new Flag();
                static bool M(int[] ids) => ids.Any(a => Wrap(ids.Any(b => ids.Any(c => c == a))));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source.Replace("TYPE", type));
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(document));
        Assert.Empty(await CompoundPredicateTestFixture.Actions(document, diagnostic));
    }
}

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SourceGeneration.Testing;
using Xunit;

namespace CodingRules;

public sealed class ReturnDecisionFormattingTests
{
    [Fact]
    public async Task UsesConfiguredLineEndingOverExistingConvention()
    {
        var testCode = string.Concat(
            "class C\r\n",
            "{\r\n",
            "    bool Check() => true;\r\n",
            "\r\n",
            "    bool M(bool flag)\r\n",
            "    {\r\n",
            "        lock (new object())\r\n",
            "        {\r\n",
            "            return {|CR0006:Check()|};\r\n",
            "        }\r\n",
            "\r\n",
            "        return false;\r\n",
            "    }\r\n",
            "}\r\n");
        var fixedCode = string.Concat(
            "class C\r\n",
            "{\r\n",
            "    bool Check() => true;\r\n",
            "\r\n",
            "    bool M(bool flag)\r\n",
            "    {\r\n",
            "        lock (new object())\r\n",
            "        {\n",
            "            if (Check())\n",
            "            {\n",
            "                return true;\n",
            "            }\n",
            "\n",
            "            return false;\n",
            "        }\n",
            "\r\n",
            "        return false;\r\n",
            "    }\r\n",
            "}\r\n");

        var test = new CSharpCodeFixTest<
            NestedReturnDecisionAnalyzer,
            NestedReturnDecisionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\nend_of_line = lf\n"));

        await test.RunAsync();
    }
}

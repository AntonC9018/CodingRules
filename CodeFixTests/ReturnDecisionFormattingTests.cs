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

    [Theory]
    [InlineData("\r\n", "lf", "\n")]
    [InlineData("\n", "crlf", "\r\n")]
    [InlineData("\r\n", "cr", "\r")]
    public async Task UsesConfiguredLineEndingForMinimalNestedReturn(
        string existingEndOfLine,
        string setting,
        string generatedEndOfLine)
    {
        await VerifyMinimalNestedReturnAsync(
            existingEndOfLine: existingEndOfLine,
            setting: setting,
            generatedEndOfLine: generatedEndOfLine);
    }

    [Theory]
    [InlineData("\n", null)]
    [InlineData("\r\n", null)]
    [InlineData("\r\n", "unset")]
    [InlineData("\r\n", "invalid")]
    public async Task PreservesDocumentLineEndingWithoutValidConfiguration(
        string existingEndOfLine,
        string? setting)
    {
        await VerifyMinimalNestedReturnAsync(
            existingEndOfLine: existingEndOfLine,
            setting: setting,
            generatedEndOfLine: existingEndOfLine);
    }

    private static async Task VerifyMinimalNestedReturnAsync(
        string existingEndOfLine,
        string? setting,
        string generatedEndOfLine)
    {
        var prefix = string.Concat(
            "class C", existingEndOfLine,
            "{", existingEndOfLine,
            "    bool M(bool flag)", existingEndOfLine,
            "    {", existingEndOfLine,
            "        lock (new object())", existingEndOfLine);
        var suffix = string.Concat(
            "    }", existingEndOfLine,
            "}", existingEndOfLine);
        var originalBlock = string.Concat(
            "        {", existingEndOfLine,
            "            return {|CR0006:flag == true|};", existingEndOfLine,
            "        }", existingEndOfLine);
        var fixedBlock = string.Concat(
            "        {", generatedEndOfLine,
            "            if (flag == true)", generatedEndOfLine,
            "            {", generatedEndOfLine,
            "                return true;", generatedEndOfLine,
            "            }", generatedEndOfLine,
            generatedEndOfLine,
            "            return false;", generatedEndOfLine,
            "        }", generatedEndOfLine);
        var testCode = string.Concat(prefix, originalBlock, suffix);
        var fixedCode = string.Concat(prefix, fixedBlock, suffix);
        var test = new CSharpCodeFixTest<
            NestedReturnDecisionAnalyzer,
            NestedReturnDecisionCodeFixProvider,
            DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
        };
        if (setting is not null)
        {
            var editorConfig = $"root = true\n\n[*]\nend_of_line = {setting}\n";
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));
        }

        await test.RunAsync();
    }
}

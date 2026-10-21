# String concatenation and interpolation

`CR1100` is a Readability warning, enabled by default, for runtime built-in
string-only `+` trees. It implements ScheduleLib follow-up ticket 10 / A10,
independently of the unresolved general inline-string/message-record policy.
Generated code is excluded. Standard Roslyn severity, pragma and
SuppressMessage configuration applies, for example:

```ini
dotnet_diagnostic.CR1100.severity = warning
```

The fix preserves explicit conversions already written by the author:

```csharp
// Before
"desired-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
file + ".lock"

// After
(string)$"desired-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
(string)$"{file}.lock"
```

The explicit string cast preserves the original expression's static type.
An uncast interpolation can select a custom handler, FormattableString, span
or other target-sensitive conversion instead of the original string overload.
The cast stays in the generated source; it is not annotated for simplification.

## Reuse decision

The [existing reuse assessment](formatting-reuse-research.md) identified
Roslyn's [concatenation refactoring](https://github.com/dotnet/roslyn/blob/036431aef582b93345a8f54b7a452926d2abd459/src/Features/CSharp/Portable/ConvertToInterpolatedString/CSharpConvertConcatenationToInterpolatedStringRefactoringProvider.cs)
and Roslynator's [RCS1267](https://josefpihrt.github.io/docs/roslynator/analyzers/RCS1267/).
The Roslyn provider is internal IDE refactoring infrastructure, not a public
enforcement diagnostic/code-fix API. RCS1267 handles `string.Concat`, not the
reviewed `+` expressions. A regression probe using the already bundled
Roslynator 5.0.1 binaries enables RCS1267 and observes one finding on
`string.Concat(file, ".lock")`, with neither A10 addition reported.
IDE0071 simplifies existing interpolation and cannot supply this finding.

CR1100 therefore supplies the small missing diagnostic/fix without adding a
dependency, enabling another upstream rule, using reflection in shipped code,
or copying upstream source. Existing upstream notices and package composition
remain applicable.

## Conservative boundary

Each reported operator must be resolved built-in, non-lifted addition with
string result and two operands whose source types are already `System.String`.
Parenthesized connected string-only additions receive one finding at the root.
All-string constant expressions remain accepted, including const declarations.

Numeric addition, string/object or string/char addition, dynamic operators,
user-defined operators and implicit user conversions to string stay outside
the first implementation. Numeric work inside an explicit `ToString` is
retained verbatim; the fixer never substitutes a numeric formatting hole for
that conversion. A bare null literal is outside the source-type boundary;
nullable string values are supported and still contribute empty text.

The fix flattens only connected proven string additions. It retains the order,
number and location of operand evaluations; string operands have no additional
user formatting step. Regular, verbatim and raw literal values are escaped into
ordinary interpolation text, including braces, quotes and control characters.
Complex holes and member/index receivers retain necessary grouping. No local
or helper is introduced. A concatenation inside an existing handler hole stays
inside that hole, preserving constructor gating and append short circuiting.

Expression trees, comments/directives within the replaced expression, invalid
binding, pre-C# 6 source and pre-C# 11 multiline interpolation holes are
conservative omissions. Caller-information observers prohibit affected edits,
including omitted source-text arguments and later line-number observations.
Implicit collection Add and indexer-initializer observers are included;
collection initializers themselves remain excluded. Fix All tracks nested
syntax and rechecks edits against preceding changes in document, project and
solution scopes. Saved-source tests verify compilation and idempotence.

CR02/CR03 and other existing diagnostic policies remain independent. This rule
does not require structured messages, extract formatted strings, redesign
reporting APIs or change the accepted lexer builder interpolation.

## A10 evidence and final package validation

The shared [fixture](../CodeFixTests/Consumers/StringConcatenation/Consumer.cs)
retains the complete reviewed Report and LocalFileLock.Acquire calls from
ScheduleLib `12e601e7b62511bdecc44e280407fee12147b230`, at
[CalendarSync.cs:232-247](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CalendarSync.cs#L232-L247)
and [CliSettings.Writes.cs:106-109](https://github.com/AntonC9018/ScheduleLib/blob/12e601e7b62511bdecc44e280407fee12147b230/src/MainCli/CliSettings.Writes.cs#L106-L109).
Surrounding domain/dependency types are compilation stubs. The fixture includes
accepted constants, numeric/object/dynamic/overloaded additions, an existing
interpolation and an expression tree. Unit tests assert exactly two findings,
exact saved fixes, unchanged invocation bindings and runtime behavior.

The culture probe uses fr-FR with a deliberately different negative sign:
ordinary `-123` renders as `~123`, while the identifier remains `desired--123`
before and after the fix. Null and ordinary paths remain `.lock` and
`calendar.lock`. Additional runtime tests cover side effects, throwing operands,
null string chains, literal escaping and custom handler overloads/gating.

`PackageConsumptionTests.AppliesPackagedStringConcatenationFix` uses the same
fixture and expected source through Anton.CodingRules. It is registered for
the final package-validation run after all tickets. Per the current development
policy, ticket 10 runs only focused and broad non-consumer tests; no package
build or consumer gate is run here. Packaged execution and downstream adoption
are therefore still pending and are not claimed by these unit results.

Local validation on 2026-10-18 passed 42 focused tests, followed sequentially
by 161 analyzer and 990 code-fix tests in the broad check (1,151 total):

```sh
dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release '-p:WarningsNotAsErrors=NU1902%3BNU1903' --filter 'FullyQualifiedName~CodingRules.StringConcatenationTests&FullyQualifiedName!~PackageConsumptionTests&FullyQualifiedName!~Packaged&FullyQualifiedName!~Consumer' --logger 'console;verbosity=minimal'
dotnet test CodingRules.slnx -c Release '-p:WarningsNotAsErrors=NU1902%3BNU1903' --filter 'FullyQualifiedName!~PackageConsumptionTests&FullyQualifiedName!~Packaged&FullyQualifiedName!~Consumer' --logger 'console;verbosity=minimal'
```

Both filters explicitly exclude PackageConsumptionTests and every name
containing Packaged or Consumer. The existing transitive NU1902/NU1903 audit
warnings use the command-scoped override specified by the repository policy.

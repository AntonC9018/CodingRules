# Named argument roles

CR0500 reports an explicit call list when at least two distinct supplied
parameters have the same constructed type and one is positional. Every positional
argument in each repeated-type group receives its bound parameter name. This
includes identifiers matching parameter names, constants and arbitrary expressions.
Omitted optional parameters and an implicit extension receiver do not count.
Expanded params entries share one parameter; an explicit params collection counts
once. Nullable reference annotations and tuple labels do not distinguish types.

The warning covers method/local-function/delegate/extension calls, conditional
invocations, ordinary and target-typed construction, base/this initializers,
primary constructor base lists and attribute constructors. Attribute member `=`
assignments do not count. Indexers are safety observers rather than warning hosts.
Generated, invalid, unresolved and dynamic calls are excluded.

The finite genuine framework exceptions are Math/MathF two-input Min/Max,
String.Equals(string,string) with its optional StringComparison overload,
String.Replace(char,char), Replace(string,string), and the StringComparison or
bool/CultureInfo families, and Path.Combine's two/three/four-string families.
Single-parameter Combine params overloads cannot establish a repeated group.
Metadata provenance and exact Microsoft assembly/token pairs are required;
source/foreign lookalikes are eligible. Assert.Equal, Path.GetRelativePath,
String.Compare and Math.Clamp remain eligible.

Use `[CodingRules.AllowPositionalArguments]` on an owned method, local function or
constructor to exempt that exact bound declaration. It is available through the
Anton.CodingRules compile asset, CodingRules.Shared. The marker has no runtime
behavior and is retained in metadata for downstream callers. Generic/reduced and
partial declarations normalize to their declaration; interface/override policy
does not inherit. Primary constructors use `[method: CodingRules.AllowPositionalArguments]`.
Delegate invocation uses its Invoke signature rather than its runtime target.
An annotated method's body remains eligible. Source/foreign marker lookalikes
are ignored. Consumers excluding compile assets may use configuration instead.

Register an exact external or source declaration in `.editorconfig`:

```ini
[*.cs]
dotnet_code_quality.CR0500.allow_positional_arguments = xunit.assert::M:Xunit.Assert.Equal(System.String,System.String) | Example.Library::M:Example.Widget.%23ctor(System.Int32,System.Int32)
```

Obtain the ID from the actual bound symbol using Roslyn 4.10:

```csharp
var declaration = (method.ReducedFrom ?? method).OriginalDefinition;
declaration = declaration.PartialDefinitionPart ?? declaration;
var id = DocumentationCommentId.CreateDeclarationId(declaration);
var entry = declaration.ContainingAssembly.Name + "::" + id;
```

Keep the exact API return suffix when present. Examples verified with Roslyn are
`M:C.G``1(``0,``0)~``0` for a generic method and
`M:D.Invoke(System.Int32,System.Int32)~System.Int32` for delegate Invoke.
A constructor is `M:C.#ctor(System.Int32,System.Int32)`; explicit implementation
is `M:C.I#M(System.Int32,System.Int32)~System.Int32`.

Roslyn's [native AnalyzerConfig parser](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfig.cs)
treats `#` and `;` as inline comments, including inside quoted/escaped values.
Percent-encode reserved characters in the ID field: `%` → `%25`, `#` → `%23`,
`;` → `%3B`, `|` → `%7C`. Thus the implementation example is
`Example.Library::M:C.I%23M(System.Int32,System.Int32)~System.Int32`.
Entries split on `|`, decode once and must exactly roundtrip through Roslyn.
Malformed escapes/IDs, wildcards, missing return suffixes and ambiguous assembly
names are ignored individually. Supported registrations are ordinary methods,
explicit implementations, constructors and DelegateInvoke. Local functions use
the annotation. File options replace global values; an empty file value clears
the configured list. Built-in/annotation exceptions stay independent.

The sole action adds names in source order without changing expressions, trivia,
ref modes or evaluation timing. Pre-C#7.2 callers also name later arguments when
required. An expanded params entry that would need a name is an individual skip;
it is never bundled into an array. The analyzer proves unchanged correspondence
without replacing a compilation for each root. It conservatively omits a site
when another overload's parameter names could remap the arguments, including the
error-free V/int/bool counterexample from the accepted contract.

Expression trees and affected implicit caller-information defaults are safety
omissions. Observers include enclosing method/constructor/attribute/indexer calls
and later CallerLineNumber sites anywhere in the same file. Explicit constants
and earlier unaffected line observers remain supported. These omissions apply to
individual sites; no accepted host category is excluded wholesale.

The fixer reparses saved source and compares constructed assembly/symbol identities,
source-order operations, argument correspondence, conversions/operator identities,
implicit defaults, attribute constants/member bindings and exact mapped compiler
diagnostic locations. It rechecks current callee policy and selected progress.
Fix All rediscovers sites after each saved edit in document/project/solution scope,
honors suppression/cancellation and is idempotent. Earlier diagnostic families,
including overlapping CR0402 selectors, remain independent.

# Real string-formatting cases for the design interview

Policy remains open. These are examples to judge, not a new diagnostic contract.
ScheduleLib source is pinned to `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`;
CR03 was inspected and probed from CodingRules `70ae752`.

Older owner-authored lines are identified by their original commit and date.
Recent ownership alone does not establish human authorship. Agent provenance
below uses either the dedicated agent account or a Generated-by commit trailer;
formatting edits are distinguished from introducing the original text.

## E1: a constant file-search pattern in older code

[Source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/CurriculumShared/CurriculumNameParser.cs#L611-L617).
The interpolated argument is attributed to owner commit `38418281`, 2025-06-09.
`CurriculumDocumentExtension` is the constant string `.docx`.

```csharp
var dirs = Directory.EnumerateFiles(
    path: directory.FullPath,
    searchPattern: $"*{CurriculumDocumentExtension}");
```

The reduced harness reports CR0300 on this interpolation. It is a machine glob,
not a human message. A candidate correction is a named format constant:

```csharp
private const string CurriculumDocumentSearchPattern =
    $"*{CurriculumDocumentExtension}";
```

Pass that constant as `searchPattern`. The containing `ListFiles` method already
provides a domain abstraction; another message record is not automatically needed.
**Q8 resolved:** use the named const when the extension is constant; use a
named local function when the extension is variable.

## E2: a typed exception already owning its rendering

[Source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/Features/LayeredConfig/Topics/ManifestSerializer.cs#L104-L114).
Owner commit `2f12258a`, 2026-02-15. The class retains the expected/found values.

```csharp
public UnexpectedTeacherNameException(Name expected, Name found)
    : base($"Expected name {expected}, got {found}")
{
    _expected = expected;
    _found = found;
}
```

The reduced base-constructor probe reports no CR03 diagnostic. Constructor
initializers are an existing safety omission, so this silence does not itself
prove that the style policy accepts the code. The maintainer prefers accessible
structured context and questions this eager base-message formatting; deferred
Message rendering is under investigation. This constructor is not yet an
accepted exception to the string policy.

## E3: an agent-generated inline framework exception

[Source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/RunOutput.cs#L59-L64).
The text was introduced by `159d40ac`, 2026-10-08, whose author is the owner but
whose trailer is `Generated-by: gpt-6.1-sol`; agent commit `12e601e` reformatted it.

```csharp
throw new IOException($"Output directory cannot traverse a symbolic link: {current.FullName}");
```

The reduced harness reports CR0300 on the interpolated argument. One possible
architectural correction is an application-owned exception accepting the data:

```csharp
throw new SymbolicLinkTraversalException(current.FullName);
```

The new exception would derive from IOException and own message formatting,
like E2. This is a proposed manual API design, not an existing class or a proved
behavior-preserving automatic fix: changing the concrete exception type may
matter to callers. Merely extracting the string is a smaller alternative.
**Q9 partly resolved:** prefer a custom exception with accessible associated
context. Whether and how to defer Message formatting needs an evidence-based
recommendation; eager base-message interpolation has not been approved.

## E4: an existing builder and custom handler

[Source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/ScheduleLib/ScheduleLib.Helper/Parser/Lexer.cs#L526-L537).
The interpolated line is owner commit `53b0a13c`, 2026-01-27.

```csharp
var sb = new StringBuilder();
sb.Append("[");
var list = new ListStringBuilder(sb, ", ");
// Inside the token loop:
var type = TokenTypeLabels.Get(t.Type);
list.Append($"{type} - {t.Value.Span}");
sb.Append("]");
```

The custom `ListStringBuilder.Append` takes an interpolated-string handler;
its overloads accept strings, spans, and generic values. A reduced call using
this actual helper source reports no CR03 diagnostic. Keep this as a possible
accepted case rather than inventing another wrapper around an existing renderer.
Chaining separate Append calls would not be a drop-in rewrite: these methods
return void, and the helper manages separators between appended items.
**Q10 resolved:** this exact builder/handler use is accepted by the maintainer.

## E5: repeated export prose in agent-edited code

[Source](https://github.com/AntonC9018/ScheduleLib/blob/4c26748e448fe7ccbdffc3f8f6c4b05ffddce144/src/MainCli/ExportCommands.cs#L178-L179).
The most recent edit is agent commit `4c26748`; the same formatting existed
before that whitespace change. Agent commit `12e601e` introduced the shared
completion structure. Do not attribute the original text solely from a later edit.

```csharp
RenderHuman = status => cli.WriteHuman(
    $"{status}: {lessonCount} lessons, {teacherCount} teachers, {paths.Length} artifacts in {output?.DirectoryPath}."),
```

`WriteHuman` takes `string line`. Related export commands repeat this pattern.
The reduced harness reports CR0300. A candidate correction is a named export
summary value, with counts/status/destination rendered by the output boundary.
Keep the current deferred rendering time: do not eagerly snapshot mutable
properties or format the message earlier without checking its behavior.

## Probe boundaries and other cases

The temporary console harness used SDK 10.0.112, Roslyn 4.10, C# 12, and the
existing built StatementOperationAnalyzer. It compiled isolated shapes with
real expressions and reduced surrounding types/signatures; E4 also included
the actual ListStringBuilder implementation. These are CR03 observations, not
a full ScheduleLib analysis, complete real-project overload proof, or tested
refactorings. Other diagnostic families were not run.

- The same probe flags an interpolated raw single-line CSS-selector argument.
  Raw syntax alone does not make a literal multiline.
- Plain string literals and a plain multiline raw literal passed to
  File.WriteAllText are unreported; multiline literals are explicitly allowed.
- FormattableString-targeted interpolation is unreported by current CR03.
- A reduced multi-hole calendar Report interpolation containing string.Join is
  unreported because current safety planning omits it. That is an implementation
  limitation, not evidence that this complexity is accepted.
- No exact File.WriteAllText(path, interpolatedContent) case was established for
  this interview. The hypothetical file-writing example is not a source fixture.

The generalized policy remains undecided. Record the maintainer's answers in
[schedulelib-follow-up.md](schedulelib-follow-up.md); the alternatives above
are proposals, and no production source or analyzer behavior has been changed.

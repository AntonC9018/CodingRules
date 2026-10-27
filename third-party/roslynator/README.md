# Roslynator binary redistribution

Anton.CodingRules redistributes the unmodified `analyzers/dotnet/roslyn4.7/cs`
assemblies from Roslynator.Analyzers 5.0.1 (except its common code-fix assembly)
and Roslynator.Formatting.Analyzers 5.0.1 (NuGet.org). Both packages declare
Apache-2.0 and copyright 2016–2023 Josef Pihrt. LICENSE.txt and NOTICE.md are
retained from the researched source snapshot
`167f43468118e620c1a4637d83d227d0d638444d` of
https://github.com/dotnet/roslynator.
APACHE-2.0.txt supplies the complete license text from apache.org.

No upstream source or binaries were modified. CodingRules supplies an explicit
diagnostic allowlist, its own Boolean-only operator and A1 layout rules, and an
independently written RCS1016 safety suppressor and expression-to-block fixer,
under MIT.
`Roslynator.CSharp.Analyzers.CodeFixes.dll` is deliberately omitted: its RCS1016
fix drops arrow-token comments and can omit required custom task-like returns,
and the tested CLI bypasses suppressors. The CodingRules provider independently
declines these unsafe shapes even in such hosts. All 15 retained upstream DLLs
are unchanged; no upstream source is copied or modified. See
`docs/formatting.md`. The upstream common and formatting packages use distinct
assembly names for their helpers. Keep both helper dependency sets and the
formatting code-fix assembly.

# Importing CodingRules guidance into a consumer

`Anton.CodingRules` ships the self-contained `agent-guidance/agent-coding-rules.md`
alongside its analyzers, fixes and configuration. Guidance is versioned with the
NuGet package, using that package's version; it is not downloaded from the current
repository branch at agent runtime. Background web links are informative and can
evolve independently of the shipped rules. A NuGet reference alone does not make
an agent discover or read guidance.

From the consuming repository root, restore the project referencing the selected
package version, then explicitly export its guidance. For example, in a POSIX shell:

```sh
dotnet restore src/App/App.csproj
dotnet msbuild src/App/App.csproj -t:ExportCodingRulesAgentGuidance \
  -p:CodingRulesAgentGuidanceDirectory="$PWD/.codingrules"
```

In PowerShell, pass an absolute directory such as
`-p:CodingRulesAgentGuidanceDirectory="$($PWD.Path)/.codingrules"`.
The target requires this directory explicitly so a nested project does not choose
the repository root on your behalf. Relative directories are relative to the
MSBuild project. Invoke it on one project, not a solution or several projects
writing the same destination concurrently.

Export writes only `agent-coding-rules.md` in the chosen directory. It never edits
`AGENTS.md`, agent settings, or other existing instructions, and it does not run
during ordinary restore/build. Treat the exported file as generated: put local
policy in your existing instructions. Re-export after changing the package version
and review the guidance diff; export replaces that generated file, including any
local edits. Commit the exported file with the package update so agents can read
it without access to the NuGet cache or network. Do not hand-edit the NuGet cache.

Append a directive to the repository instructions your agent actually loads,
preserving their existing content:

```markdown
Read and follow `.codingrules/agent-coding-rules.md` before changing C# code,
reviewing duplication, messages or exceptions, or configuring builders.
```

`AGENTS.md` discovery is agent-specific. If your agent uses another instruction
file, put the directive there. This is an explicit instruction to open/read the
file, not an assumption that a Markdown link or NuGet reference imports it.
Verify in a fresh agent session that it finds the repository instructions and
reads the exported file. The automated checks prove the export and reference
resolve; they do not prove any particular agent's compliance.

The sample repository in `samples/AgentGuidance` contains a package reference and
an `AGENTS.md` with an existing project rule plus this read directive. Its README
gives the same two-command export workflow. No export has to overwrite the
repository's instructions.

## Verification boundary

`AgentGuidanceExportTests` exercises the shipped target from a staged package
layout without packing or restoring a consumer: ordinary build has no export
side effect, missing destinations fail clearly, explicit exports match the
canonical rules, repeated export refreshes rules, and the sample's read directive
resolves while unrelated instructions survive. The package's declared pack items
are used to stage the files. This does not replace checking the final nupkg.

For the final stable candidate only, run
`python3 build/verify-agent-guidance-consumer.py <existing-nupkg-or-directory>`.
This gate requires an existing package and never packs one. It checks the archived
guidance/import instructions and target, then runs the sample workflow from an
isolated local feed/cache. It confirms ordinary build without export, an explicit
destination requirement, exact exported bytes, refresh, and preservation of root
and directory-specific instructions. This final package execution is deferred
until after all follow-up tickets; no package gate runs during ticket 14.

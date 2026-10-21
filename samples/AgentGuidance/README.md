# Sample consuming repository

Use this directory as the root of a consuming repository. Select the desired
`Anton.CodingRules` package version in `AgentGuidance.csproj`, then run here:

```sh
dotnet restore AgentGuidance.csproj
dotnet msbuild AgentGuidance.csproj -t:ExportCodingRulesAgentGuidance \
  -p:CodingRulesAgentGuidanceDirectory="$PWD/.codingrules"
```

The existing `AGENTS.md` tells agents to open and read the exported file. Export
does not alter its application-specific rule or read directive. Keep both
`AGENTS.md` and `.codingrules/agent-coding-rules.md` in the consumer repository;
re-export and review the diff when updating the package version. A package
reference alone does not load agent instructions. If your agent uses another
instruction entry point, preserve its content and add the read directive there.

Before publication, use the final candidate from a local feed when restoring.
Do not assume the public `0.1.0` package already contains these new files.
See `docs/consumer-agent-guidance.md` in the CodingRules source, or the same
file under `agent-guidance` in the installed package, for import semantics and
verification boundaries.

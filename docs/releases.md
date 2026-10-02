# CI and NuGet releases

Merge and publish SourceGenerators PR #10 first. CodingRules restores
`Anton.SourceGeneration.Sdk` 1.1.0 and its helper packages from NuGet.org.
CI does not clone SourceGenerators or use a development feed/cache.

`ci.yml` runs on every pull request, regardless of its base branch, and on
pushes to `main`. Require the stable **Build, test, and package** check with
up-to-date branches. The complete analyzer/code-fix suite includes a packaged
code-fix consumer. CI then packs a release candidate, tests its exact bytes in
a fresh consumer (diagnostic fixture fails with CR0001; fixed fixture builds),
and records the package version, commit, run/attempt and SHA-256 in a manifest.
Successful main runs retain the nupkg and manifest together for 90 days.

## One-time setup

- Set the repository variable `NUGET_USER` to the NuGet profile name
  `AntonC9018`.
- Create the GitHub environment `release`. Restrict who can publish releases
  and approve deployments according to the repository's maintainer policy.
- On NuGet.org, add a trusted publishing policy owned by `AntonC9018`:
  repository owner `AntonC9018`, repository `CodingRules`, workflow file
  `release.yml` (file name only), environment `release`, package scope
  `Anton.CodingRules`. No long-lived API key is used.

NuGet's [trusted publishing documentation](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
describes the policy and `NuGet/login@v1` exchange.

## Publish a version

1. Change `Version` in `Package/CodingRules.Package.csproj` to the new NuGet
   version, merge through a passing PR, and wait for successful push CI on main.
2. Manually create and publish a GitHub release with tag `v<Version>` pointing
   at that exact passing commit. An older commit still reachable from main is
   allowed; the current main tip is not required. A tag push alone does not
   publish anything.
3. `release.yml` checks the published event's tag commit, source version and
   main ancestry, finds a successful
   `ci.yml` **push** run for that exact commit, and downloads that run attempt's
   immutable package artifact. It verifies the archive digest, manifest,
   nupkg hash, nuspec version and repository commit before requesting a
   temporary NuGet key and pushing the already tested nupkg.

There is no release rebuild or automatic GitHub release creation. Artifact
expiry or a missing successful run fails publishing. Rerun the original main
push CI to obtain a retained artifact for the same commit, then rerun the
release job. Publishing uses `--skip-duplicate` so rerunning a completed
release is safe; always choose a new package version for changed bytes.

The guard uses GitHub's [workflow-run API](https://docs.github.com/en/rest/actions/workflow-runs)
and [artifact API](https://docs.github.com/en/rest/actions/artifacts) to bind
the archive to the workflow, event, repository, commit and successful attempt.

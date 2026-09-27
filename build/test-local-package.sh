#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
feed="$repository_root/artifacts/local-feed"
package="$feed/Anton.CodingRules.0.1.0.nupkg"
if [[ ! -f "$package" ]]; then
    echo "Pack the CodingRules.Package project into $feed first." >&2
    exit 1
fi

python3 - "$package" <<'PY'
from sys import argv
from zipfile import ZipFile

expected = {
    "analyzers/dotnet/cs/CodingRules.Analyzers.dll",
    "analyzers/dotnet/cs/CodingRules.CodeFixes.dll",
    "analyzers/dotnet/cs/CodingRules.Shared.dll",
    "analyzers/dotnet/cs/Anton.SourceGeneration.dll",
    "analyzers/dotnet/cs/Anton.Utils.Shared.dll",
}
with ZipFile(argv[1]) as package:
    missing = expected.difference(package.namelist())
if missing:
    raise SystemExit("Missing package files: " + ", ".join(sorted(missing)))
PY

consumer_root="$(mktemp -d)"
trap 'rm -rf -- "$consumer_root"' EXIT
cp "$repository_root/global.json" "$consumer_root/global.json"

cat > "$consumer_root/Consumer.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Anton.CodingRules" Version="0.1.0" />
  </ItemGroup>
</Project>
XML

cat > "$consumer_root/Consumer.cs" <<'CS'
public static class Consumer
{
    public static bool Check(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        return value.Contains('x');
    }
}
CS

cat > "$consumer_root/NuGet.Config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

if output="$(cd "$consumer_root" && NUGET_PACKAGES="$consumer_root/packages" dotnet build Consumer.csproj --nologo 2>&1)"; then
    echo "The package consumer built without the expected CR0001 diagnostic." >&2
    exit 1
fi

if [[ "$output" != *"error CR0001"* ]]; then
    echo "$output" >&2
    exit 1
fi

(
    cd "$consumer_root"
    NUGET_PACKAGES="$consumer_root/packages" dotnet format analyzers Consumer.csproj \
        --diagnostics CR0001 --no-restore --verbosity quiet
)

if ! grep -q "if (value.Contains('x'))" "$consumer_root/Consumer.cs"; then
    echo "The package code fix did not rewrite the return decision." >&2
    exit 1
fi

(
    cd "$consumer_root"
    NUGET_PACKAGES="$consumer_root/packages" dotnet build Consumer.csproj --nologo
)

echo "The local package emitted CR0001 and its code fix repaired a fresh consumer."

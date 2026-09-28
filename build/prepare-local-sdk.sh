#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source_generators_root="${1:-${SOURCE_GENERATORS_REPO:-}}"
if [[ -z "$source_generators_root" ]]; then
    echo "Usage: $0 /path/to/SourceGenerators" >&2
    exit 1
fi

source_generators_root="$(cd -- "$source_generators_root" && pwd)"
feed="$repository_root/artifacts/local-feed"
mkdir -p "$feed"

(
    cd "$source_generators_root"
    dotnet pack source/Utils.Shared/Utils.Shared.csproj \
        --configuration Release --output "$feed"
    dotnet pack source/SourceGeneration/SourceGeneration.csproj \
        --configuration Release --output "$feed"
    dotnet pack source/SourceGeneration.RoslynTesting/SourceGeneration.RoslynTesting.csproj \
        --configuration Release --output "$feed"
    dotnet pack source/SourceGeneration.Sdk/SourceGeneration.Sdk.csproj \
        --configuration Release --output "$feed"
    dotnet pack source/SourceGeneration.PackageTesting/SourceGeneration.PackageTesting.csproj \
        --configuration Release --output "$feed"
)

cache_root="$(realpath -m "$repository_root/artifacts/nuget-packages")"
if [[ "$cache_root" != "$repository_root/artifacts/nuget-packages" ]]; then
    echo "The local NuGet cache must stay inside this checkout." >&2
    exit 1
fi

for package in anton.sourcegeneration.sdk anton.sourcegeneration anton.sourcegeneration.roslyntesting anton.sourcegeneration.packagetesting anton.utils.shared; do
    cache_package="$(realpath -m "$cache_root/$package")"
    if [[ "$cache_package" != "$cache_root/$package" ]]; then
        echo "The local NuGet cache package must stay inside this checkout." >&2
        exit 1
    fi

    rm -rf -- "$cache_package"
done

echo "SDK feed ready: $feed"

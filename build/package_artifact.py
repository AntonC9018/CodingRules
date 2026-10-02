"""Smoke-test the release nupkg, then record its bytes and CI provenance."""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile

PACKAGE_ID = "Anton.CodingRules"
ROOT = Path(__file__).resolve().parent.parent


def require(condition, message):
    if not condition:
        raise ValueError(message)


def source_version(xml):
    project = ET.fromstring(xml)
    versions = project.findall("./PropertyGroup/Version")
    require(len(versions) == 1, "Expected one explicit package version in source")
    return versions[0].text


def package_metadata(path):
    with ZipFile(path) as package:
        specs = [name for name in package.namelist() if name.endswith(".nuspec")]
        require(len(specs) == 1, "Expected exactly one nuspec")
        spec = ET.fromstring(package.read(specs[0]))
        ns = {"n": spec.tag.split("}")[0].lstrip("{")}
        metadata = spec.find("n:metadata", ns)
        package_id = metadata.findtext("n:id", namespaces=ns)
        version = metadata.findtext("n:version", namespaces=ns)
        repository = metadata.find("n:repository", ns)
        commit = repository.get("commit") if repository is not None else None
        required = {
            "analyzers/dotnet/cs/CodingRules.Analyzers.dll",
            "analyzers/dotnet/cs/CodingRules.CodeFixes.dll",
            "analyzers/dotnet/cs/CodingRules.Shared.dll",
        }
        require(required <= set(package.namelist()), "Analyzer/code fix assemblies missing")
    require(package_id == PACKAGE_ID, "Unexpected package identity")
    return version, commit


def smoke_test(package, version):
    with tempfile.TemporaryDirectory(prefix="codingrules-package-") as directory:
        consumer = Path(directory)
        feed = consumer / "feed"
        feed.mkdir()
        shutil.copy2(package, feed)
        shutil.copy2(ROOT / "global.json", consumer)
        shutil.copy2(ROOT / "CodeFixTests/Consumers/ExplicitReturnDecision/Consumer.cs", consumer)
        project = ET.parse(ROOT / "CodeFixTests/Consumers/ExplicitReturnDecision/Consumer.csproj")
        project.find("./ItemGroup/PackageReference").set("Version", version)
        project.write(consumer / "Consumer.csproj", encoding="utf-8")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="tested-package", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8")
        env = dict(os.environ, NUGET_PACKAGES=str(consumer / "packages"))
        command = ["dotnet", "build", "Consumer.csproj", "-c", "Release", "--no-incremental"]
        result = subprocess.run(command, cwd=consumer, env=env, text=True, capture_output=True)
        require(result.returncode != 0, "Packaged analyzer did not reject the diagnostic fixture")
        require("error CR0001" in result.stdout, result.stdout + result.stderr)
        shutil.copyfile(ROOT / "CodeFixTests/Expected/ExplicitReturnDecision.Fixed.cs.txt", consumer / "Consumer.cs")
        subprocess.run(command, cwd=consumer, env=env, check=True)


def main():
    directory = Path(sys.argv[1]).resolve()
    packages = list(directory.glob("*.nupkg"))
    require(len(packages) == 1, "Expected one release nupkg")
    package = packages[0]
    version, repository_commit = package_metadata(package)
    require(version == source_version((ROOT / "Package/CodingRules.Package.csproj").read_text()),
            "Package version differs from source")
    commit = os.environ.get("GITHUB_SHA") or subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    require(repository_commit == commit, "Package repository commit differs from tested source")
    smoke_test(package, version)
    manifest = {
        "schema": 1,
        "repository": os.environ.get("GITHUB_REPOSITORY", "AntonC9018/CodingRules"),
        "commit": commit,
        "run_id": int(os.environ.get("GITHUB_RUN_ID", "0")),
        "run_attempt": int(os.environ.get("GITHUB_RUN_ATTEMPT", "1")),
        "package_id": PACKAGE_ID,
        "version": version,
        "file": package.name,
        "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
    }
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Tested {package.name} at {commit}; SHA-256 {manifest['sha256']}")


if __name__ == "__main__":
    main()

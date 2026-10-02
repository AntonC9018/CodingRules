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
SCRIPT_PATH = Path(__file__)
RESOLVED_SCRIPT_PATH = SCRIPT_PATH.resolve()
ROOT = RESOLVED_SCRIPT_PATH.parent.parent


def require(condition, message):
    if not condition:
        raise ValueError(message)


def source_version(xml):
    project = ET.fromstring(xml)
    versions = project.findall("./PropertyGroup/Version")
    version_count = len(versions)
    require(version_count == 1, "Expected one explicit package version in source")
    return versions[0].text


def package_metadata(path):
    with ZipFile(path) as package:
        entries = package.namelist()
        specs = [name for name in entries if name.endswith(".nuspec")]
        spec_count = len(specs)
        require(spec_count == 1, "Expected exactly one nuspec")
        spec_xml = package.read(specs[0])
        spec = ET.fromstring(spec_xml)
        tag_parts = spec.tag.split("}")
        namespace = tag_parts[0].lstrip("{")
        ns = {"n": namespace}
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
        entry_names = set(entries)
        require(required <= entry_names, "Analyzer/code fix assemblies missing")
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
        package_reference = project.find("./ItemGroup/PackageReference")
        package_reference.set("Version", version)
        project.write(consumer / "Consumer.csproj", encoding="utf-8")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        feed_path = str(feed)
        ET.SubElement(sources, "add", key="tested-package", value=feed_path)
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        config_tree = ET.ElementTree(config)
        config_tree.write(consumer / "NuGet.Config", encoding="utf-8")
        packages_directory = consumer / "packages"
        packages_path = str(packages_directory)
        env = dict(os.environ, NUGET_PACKAGES=packages_path)
        command = ["dotnet", "build", "Consumer.csproj", "-c", "Release", "--no-incremental"]
        result = subprocess.run(command, cwd=consumer, env=env, text=True, capture_output=True)
        require(result.returncode != 0, "Packaged analyzer did not reject the diagnostic fixture")
        require("error CR0001" in result.stdout, result.stdout + result.stderr)
        shutil.copyfile(ROOT / "CodeFixTests/Expected/ExplicitReturnDecision.Fixed.cs.txt", consumer / "Consumer.cs")
        subprocess.run(command, cwd=consumer, env=env, check=True)


def main():
    output_path = Path(sys.argv[1])
    directory = output_path.resolve()
    package_paths = directory.glob("*.nupkg")
    packages = list(package_paths)
    package_count = len(packages)
    require(package_count == 1, "Expected one release nupkg")
    package = packages[0]
    version, repository_commit = package_metadata(package)
    project_path = ROOT / "Package/CodingRules.Package.csproj"
    project_xml = project_path.read_text()
    expected_version = source_version(project_xml)
    require(version == expected_version, "Package version differs from source")
    commit = os.environ.get("GITHUB_SHA")
    if not commit:
        commit_output = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True)
        commit = commit_output.strip()
    require(repository_commit == commit, "Package repository commit differs from tested source")
    smoke_test(package, version)
    repository = os.environ.get("GITHUB_REPOSITORY", "AntonC9018/CodingRules")
    run_id_text = os.environ.get("GITHUB_RUN_ID", "0")
    run_id = int(run_id_text)
    run_attempt_text = os.environ.get("GITHUB_RUN_ATTEMPT", "1")
    run_attempt = int(run_attempt_text)
    package_bytes = package.read_bytes()
    package_hash = hashlib.sha256(package_bytes)
    package_digest = package_hash.hexdigest()
    manifest = {
        "schema": 1,
        "repository": repository,
        "commit": commit,
        "run_id": run_id,
        "run_attempt": run_attempt,
        "package_id": PACKAGE_ID,
        "version": version,
        "file": package.name,
        "sha256": package_digest,
    }
    manifest_json = json.dumps(manifest, indent=2)
    manifest_text = manifest_json + "\n"
    manifest_path = directory / "manifest.json"
    manifest_path.write_text(manifest_text)
    message = f"Tested {package.name} at {commit}; SHA-256 {manifest['sha256']}"
    print(message)


if __name__ == "__main__":
    main()

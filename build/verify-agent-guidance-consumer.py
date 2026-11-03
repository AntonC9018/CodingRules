#!/usr/bin/env python3
"""Verify explicit agent-guidance import from one existing stable candidate package."""

import argparse
import os
from pathlib import Path
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile

from consumer_fixture import FORMATTING, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="Existing nupkg or directory; never packs a new candidate")
    arguments = parser.parse_args()
    try:
        package = FORMATTING.select_package(arguments.package)
    except ValueError as error:
        parser.error(str(error))
    with ZipFile(package) as archive:
        metadata = ET.fromstring(archive.read("Anton.CodingRules.nuspec"))
        version = metadata.find("{*}metadata/{*}version").text
        rules = archive.read("agent-guidance/agent-coding-rules.md")
        assert archive.read("agent-guidance/consumer-agent-guidance.md")
        assert archive.read("buildTransitive/Anton.CodingRules.targets")

    with tempfile.TemporaryDirectory(prefix="codingrules-agent-guidance-") as temporary:
        work = Path(temporary)
        feed = work / "feed"
        feed.mkdir()
        shutil.copyfile(package, feed / package.name)
        shutil.copyfile(ROOT / "global.json", work / "global.json")
        consumer = work / "sample repository"
        shutil.copytree(ROOT / "samples/AgentGuidance", consumer,
                        ignore=shutil.ignore_patterns("bin", "obj", ".codingrules"))
        project_file = consumer / "AgentGuidance.csproj"
        project = ET.parse(project_file)
        project.find("ItemGroup/PackageReference").set("Version", version)
        project.write(project_file, encoding="utf-8")
        configuration = ET.Element("configuration")
        sources = ET.SubElement(configuration, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="candidate", value=str(feed))
        ET.ElementTree(configuration).write(work / "NuGet.Config", encoding="utf-8")
        environment = dict(os.environ, NUGET_PACKAGES=str(work / "packages"))
        run = FORMATTING.run
        instructions_file = consumer / "AGENTS.md"
        instructions = instructions_file.read_bytes()
        relative_path = re.search(r"Read and follow `([^`]+)`", instructions.decode()).group(1)
        exported = consumer / relative_path

        run(["dotnet", "restore", str(project_file)], consumer, environment)
        run(["dotnet", "build", str(project_file), "--no-restore"], consumer, environment)
        assert not exported.exists(), "Ordinary restore/build must not export instructions"
        assert instructions_file.read_bytes() == instructions
        command = ["dotnet", "msbuild", str(project_file), "-t:ExportCodingRulesAgentGuidance"]
        output = run(command, consumer, environment, expected_returncode=1)
        assert "Set CodingRulesAgentGuidanceDirectory" in output, output
        command.append(f"-p:CodingRulesAgentGuidanceDirectory={exported.parent}")
        run(command, consumer, environment)
        assert exported.read_bytes() == rules

        other_instructions = exported.parent / "AGENTS.md"
        other_instructions.write_text("Preserve directory-specific instructions.\n")
        exported.write_text("A local edit to the generated guidance.\n")
        run(command, consumer, environment)
        assert exported.read_bytes() == rules, "Re-export must refresh generated guidance"
        assert instructions_file.read_bytes() == instructions
        assert other_instructions.read_text() == "Preserve directory-specific instructions.\n"
        print(f"PASS: Anton.CodingRules {version} guidance exported explicitly; sample read directive resolves; "
              "normal build, required destination, refresh and unrelated instructions verified.")


if __name__ == "__main__":
    main()

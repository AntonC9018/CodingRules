#!/usr/bin/env python3
"""Pack and exercise the actual analyzer/code-fix assets in an isolated consumer."""

from collections import Counter
import argparse
import difflib
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parents[1]
DIAGNOSTICS = ["RCS0042", "RCS1016", "CR0800"]


def run(arguments, cwd, environment):
    print("$ " + " ".join(arguments), flush=True)
    result = subprocess.run(arguments, cwd=cwd, env=environment, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    print(result.stdout, flush=True)
    result.check_returncode()
    return result.stdout


def select_package(path):
    if not path.is_dir():
        return path
    packages = sorted(package for package in path.glob("Anton.CodingRules.*.nupkg")
                      if package.is_file() and not package.name.endswith(".symbols.nupkg"))
    if len(packages) != 1:
        names = ", ".join(package.name for package in packages) or "none"
        raise ValueError(f"Expected exactly one non-symbol Anton.CodingRules nupkg in {path}; "
                         f"found {len(packages)}: {names}")
    return packages[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", nargs="?", type=Path,
                        help="Verify an existing nupkg or package directory instead of packing")
    arguments = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="codingrules-formatting-consumer-") as temporary:
        work = Path(temporary)
        shutil.copyfile(ROOT / "global.json", work / "global.json")
        feed = work / "feed"
        feed.mkdir()
        environment = dict(os.environ)
        if arguments.package is None:
            run(["dotnet", "pack", "Package/CodingRules.Package.csproj", "-c", "Release",
                 "--output", str(feed)], ROOT, environment)
        source = feed if arguments.package is None else arguments.package
        try:
            package = select_package(source)
        except ValueError as error:
            parser.error(str(error))
        if arguments.package is not None:
            destination = feed / package.name
            shutil.copyfile(package, destination)
            package = destination
        with ZipFile(package) as archive:
            names = set(archive.namelist())
            metadata = ET.fromstring(archive.read("Anton.CodingRules.nuspec"))
            version = metadata.find("{*}metadata/{*}version").text
            assert metadata.find("{*}metadata/{*}license").text == "MIT AND Apache-2.0"
            for name in ("LICENSE.txt", "NOTICE.md", "APACHE-2.0.txt", "README.md"):
                assert "third-party/roslynator/" + name in names, name
            assert "buildTransitive/Anton.CodingRules.props" in names
            assert "config/CodingRules.globalconfig" in names
            assert "analyzers/dotnet/cs/Roslynator.CSharp.Analyzers.CodeFixes.dll" not in names
            assert "analyzers/dotnet/cs/Roslynator.Formatting.Analyzers.CodeFixes.dll" in names
            assert sum(name.startswith("analyzers/dotnet/cs/Roslynator")
                       and name.endswith(".dll") for name in names) == 15

        consumer = work / "consumer"
        shutil.copytree(ROOT / "CodeFixTests/Consumers/Formatting", consumer,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        project_xml = ET.parse(consumer / "Consumer.csproj")
        project_xml.find("ItemGroup/PackageReference").set("Version", version)
        project_xml.write(consumer / "Consumer.csproj", encoding="utf-8")
        (work / "NuGet.Config").write_text(f"""<configuration>
  <packageSources>
    <clear />
    <add key="test-feed" value="{feed}" />
  </packageSources>
</configuration>
""")
        # The archive must stand alone. No upstream package cache or remote feed.
        environment["NUGET_PACKAGES"] = str(work / "packages")
        project = "Consumer.csproj"
        run(["dotnet", "restore", project], consumer, environment)
        output = run(["dotnet", "build", project, "--no-restore"], consumer, environment)
        reports = set(re.findall(r"([^/\\\s]+\.cs)\((\d+),(\d+)\): warning (\w+):", output))
        counts = Counter(report[3] for report in reports)
        assert counts == {"RCS0042": 1, "RCS1016": 6, "CR0800": 2}, counts
        assert not re.search(r"warning (?:AD|CS|ROS)\d+", output), output

        # The SDK CLI's analyzer filter overlooks global diagnostic severities.
        # Exercise the documented setup using the template actually shipped.
        with ZipFile(package) as archive:
            template = archive.read("config/CodingRules.Formatting.editorconfig")
        (consumer / ".editorconfig").write_bytes(b"root = true\n\n" + template)

        command = ["dotnet", "format", "analyzers", project, "--no-restore",
                   "--diagnostics", *DIAGNOSTICS]
        run(command, consumer, environment)
        run(["dotnet", "format", "whitespace", project, "--no-restore"], consumer, environment)
        source_path = consumer / "Consumer.cs"
        task_like_path = consumer / "TaskLike.cs"
        task_like_original = (ROOT / "CodeFixTests/Consumers/Formatting/TaskLike.cs").read_bytes()
        assert task_like_path.read_bytes() == task_like_original, "Custom async contracts must remain unchanged"
        fixed = source_path.read_bytes()
        expected = (ROOT / "CodeFixTests/Expected/Formatting.Fixed.cs.txt").read_bytes()
        difference = "".join(difflib.unified_diff(expected.decode().splitlines(keepends=True),
                                               fixed.decode().splitlines(keepends=True),
                                               fromfile="expected", tofile="actual"))
        assert fixed == expected, difference
        run([*command, "--verify-no-changes"], consumer, environment)
        run(command, consumer, environment)
        run(["dotnet", "format", "whitespace", project, "--no-restore", "--verify-no-changes"],
            consumer, environment)
        assert source_path.read_bytes() == fixed
        assert task_like_path.read_bytes() == task_like_original
        run(["dotnet", "build", project, "--no-restore", "-warnaserror"], consumer, environment)
        print("PASS: exact diagnostics, exported CLI fixes, formatter and second-pass stability; isolated package.")


if __name__ == "__main__":
    main()

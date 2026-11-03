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
DIAGNOSTICS = ["RCS0042", "RCS1016", "CR0800", "CR0801", "CR0802", "CR0806"]
SOURCE_WARNING = re.compile(r"([^/\\\s]+\.cs)\((\d+),(\d+)\): warning (\w+):")


def run(arguments, cwd, environment, expected_returncode=0):
    print("$ " + " ".join(arguments), flush=True)
    result = subprocess.run(arguments, cwd=cwd, env=environment, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    print(result.stdout, flush=True)
    if expected_returncode == 0:
        result.check_returncode()
    else:
        assert result.returncode == expected_returncode, (result.returncode, expected_returncode)
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
        reports = set(SOURCE_WARNING.findall(output))
        counts = Counter(report[3] for report in reports)
        assert counts == {"RCS0042": 1, "RCS1016": 6, "CR0800": 2, "CR0801": 10,
                          "CR0802": 5, "CR0806": 5}, counts
        assert not re.search(r"warning (?:AD|CS|ROS)\d+", output), output
        original_behavior = run(["dotnet", "run", "--project", project, "--no-build"], consumer, environment)
        assert original_behavior.splitlines() == ["123345667890123", "new CallerPayload(line: 0) { A = 1, B = 2 }", "18", "19", "27", "42"], original_behavior

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
        initializer_path = consumer / "InitializerCalls.cs"
        initializer_fixed = initializer_path.read_bytes()
        initializer_expected = (ROOT / "CodeFixTests/Expected/InitializerCalls.Fixed.cs.txt").read_bytes()
        initializer_difference = "".join(difflib.unified_diff(
            initializer_expected.decode().splitlines(keepends=True), initializer_fixed.decode().splitlines(keepends=True),
            fromfile="initializer expected", tofile="initializer actual"))
        assert initializer_fixed == initializer_expected, initializer_difference
        expected = (ROOT / "CodeFixTests/Expected/Formatting.Fixed.cs.txt").read_bytes()
        difference = "".join(difflib.unified_diff(expected.decode().splitlines(keepends=True),
                                               fixed.decode().splitlines(keepends=True),
                                               fromfile="expected", tofile="actual"))
        assert fixed == expected, difference
        caller_path = consumer / "CallerInformation.cs"
        caller_fixed = caller_path.read_bytes()
        caller_expected = (ROOT / "CodeFixTests/Expected/CallerInformation.Fixed.cs.txt").read_bytes()
        assert caller_fixed == caller_expected, "Caller-information cases must keep unsafe layouts and fix safe ones"
        expression_path = consumer / "CallerExpressions.cs"
        expression_fixed = expression_path.read_bytes()
        assert expression_fixed == (ROOT / "CodeFixTests/Expected/CallerExpressions.Fixed.cs.txt").read_bytes()
        chopped_path = consumer / "ChoppedArguments.cs"
        chopped_fixed = chopped_path.read_bytes()
        chopped_expected = (ROOT / "CodeFixTests/Expected/ChoppedArguments.Fixed.cs.txt").read_bytes()
        chopped_difference = "".join(difflib.unified_diff(
            chopped_expected.decode().splitlines(keepends=True), chopped_fixed.decode().splitlines(keepends=True),
            fromfile="chopped arguments expected", tofile="chopped arguments actual"))
        assert chopped_fixed == chopped_expected, chopped_difference
        observers_path = consumer / "ChoppedArgumentObservers.cs"
        observers_fixed = observers_path.read_bytes()
        observers_expected = (ROOT / "CodeFixTests/Expected/ChoppedArgumentObservers.Fixed.cs.txt").read_bytes()
        observers_difference = "".join(difflib.unified_diff(
            observers_expected.decode().splitlines(keepends=True), observers_fixed.decode().splitlines(keepends=True),
            fromfile="chopped argument observers expected", tofile="chopped argument observers actual"))
        assert observers_fixed == observers_expected, observers_difference
        caller_reports = {
            ("CallerExpressions.cs", "9", "62", "CR0801"),
            ("CallerInformation.cs", "17", "12", "CR0802"),
            ("CallerInformation.cs", "19", "31", "CR0801"),
            ("ChoppedArgumentObservers.cs", "11", "23", "CR0806"),
            ("ChoppedArgumentObservers.cs", "13", "37", "CR0806"),
        }
        output = run(["dotnet", "format", "analyzers", project, "--no-restore", "--diagnostics",
                      "CR0801", "CR0802", "CR0806", "--verify-no-changes"], consumer, environment,
                     expected_returncode=2)
        assert set(SOURCE_WARNING.findall(output)) == caller_reports, output
        # This CLI bypasses suppressors, but newer SDK feature bands omit
        # manual-only diagnostics that offer no action. The golden bytes and
        # exact caller-information diagnostic set are the invariant; only the
        # protected manual diagnostics below may also be reported.
        manual_reports = set()
        for filename, content in (("Consumer.cs", expected.decode()),
                                  ("TaskLike.cs", task_like_original.decode())):
            for number, line in enumerate(content.splitlines(), start=1):
                if "=> /* preserve" in line or "=> // preserve" in line or "async MyTask<int>" in line:
                    manual_reports.add((filename, str(number), str(line.index("=>") + 1), "RCS1016"))
        assert len(manual_reports) == 8
        output = run([*command, "--verify-no-changes"], consumer, environment, expected_returncode=2)
        reports = set(SOURCE_WARNING.findall(output))
        assert caller_reports <= reports <= manual_reports | caller_reports, output
        run(command, consumer, environment)
        run(["dotnet", "format", "whitespace", project, "--no-restore", "--verify-no-changes"],
            consumer, environment)
        assert source_path.read_bytes() == fixed
        assert initializer_path.read_bytes() == initializer_fixed
        assert task_like_path.read_bytes() == task_like_original
        assert caller_path.read_bytes() == caller_fixed
        assert expression_path.read_bytes() == expression_fixed
        assert chopped_path.read_bytes() == chopped_fixed
        assert observers_path.read_bytes() == observers_fixed
        output = run(["dotnet", "build", project, "--no-restore", "-warnaserror",
                      "-p:WarningsNotAsErrors=CR0801%3BCR0802%3BCR0806"], consumer, environment)
        assert set(SOURCE_WARNING.findall(output)) == caller_reports, output
        assert run(["dotnet", "run", "--project", project, "--no-build"], consumer, environment) == original_behavior
        print("PASS: exact diagnostics, exported CLI fixes, formatter and second-pass stability; isolated package.")


if __name__ == "__main__":
    main()

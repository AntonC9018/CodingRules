#!/usr/bin/env python3
"""Exercise manual diagnostic fixtures through the exact isolated consumer package."""

import argparse
import importlib.util
import os
from pathlib import Path
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parents[1]
# Use the existing package selector and command runner, including their failure handling.
SPEC = importlib.util.spec_from_file_location("formatting_consumer", ROOT / "build/verify-formatting-consumer.py")
FORMATTING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FORMATTING)
REPORT = re.compile(r"([^/\\\s]+\.cs)\((\d+),(\d+)\): (warning|error) (\w+):")


def verify_manual_consumer(fixture_name, diagnostic_id, expected_count, generated_source, description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("package", nargs="?", type=Path, help="Existing nupkg or package directory; otherwise pack")
    arguments = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="codingrules-manual-consumer-") as temporary:
        work = Path(temporary)
        feed = work / "feed"
        feed.mkdir()
        environment = dict(os.environ)
        run = FORMATTING.run
        if arguments.package is None:
            run(["dotnet", "pack", "Package/CodingRules.Package.csproj", "-c", "Release", "--output", str(feed)],
                ROOT, environment)
        try:
            package = FORMATTING.select_package(arguments.package or feed)
        except ValueError as error:
            parser.error(str(error))
        if arguments.package is not None:
            shutil.copyfile(package, feed / package.name)
        with ZipFile(package) as archive:
            metadata = ET.fromstring(archive.read("Anton.CodingRules.nuspec"))
            version = metadata.find("{*}metadata/{*}version").text
            assert "analyzers/dotnet/cs/CodingRules.Analyzers.dll" in archive.namelist()
        shutil.copyfile(ROOT / "global.json", work / "global.json")
        consumer = work / "consumer"
        shutil.copytree(ROOT / "CodeFixTests/Consumers" / fixture_name, consumer,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        project = ET.parse(consumer / "Consumer.csproj")
        project.find("ItemGroup/PackageReference").set("Version", version)
        project.write(consumer / "Consumer.csproj", encoding="utf-8")
        (work / "NuGet.Config").write_text(f"""<configuration>
  <packageSources><clear /><add key="test-feed" value="{feed}" /></packageSources>
</configuration>
""")
        # No cache or remote feeds can supply missing analyzer/package assets.
        environment["NUGET_PACKAGES"] = str(work / "packages")
        config = consumer / ".editorconfig"
        template = """root = true

[*.cs]
dotnet_analyzer_diagnostic.severity = none
dotnet_diagnostic.RCS0042.severity = none
dotnet_diagnostic.RCS1016.severity = none
dotnet_diagnostic.{diagnostic_id}.severity = {severity}
"""
        config.write_text(template.format(diagnostic_id=diagnostic_id, severity="warning"))
        (consumer / "Generated.g.cs").write_text(generated_source)
        sources = {path.name: path.read_bytes() for path in consumer.glob("*.cs")}
        expected = set()
        marker = f"// {diagnostic_id}: "
        for number, line in enumerate(sources["Findings.cs"].decode().splitlines(), start=1):
            if marker in line:
                prefix, anchor = line.split(marker, 1)
                expected.add(("Findings.cs", str(number), str(prefix.index(anchor) + 1), "warning", diagnostic_id))
        assert len(expected) == expected_count, expected
        run(["dotnet", "restore", "Consumer.csproj"], consumer, environment)
        build = ["dotnet", "build", "Consumer.csproj", "--no-restore", "--no-incremental"]
        output = run(build, consumer, environment)
        assert set(REPORT.findall(output)) == expected, output

        # A manual architectural diagnostic must leave source unchanged in a fixer host.
        run(["dotnet", "format", "analyzers", "Consumer.csproj", "--no-restore", "--diagnostics", diagnostic_id],
            consumer, environment)
        assert {path.name: path.read_bytes() for path in consumer.glob("*.cs")} == sources
        output = run([*build, f"-warnaserror:{diagnostic_id}"], consumer, environment, expected_returncode=1)
        errors = {(name, line, column, "error", rule) for name, line, column, _, rule in expected}
        assert set(REPORT.findall(output)) == errors, output

        # Exercise ordinary editorconfig transport through the real package, not an in-memory option stub.
        config.write_text(template.format(diagnostic_id=diagnostic_id, severity="none"))
        output = run([*build, "-warnaserror"], consumer, environment)
        assert not REPORT.findall(output), output
        config.write_text(template.format(diagnostic_id=diagnostic_id, severity="error"))
        output = run(build, consumer, environment, expected_returncode=1)
        assert set(REPORT.findall(output)) == errors, output
        print(f"PASS: {expected_count} exact {diagnostic_id} findings; accepted and generated cases; "
              "manual-only source stability, warnings-as-errors and editorconfig severity; isolated package.")

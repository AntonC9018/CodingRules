#!/usr/bin/env python3
"""Exercise manual diagnostic fixtures through the exact isolated consumer package."""

import argparse
from pathlib import Path
import re

from consumer_fixture import packaged_consumer


REPORT = re.compile(r"([^/\\\s]+\.cs)\((\d+),(\d+)\): (warning|error) (\w+):")


def verify_manual_consumer(fixture_name, diagnostic_id, expected_count, generated_source, description,
                           unresolved_fixture=None):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("package", nargs="?", type=Path, help="Existing nupkg or package directory; otherwise pack")
    arguments = parser.parse_args()
    with packaged_consumer(fixture_name, arguments.package, parser) as (consumer, environment, run):
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
        if unresolved_fixture is not None:
            config.write_text(template.format(diagnostic_id=diagnostic_id, severity="warning"))
            unresolved_source = (consumer / unresolved_fixture).read_text()
            (consumer / "Unresolved.cs").write_text(unresolved_source)
            unresolved_errors = set()
            for number, line in enumerate(unresolved_source.splitlines(), start=1):
                if "// CS0246: " in line:
                    prefix, anchor = line.split("// CS0246: ", 1)
                    unresolved_errors.add(("Unresolved.cs", str(number), str(prefix.index(anchor) + 1), "error", "CS0246"))
            assert unresolved_errors, "Missing unresolved-signature expectations"
            output = run(build, consumer, environment, expected_returncode=1)
            assert set(REPORT.findall(output)) == expected | unresolved_errors, output
            print("PASS: unresolved signatures produce only their expected compiler errors; no extra analyzer findings.")

        print(f"PASS: {expected_count} exact {diagnostic_id} findings; accepted and generated cases; "
              "manual-only source stability, warnings-as-errors and editorconfig severity; isolated package.")

#!/usr/bin/env python3
"""Verify builder/positional MSBuild transport and braces/cast fixes from one package."""
import argparse
from pathlib import Path
from consumer_fixture import packaged_consumer
from manual_consumer import REPORT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package', type=Path)
    args = parser.parse_args()
    with packaged_consumer('PolicyRefinements', args.package, parser) as (consumer, environment, run):
        external = consumer.parent / 'external'
        external.mkdir()
        (external / 'External.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        (external / 'Api.cs').write_text('namespace External { public static class Api {'
            'public static bool Compare(int a,int b)=>a==b; public static bool Same(int a,int b)=>a==b; } }')
        project = consumer / 'Consumer.csproj'
        text = project.read_text().replace('</Project>', '<ItemGroup><ProjectReference Include="../external/External.csproj" /></ItemGroup></Project>')
        text = text.replace('</PropertyGroup>', '<CodingRulesAllowPositionalArguments>External::M:External.Api.Same(System.Int32,System.Int32)~System.Boolean</CodingRulesAllowPositionalArguments></PropertyGroup>')
        project.write_text(text)
        (consumer / '.editorconfig').write_text('root=true\n[*.cs]\ndotnet_analyzer_diagnostic.severity=none\n'
            + ''.join(f'dotnet_diagnostic.{rule}.severity=warning\n' for rule in ['CR0500','CR0803','CR0804','CR1001']))
        build = ['dotnet', 'build', 'Consumer.csproj', '-t:Rebuild', '-v:minimal']
        output = run(build, consumer, environment)
        rules = {match[4] for match in REPORT.findall(output)}
        assert rules == {'CR0803', 'CR0804', 'CR1001'}, output
        # Default builder and explicit marker exempt their declaring types.
        assert output.count('Use a configuration builder') == 4, output  # two source findings plus summary
        project.write_text(text.replace('</PropertyGroup>', '<CodingRulesBuilderSuffixes>Builder;Descriptor;Configurator;Setup</CodingRulesBuilderSuffixes></PropertyGroup>'))
        output = run(build, consumer, environment)
        assert 'warning CR1001:' not in output and 'warning CR0500:' not in output, output
        command = ['dotnet', 'format', 'analyzers', 'Consumer.csproj', '--no-restore', '--diagnostics', 'CR0803', 'CR0804']
        run(command, consumer, environment)
        fixed = (consumer / 'Consumer.cs').read_bytes()
        assert '(int) pid' in fixed.decode(), fixed
        assert 'warning CR0803:' not in run(build, consumer, environment)
        run(command, consumer, environment)
        assert (consumer / 'Consumer.cs').read_bytes() == fixed
        print('PASS: exact external assembly/MSBuild registrations; builder suffix property and marker; saved braces/cast fixes.')


if __name__ == '__main__':
    main()

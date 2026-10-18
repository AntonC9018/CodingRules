"""Shared isolated package setup for compiled consumer fixtures."""

from contextlib import contextmanager
import importlib.util
import os
from pathlib import Path
import shutil
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("formatting_consumer", ROOT / "build/verify-formatting-consumer.py")
FORMATTING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FORMATTING)


@contextmanager
def packaged_consumer(fixture_name, package_path, parser):
    with tempfile.TemporaryDirectory(prefix="codingrules-consumer-") as temporary:
        work = Path(temporary)
        feed = work / "feed"
        feed.mkdir()
        environment = dict(os.environ)
        run = FORMATTING.run
        if package_path is None:
            run(["dotnet", "pack", "Package/CodingRules.Package.csproj", "-c", "Release", "--output", str(feed)],
                ROOT, environment)
        try:
            package = FORMATTING.select_package(package_path or feed)
        except ValueError as error:
            parser.error(str(error))
        if package_path is not None:
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
        environment["NUGET_PACKAGES"] = str(work / "packages")
        yield consumer, environment, run

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "verify-formatting-consumer.py"
SPEC = importlib.util.spec_from_file_location("formatting_consumer", SCRIPT)
formatting_consumer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(formatting_consumer)


class FormattingConsumerPackageTests(unittest.TestCase):
    def test_directory_selects_main_package_and_ignores_symbols_and_unrelated_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            feed = Path(temporary)
            for name in ["Anton.CodingRules.0.1.0.symbols.nupkg", "Other.0.1.0.nupkg",
                         "Anton.CodingRules.0.1.0.snupkg", "Anton.CodingRules.0.1.0.nupkg"]:
                (feed / name).touch()
            package = formatting_consumer.select_package(feed)
            self.assertEqual(feed / "Anton.CodingRules.0.1.0.nupkg", package)

    def test_explicit_package_path_is_preserved(self):
        package = Path("artifacts/Anton.CodingRules.0.1.0.nupkg")
        self.assertEqual(package, formatting_consumer.select_package(package))

    def test_cli_rejects_empty_symbol_only_and_ambiguous_feeds_clearly(self):
        cases = [([], "found 0: none"),
                 (["Anton.CodingRules.0.1.0.symbols.nupkg"], "found 0: none"),
                 (["Anton.CodingRules.0.2.0.nupkg", "Anton.CodingRules.0.1.0.nupkg"],
                  "found 2: Anton.CodingRules.0.1.0.nupkg, Anton.CodingRules.0.2.0.nupkg")]
        for names, expected in cases:
            with self.subTest(names=names), tempfile.TemporaryDirectory() as temporary:
                feed = Path(temporary)
                for name in names:
                    (feed / name).touch()
                result = subprocess.run([sys.executable, str(SCRIPT), str(feed)],
                                        capture_output=True, text=True, check=False)
                self.assertEqual(2, result.returncode)
                self.assertIn("Expected exactly one non-symbol Anton.CodingRules nupkg", result.stderr)
                self.assertIn(expected, result.stderr)
                self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()

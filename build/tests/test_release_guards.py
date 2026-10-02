import copy
import hashlib
from io import BytesIO
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

TEST_PATH = Path(__file__)
RESOLVED_TEST_PATH = TEST_PATH.resolve()
BUILD_DIRECTORY = RESOLVED_TEST_PATH.parents[1]
BUILD_PATH = str(BUILD_DIRECTORY)
sys.path.insert(0, BUILD_PATH)
from prepare_release import select_artifact, valid_run, verify_archive
import prepare_release


class ReleaseGuardTests(unittest.TestCase):
    def setUp(self):
        self.commit = "a" * 40
        self.repository = {"id": 123, "full_name": "AntonC9018/CodingRules"}
        self.run = {
            "id": 789, "run_attempt": 2, "workflow_id": 456,
            "path": ".github/workflows/ci.yml", "event": "push", "head_branch": "main",
            "head_sha": self.commit, "status": "completed", "conclusion": "success",
            "repository": {"id": 123}, "head_repository": {"id": 123},
        }
        self.package = b"the exact tested nupkg bytes"
        package_hash = hashlib.sha256(self.package)
        package_digest = package_hash.hexdigest()
        self.manifest = {
            "schema": 1, "repository": self.repository["full_name"], "commit": self.commit,
            "run_id": 789, "run_attempt": 2, "package_id": "Anton.CodingRules",
            "version": "0.1.0", "file": "Anton.CodingRules.0.1.0.nupkg",
            "sha256": package_digest,
        }
        self.artifact = {
            "id": 101, "name": f"codingrules-package-{self.commit}-attempt-2", "expired": False,
            "digest": "sha256:" + "b" * 64,
            "workflow_run": {"id": 789, "repository_id": 123, "head_repository_id": 123,
                             "head_branch": "main", "head_sha": self.commit},
        }

    def archive(self, manifest=None, package=None, extra=None):
        output = BytesIO()
        with ZipFile(output, "w") as archive:
            selected_manifest = manifest or self.manifest
            manifest_json = json.dumps(selected_manifest)
            archive.writestr("manifest.json", manifest_json)
            selected_package = self.package if package is None else package
            archive.writestr(self.manifest["file"], selected_package)
            if extra:
                archive.writestr(extra, "unexpected")
        data = output.getvalue()
        artifact = copy.deepcopy(self.artifact)
        archive_hash = hashlib.sha256(data)
        archive_digest = archive_hash.hexdigest()
        artifact["digest"] = "sha256:" + archive_digest
        return data, artifact

    def verify(self, data, artifact):
        return verify_archive(data, artifact, self.repository, self.run, self.commit, "0.1.0")

    def test_only_exact_successful_main_push_workflow_is_accepted(self):
        run_is_valid = valid_run(self.run, self.repository, 456, self.commit)
        self.assertTrue(run_is_valid)
        mutations = {"workflow_id": 999, "path": ".github/workflows/other.yml",
                     "event": "pull_request", "head_branch": "feature", "head_sha": "c" * 40,
                     "status": "in_progress", "conclusion": "failure", "repository": {"id": 999},
                     "head_repository": {"id": 999}}
        for key, value in mutations.items():
            with self.subTest(key=key):
                run = dict(self.run, **{key: value})
                run_is_valid = valid_run(run, self.repository, 456, self.commit)
                self.assertFalse(run_is_valid)

    def test_artifact_must_match_successful_run_attempt_and_provenance(self):
        selected_artifact = select_artifact([self.artifact], self.run, self.repository, self.commit)
        self.assertEqual(self.artifact, selected_artifact)
        mutations = {"id": 999, "repository_id": 999, "head_repository_id": 999,
                     "head_branch": "feature", "head_sha": "c" * 40}
        for key, value in mutations.items():
            with self.subTest(key=key):
                artifact = copy.deepcopy(self.artifact)
                artifact["workflow_run"][key] = value
                with self.assertRaises(ValueError):
                    select_artifact([artifact], self.run, self.repository, self.commit)
        for changes in [{"expired": True}, {"digest": None}, {"name": "attempt-1"}]:
            with self.subTest(changes=changes), self.assertRaises((ValueError, TypeError)):
                artifact = dict(self.artifact, **changes)
                select_artifact([artifact], self.run, self.repository, self.commit)
        with self.assertRaises(ValueError):
            select_artifact([self.artifact, self.artifact], self.run, self.repository, self.commit)

    def test_verified_archive_returns_only_exact_package_bytes(self):
        data, artifact = self.archive()
        verified_package = self.verify(data, artifact)
        self.assertEqual((self.manifest["file"], self.package), verified_package)

    def test_archive_digest_mismatch_is_fatal(self):
        data, artifact = self.archive()
        artifact["digest"] = "sha256:" + "0" * 64
        with self.assertRaisesRegex(ValueError, "archive SHA-256"):
            self.verify(data, artifact)

    def test_manifest_provenance_and_version_mismatches_are_fatal(self):
        mutations = {"schema": 2, "repository": "attacker/fork", "commit": "c" * 40,
                     "run_id": 999, "run_attempt": 1, "package_id": "Other.Package",
                     "version": "0.2.0", "file": "../injected.nupkg"}
        for key, value in mutations.items():
            with self.subTest(key=key):
                manifest = dict(self.manifest, **{key: value})
                data, artifact = self.archive(manifest)
                with self.assertRaises(ValueError):
                    self.verify(data, artifact)

    def test_changed_package_bytes_are_rejected_even_with_valid_archive_digest(self):
        data, artifact = self.archive(package=b"different bytes")
        with self.assertRaisesRegex(ValueError, "nupkg SHA-256"):
            self.verify(data, artifact)

    def test_unexpected_entries_and_path_traversal_are_rejected(self):
        for name in ["extra.nupkg", "../script.py"]:
            data, artifact = self.archive(extra=name)
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.verify(data, artifact)

    def invoke_release(self, tag="v0.1.0", ancestor=True, runs=None, event_commit=None):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            event = root / "event.json"
            event_payload = {"action": "published", "release": {"draft": False, "tag_name": tag}}
            event_json = json.dumps(event_payload)
            event.write_text(event_json)
            self.repository["default_branch"] = "main"
            package_stream = BytesIO()
            with ZipFile(package_stream, "w") as package:
                spec_xml = f'''
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata><id>Anton.CodingRules</id><version>0.1.0</version>
  <repository commit="{self.commit}" /></metadata>
</package>'''
                package.writestr("Anton.CodingRules.nuspec", spec_xml)
                for name in ["CodingRules.Analyzers", "CodingRules.CodeFixes", "CodingRules.Shared"]:
                    assembly_path = f"analyzers/dotnet/cs/{name}.dll"
                    package.writestr(assembly_path, b"fixture")
            self.package = package_stream.getvalue()
            package_hash = hashlib.sha256(self.package)
            self.manifest["sha256"] = package_hash.hexdigest()
            data, artifact = self.archive()

            def fake_api(endpoint, paginated=False):
                if endpoint.endswith("CodingRules"):
                    return self.repository
                if endpoint.endswith("/actions/workflows/ci.yml"):
                    return {"id": 456, "path": ".github/workflows/ci.yml"}
                if "/runs?" in endpoint:
                    selected_runs = [self.run] if runs is None else runs
                    return [{"workflow_runs": selected_runs}]
                if endpoint.endswith("/actions/runs/789"):
                    return self.run
                if "/artifacts?" in endpoint:
                    return [{"artifacts": [artifact]}]
                message = f"Unexpected API endpoint: {endpoint}"
                self.fail(message)

            def fake_git(*arguments):
                if arguments[0] == "rev-parse":
                    return self.commit
                if arguments[0] == "show":
                    return "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"
                message = f"Unexpected git call: {arguments}"
                self.fail(message)

            error = None if ancestor else subprocess.CalledProcessError(1, "git merge-base")
            event_path = str(event)
            env = {"GITHUB_EVENT_PATH": event_path, "GITHUB_EVENT_NAME": "release",
                   "GITHUB_REPOSITORY": self.repository["full_name"],
                   "GITHUB_SHA": self.commit if event_commit is None else event_commit}
            output = root / "publish"
            output_path = str(output)
            with patch.dict(os.environ, env), patch.object(sys, "argv", ["prepare_release.py", output_path]), \
                    patch.object(prepare_release, "api", side_effect=fake_api), \
                    patch.object(prepare_release, "git", side_effect=fake_git), \
                    patch.object(prepare_release.subprocess, "check_output", return_value=data), \
                    patch.object(prepare_release.subprocess, "run", side_effect=error) as ancestry:
                prepare_release.main()
                ancestry.assert_called_once_with(
                    ["git", "merge-base", "--is-ancestor", self.commit, "origin/main"], check=True)
                package_path = output / self.manifest["file"]
                published_package = package_path.read_bytes()
                self.assertEqual(self.package, published_package)

    def test_release_can_use_an_older_reachable_main_commit(self):
        # Resolution/ancestry guards accept this SHA without requiring main-tip equality.
        self.invoke_release()

    def test_release_rejects_non_main_commit(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.invoke_release(ancestor=False)

    def test_release_rejects_tag_moved_after_publication(self):
        with self.assertRaisesRegex(ValueError, "tag moved"):
            self.invoke_release(event_commit="c" * 40)

    def test_release_rejects_source_tag_version_mismatch(self):
        with self.assertRaisesRegex(ValueError, "tag does not match"):
            self.invoke_release(tag="v0.2.0")

    def test_release_rejects_missing_successful_push_ci(self):
        with self.assertRaisesRegex(ValueError, "No successful main push"):
            self.invoke_release(runs=[])


if __name__ == "__main__":
    unittest.main()

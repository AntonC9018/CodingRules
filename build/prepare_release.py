"""Accept only a reachable main commit's successful push-CI immutable artifact."""

import hashlib
from io import BytesIO
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlencode
from zipfile import ZipFile

from package_artifact import PACKAGE_ID, package_metadata, require, source_version

CI_PATH = ".github/workflows/ci.yml"
VERSION_PATTERN = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"


def git(*arguments):
    output = subprocess.check_output(["git", *arguments], text=True)
    return output.strip()


def api(endpoint, paginated=False):
    command = ["gh", "api", endpoint]
    if paginated:
        command.extend(["--paginate", "--slurp"])
    output = subprocess.check_output(command)
    return json.loads(output)


def valid_run(run, repository, workflow_id, commit):
    run_workflow_id = run.get("workflow_id")
    if run_workflow_id != workflow_id:
        return False
    path = run.get("path")
    if path != CI_PATH:
        return False
    event = run.get("event")
    if event != "push":
        return False
    branch = run.get("head_branch")
    if branch != "main":
        return False
    head_commit = run.get("head_sha")
    if head_commit != commit:
        return False
    status = run.get("status")
    if status != "completed":
        return False
    conclusion = run.get("conclusion")
    if conclusion != "success":
        return False
    run_repository = run.get("repository", {})
    run_repository_id = run_repository.get("id")
    if run_repository_id != repository["id"]:
        return False
    head_repository = run.get("head_repository", {})
    head_repository_id = head_repository.get("id")
    if head_repository_id != repository["id"]:
        return False
    return True


def select_artifact(artifacts, run, repository, commit):
    expected_name = f"codingrules-package-{commit}-attempt-{run['run_attempt']}"
    matches = [artifact for artifact in artifacts if artifact.get("name") == expected_name]
    match_count = len(matches)
    require(match_count == 1, "Expected one artifact from the successful CI attempt")
    artifact = matches[0]
    expired = artifact.get("expired", True)
    require(not expired, "Tested artifact has expired; rerun main CI")
    provenance = artifact.get("workflow_run", {})
    expected_provenance = {"id": run["id"], "repository_id": repository["id"],
                           "head_repository_id": repository["id"], "head_branch": "main",
                           "head_sha": commit}
    for key, expected in expected_provenance.items():
        actual = provenance.get(key)
        message = f"Artifact {key} provenance mismatch"
        require(actual == expected, message)
    digest = artifact.get("digest")
    digest_is_text = isinstance(digest, str)
    require(digest_is_text, "Artifact has no immutable archive digest")
    digest_match = re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
    require(digest_match is not None, "Artifact has no valid immutable archive digest")
    return artifact


def verify_archive(data, artifact, repository, run, commit, version):
    archive_hash = hashlib.sha256(data)
    archive_digest = archive_hash.hexdigest()
    digest = "sha256:" + archive_digest
    require(digest == artifact["digest"], "Downloaded archive SHA-256 mismatch")
    archive_stream = BytesIO(data)
    with ZipFile(archive_stream) as archive:
        entries = archive.namelist()
        entry_count = len(entries)
        require(entry_count == 2, "Unexpected artifact entries")
        manifest_json = archive.read("manifest.json")
        manifest = json.loads(manifest_json)
        expected = {"schema": 1, "repository": repository["full_name"], "commit": commit,
                    "run_id": run["id"], "run_attempt": run["run_attempt"],
                    "package_id": PACKAGE_ID, "version": version,
                    "file": f"{PACKAGE_ID}.{version}.nupkg"}
        for key, value in expected.items():
            actual = manifest.get(key)
            message = f"Manifest {key} mismatch"
            require(actual == value, message)
        actual_paths = set(entries)
        expected_paths = {"manifest.json", expected["file"]}
        require(actual_paths == expected_paths, "Artifact contains unexpected paths")
        package = archive.read(expected["file"])
        package_hash = hashlib.sha256(package)
        package_digest = package_hash.hexdigest()
        expected_package_digest = manifest.get("sha256")
        require(package_digest == expected_package_digest, "Tested nupkg SHA-256 mismatch")
    return expected["file"], package


def main():
    event_path = Path(os.environ["GITHUB_EVENT_PATH"])
    event_json = event_path.read_text()
    event = json.loads(event_json)
    release = event["release"]
    require(os.environ["GITHUB_EVENT_NAME"] == "release", "Publishing requires a published GitHub release")
    require(event["action"] == "published", "Publishing requires a published GitHub release")
    require(not release["draft"], "Cannot publish a draft")
    tag = release["tag_name"]
    tag_pattern = "v" + VERSION_PATTERN
    tag_match = re.fullmatch(tag_pattern, tag)
    require(tag_match is not None, "Expected a v<package-version> tag")
    repository_name = os.environ["GITHUB_REPOSITORY"]
    require(repository_name == "AntonC9018/CodingRules", "Unexpected release repository")
    endpoint = f"repos/{repository_name}"
    repository = api(endpoint)
    require(repository["default_branch"] == "main", "Expected default branch main")
    # Full checkout includes main history and tags. Resolve annotated tags to commits too.
    tag_ref = f"refs/tags/{tag}^{{commit}}"
    commit = git("rev-parse", "--verify", tag_ref)
    require(commit == os.environ["GITHUB_SHA"], "Release tag moved from its published event commit")
    subprocess.run(["git", "merge-base", "--is-ancestor", commit, "origin/main"], check=True)
    project_ref = f"{commit}:Package/CodingRules.Package.csproj"
    project_xml = git("show", project_ref)
    version = source_version(project_xml)
    require(tag == "v" + version, "Release tag does not match the source package version")
    workflow = api(endpoint + "/actions/workflows/ci.yml")
    require(workflow["path"] == CI_PATH, "Unexpected CI workflow")
    query = urlencode({"head_sha": commit, "branch": "main", "event": "push",
                       "status": "success", "per_page": 100})
    pages = api(endpoint + "/actions/workflows/ci.yml/runs?" + query, paginated=True)
    runs = [run for page in pages for run in page["workflow_runs"]
            if valid_run(run, repository, workflow["id"], commit)]
    require(runs, "No successful main push CI run for this exact release commit")
    run = max(runs, key=lambda item: item["id"])
    # Re-read to reject an in-progress or failed rerun that started during selection.
    run_endpoint = f"{endpoint}/actions/runs/{run['id']}"
    run = api(run_endpoint)
    run_is_valid = valid_run(run, repository, workflow["id"], commit)
    require(run_is_valid, "Selected CI run is no longer successful")
    artifacts_endpoint = f"{endpoint}/actions/runs/{run['id']}/artifacts?per_page=100"
    pages = api(artifacts_endpoint, paginated=True)
    artifacts = [artifact for page in pages for artifact in page["artifacts"]]
    artifact = select_artifact(artifacts, run, repository, commit)
    archive_endpoint = f"{endpoint}/actions/artifacts/{artifact['id']}/zip"
    data = subprocess.check_output(["gh", "api", archive_endpoint])
    name, package = verify_archive(data, artifact, repository, run, commit, version)
    directory = Path(sys.argv[1])
    directory_exists = directory.exists()
    require(not directory_exists, "Publish directory must be fresh")
    directory.mkdir(parents=True)
    path = directory / name
    path.write_bytes(package)
    package_version, package_commit = package_metadata(path)
    require(package_version == version, "Nuspec source/version provenance mismatch")
    require(package_commit == commit, "Nuspec source/version provenance mismatch")
    message = f"Verified {tag} at {commit}; CI run {run['id']} attempt {run['run_attempt']}; artifact {artifact['id']}"
    print(message)


if __name__ == "__main__":
    main()

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
    return subprocess.check_output(["git", *arguments], text=True).strip()


def api(endpoint, paginated=False):
    command = ["gh", "api", endpoint]
    if paginated:
        command.extend(["--paginate", "--slurp"])
    return json.loads(subprocess.check_output(command))


def valid_run(run, repository, workflow_id, commit):
    return (
        run.get("workflow_id") == workflow_id
        and run.get("path") == CI_PATH
        and run.get("event") == "push"
        and run.get("head_branch") == "main"
        and run.get("head_sha") == commit
        and run.get("status") == "completed"
        and run.get("conclusion") == "success"
        and run.get("repository", {}).get("id") == repository["id"]
        and run.get("head_repository", {}).get("id") == repository["id"]
    )


def select_artifact(artifacts, run, repository, commit):
    expected_name = f"codingrules-package-{commit}-attempt-{run['run_attempt']}"
    matches = [artifact for artifact in artifacts if artifact.get("name") == expected_name]
    require(len(matches) == 1, "Expected one artifact from the successful CI attempt")
    artifact = matches[0]
    require(not artifact.get("expired", True), "Tested artifact has expired; rerun main CI")
    provenance = artifact.get("workflow_run", {})
    for key, expected in {"id": run["id"], "repository_id": repository["id"],
                          "head_repository_id": repository["id"], "head_branch": "main",
                          "head_sha": commit}.items():
        require(provenance.get(key) == expected, f"Artifact {key} provenance mismatch")
    digest = artifact.get("digest")
    require(isinstance(digest, str), "Artifact has no immutable archive digest")
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", digest) is not None,
            "Artifact has no valid immutable archive digest")
    return artifact


def verify_archive(data, artifact, repository, run, commit, version):
    digest = "sha256:" + hashlib.sha256(data).hexdigest()
    require(digest == artifact["digest"], "Downloaded archive SHA-256 mismatch")
    with ZipFile(BytesIO(data)) as archive:
        require(len(archive.namelist()) == 2, "Unexpected artifact entries")
        manifest = json.loads(archive.read("manifest.json"))
        expected = {"schema": 1, "repository": repository["full_name"], "commit": commit,
                    "run_id": run["id"], "run_attempt": run["run_attempt"],
                    "package_id": PACKAGE_ID, "version": version,
                    "file": f"{PACKAGE_ID}.{version}.nupkg"}
        for key, value in expected.items():
            require(manifest.get(key) == value, f"Manifest {key} mismatch")
        require(set(archive.namelist()) == {"manifest.json", expected["file"]},
                "Artifact contains unexpected paths")
        package = archive.read(expected["file"])
        require(hashlib.sha256(package).hexdigest() == manifest.get("sha256"),
                "Tested nupkg SHA-256 mismatch")
    return expected["file"], package


def main():
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    release = event["release"]
    require(os.environ["GITHUB_EVENT_NAME"] == "release" and event["action"] == "published",
            "Publishing requires a published GitHub release")
    require(not release["draft"], "Cannot publish a draft")
    tag = release["tag_name"]
    require(re.fullmatch("v" + VERSION_PATTERN, tag) is not None, "Expected a v<package-version> tag")
    repository_name = os.environ["GITHUB_REPOSITORY"]
    require(repository_name == "AntonC9018/CodingRules", "Unexpected release repository")
    endpoint = f"repos/{repository_name}"
    repository = api(endpoint)
    require(repository["default_branch"] == "main", "Expected default branch main")
    # Full checkout includes main history and tags. Resolve annotated tags to commits too.
    commit = git("rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}")
    require(commit == os.environ["GITHUB_SHA"], "Release tag moved from its published event commit")
    subprocess.run(["git", "merge-base", "--is-ancestor", commit, "origin/main"], check=True)
    version = source_version(git("show", f"{commit}:Package/CodingRules.Package.csproj"))
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
    run = api(endpoint + f"/actions/runs/{run['id']}")
    require(valid_run(run, repository, workflow["id"], commit), "Selected CI run is no longer successful")
    pages = api(endpoint + f"/actions/runs/{run['id']}/artifacts?per_page=100", paginated=True)
    artifacts = [artifact for page in pages for artifact in page["artifacts"]]
    artifact = select_artifact(artifacts, run, repository, commit)
    data = subprocess.check_output(["gh", "api", endpoint + f"/actions/artifacts/{artifact['id']}/zip"])
    name, package = verify_archive(data, artifact, repository, run, commit, version)
    directory = Path(sys.argv[1])
    require(not directory.exists(), "Publish directory must be fresh")
    directory.mkdir(parents=True)
    path = directory / name
    path.write_bytes(package)
    package_version, package_commit = package_metadata(path)
    require(package_version == version and package_commit == commit,
            "Nuspec source/version provenance mismatch")
    print(f"Verified {tag} at {commit}; CI run {run['id']} attempt {run['run_attempt']}; artifact {artifact['id']}")


if __name__ == "__main__":
    main()

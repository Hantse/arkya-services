"""Promote a complete, single-commit GHCR release without altering Unity image pins."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

import yaml

BASE_SERVICES = ("identity", "gateway", "characters", "content", "admin", "realms", "backend", "frontend")
SERVICES = (*BASE_SERVICES, "inference", "reports")
IMAGE_PREFIX = "ghcr.io/hantse/arkya-services-"


def read_git_source(commit: str, path: Path):
    root = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], check=True,
                              capture_output=True, text=True, encoding="utf-8").stdout.strip()).resolve()
    relative = path.resolve().relative_to(root).as_posix()
    return subprocess.run(["git", "show", f"{commit}:{relative}"], check=True,
                          capture_output=True, text=True, encoding="utf-8").stdout


def source_without_release_pins(text: str, expected_services: tuple):
    try:
        document = yaml.safe_load(text)
    except yaml.YAMLError as error:
        raise ValueError("Invalid GitOps source YAML.") from error
    images = document.get("images") if isinstance(document, dict) else None
    if not isinstance(images, list) or not all(isinstance(image, dict) and isinstance(image.get("name"), str) for image in images):
        raise ValueError("The GitOps source must contain named Kustomize images.")
    names = [image["name"] for image in images]
    if len(names) != len(set(names)):
        raise ValueError("Duplicate image names in the GitOps source.")
    expected = {IMAGE_PREFIX + service for service in expected_services}
    if not expected.issubset(names):
        raise ValueError("The GitOps source is missing an assigned release image.")
    for image in images:
        if image["name"] in expected:
            for field in ("newName", "newTag", "digest"):
                image.pop(field, None)
    return document


def assert_source_current(source_commit: str, kustomization: Path, inference_kustomization: Path, reports_kustomization: Path):
    if not re.fullmatch(r"[a-f0-9]{40}", source_commit):
        raise ValueError("A full source commit SHA is required.")
    for path, services in ((kustomization, BASE_SERVICES), (inference_kustomization, ("inference",)),
                           (reports_kustomization, ("reports",))):
        tested = source_without_release_pins(read_git_source(source_commit, path), services)
        latest = source_without_release_pins(read_git_source("HEAD", path), services)
        if tested != latest:
            raise ValueError(f"Newer deployment configuration exists in {path}; only release image pins may differ.")


def promote(artifact_directory: Path, source_commit: str, kustomization: Path, release_file: Path,
            inference_kustomization: Path, reports_kustomization: Path):
    if len({path.resolve() for path in (kustomization, inference_kustomization, reports_kustomization, release_file)}) != 4:
        raise ValueError("The three GitOps sources and the release evidence must be distinct files.")
    if not re.fullmatch(r"[a-f0-9]{40}", source_commit):
        raise ValueError("A full source commit SHA is required.")
    paths = sorted(artifact_directory.glob("*.json"))
    if {path.name for path in paths} != {f"{service}.json" for service in SERVICES}:
        raise ValueError(f"Promotion requires exactly one digest artifact for each of the {len(SERVICES)} images.")
    release = []
    for path in paths:
        image = json.loads(path.read_text(encoding="utf-8"))
        service = path.stem
        if not isinstance(image, dict) or image.get("schemaVersion") != 1 or image.get("service") != service:
            raise ValueError(f"Unexpected image artifact: {path.name}")
        if image.get("image") != IMAGE_PREFIX + service or image.get("sourceCommit") != source_commit:
            raise ValueError(f"Image identity or source commit mismatch: {service}")
        if not isinstance(image.get("digest"), str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", image["digest"]):
            raise ValueError(f"Invalid published image digest: {service}")
        if image.get("platforms") != ["linux/amd64", "linux/arm64"]:
            raise ValueError(f"Both approved platforms are required: {service}")
        release.append(image)

    by_name = {image["image"]: image for image in release}
    # Validate every source before staging any writes. Git publishes all pins in one commit.
    beta_text = prepare_source(kustomization, BASE_SERVICES, by_name)
    inference_text = prepare_source(inference_kustomization, ("inference",), by_name)
    reports_text = prepare_source(reports_kustomization, ("reports",), by_name)
    evidence = json.dumps({"schemaVersion": 1, "sourceCommit": source_commit, "images": release}, indent=2) + "\n"
    replace_files({kustomization: beta_text, inference_kustomization: inference_text,
                   reports_kustomization: reports_text, release_file: evidence})
    return release


def prepare_source(kustomization: Path, expected_services: tuple, by_name: dict):
    try:
        document = yaml.safe_load(kustomization.read_text(encoding="utf-8"))
    except yaml.YAMLError as error:
        raise ValueError(f"Invalid GitOps YAML: {kustomization}") from error
    images = document.get("images") if isinstance(document, dict) else None
    if not isinstance(images, list) or not all(isinstance(image, dict) and isinstance(image.get("name"), str) for image in images):
        raise ValueError("The GitOps source must contain named Kustomize images.")
    names = [image["name"] for image in images]
    if len(names) != len(set(names)):
        raise ValueError("Duplicate image names in the GitOps source.")
    expected = {IMAGE_PREFIX + service for service in expected_services}
    if set(names) & set(by_name) != expected:
        raise ValueError(f"The GitOps source must declare exactly its assigned service images: {kustomization}")
    for image in images:
        if image["name"] not in by_name:
            continue
        image["newName"] = image["name"]
        image["digest"] = by_name[image["name"]]["digest"]
        image.pop("newTag", None)

    return yaml.safe_dump(document, sort_keys=False)


def stage_file(target: Path, content: bytes):
    descriptor, name = tempfile.mkstemp(prefix=target.name + ".", suffix=".tmp", dir=target.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        return temporary
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def replace_files(contents: dict):
    originals = {path: path.read_bytes() if path.exists() else None for path in contents}
    staged, replaced = {}, []
    try:
        for path, content in contents.items():
            staged[path] = stage_file(path, content.encode("utf-8"))
        for path, temporary in staged.items():
            temporary.replace(path)
            replaced.append(path)
    except BaseException:
        for path in reversed(replaced):
            if originals[path] is None:
                path.unlink(missing_ok=True)
            else:
                rollback = stage_file(path, originals[path])
                try:
                    rollback.replace(path)
                finally:
                    rollback.unlink(missing_ok=True)
        raise
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--source-commit")
    mode.add_argument("--assert-source-commit")
    parser.add_argument("--artifact-directory", type=Path)
    parser.add_argument("--kustomization", type=Path, required=True)
    parser.add_argument("--inference-kustomization", type=Path, required=True)
    parser.add_argument("--reports-kustomization", type=Path, required=True)
    parser.add_argument("--release-file", type=Path)
    arguments = parser.parse_args()
    if arguments.assert_source_commit:
        if arguments.artifact_directory or arguments.release_file:
            parser.error("Source verification does not accept output or artifact paths.")
        assert_source_current(arguments.assert_source_commit, arguments.kustomization, arguments.inference_kustomization,
                              arguments.reports_kustomization)
        print("The tested deployment configuration is unchanged; only release pins may differ.")
    else:
        if not arguments.artifact_directory or not arguments.release_file:
            parser.error("Promotion requires --artifact-directory and --release-file.")
        promoted = promote(arguments.artifact_directory, arguments.source_commit, arguments.kustomization,
                           arguments.release_file, arguments.inference_kustomization, arguments.reports_kustomization)
        print(f"Pinned all {len(promoted)} service and site images from {arguments.source_commit}.")

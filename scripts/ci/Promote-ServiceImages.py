"""Promote a complete, single-commit GHCR release without altering Unity image pins."""

import argparse
import json
from pathlib import Path
import re

import yaml

SERVICES = ("identity", "gateway", "characters", "content", "admin", "realms", "backend", "frontend")
IMAGE_PREFIX = "ghcr.io/hantse/arkya-services-"


def promote(artifact_directory: Path, source_commit: str, kustomization: Path, release_file: Path):
    if not re.fullmatch(r"[a-f0-9]{40}", source_commit):
        raise ValueError("A full source commit SHA is required.")
    paths = sorted(artifact_directory.glob("*.json"))
    if {path.name for path in paths} != {f"{service}.json" for service in SERVICES}:
        raise ValueError("Promotion requires exactly one digest artifact for each of the eight images.")
    release = []
    for path in paths:
        image = json.loads(path.read_text(encoding="utf-8"))
        service = path.stem
        if not isinstance(image, dict) or image.get("schemaVersion") != 1 or image.get("service") != service:
            raise ValueError(f"Unexpected image artifact: {path.name}")
        if image.get("image") != IMAGE_PREFIX + service or image.get("sourceCommit") != source_commit:
            raise ValueError(f"Image identity or source commit mismatch: {service}")
        if not re.fullmatch(r"sha256:[a-f0-9]{64}", image.get("digest", "")):
            raise ValueError(f"Invalid published image digest: {service}")
        if image.get("platforms") != ["linux/amd64", "linux/arm64"]:
            raise ValueError(f"Both approved platforms are required: {service}")
        release.append(image)

    document = yaml.safe_load(kustomization.read_text(encoding="utf-8"))
    images = document.get("images") if isinstance(document, dict) else None
    if not isinstance(images, list) or not all(isinstance(image, dict) and isinstance(image.get("name"), str) for image in images):
        raise ValueError("The GitOps source must contain named Kustomize images.")
    names = [image["name"] for image in images]
    if len(names) != len(set(names)):
        raise ValueError("Duplicate image names in the GitOps source.")
    if not all(IMAGE_PREFIX + service in names for service in SERVICES):
        raise ValueError("The GitOps source must declare each of the eight service and site images.")
    by_name = {image["image"]: image for image in release}
    for image in images:
        if image["name"] not in by_name:
            continue
        image["newName"] = image["name"]
        image["digest"] = by_name[image["name"]]["digest"]
        image.pop("newTag", None)

    temporary = kustomization.with_suffix(kustomization.suffix + ".tmp")
    temporary.write_text(yaml.safe_dump(document, sort_keys=False), encoding="utf-8", newline="\n")
    temporary.replace(kustomization)
    release_file.write_text(json.dumps({"schemaVersion": 1, "sourceCommit": source_commit, "images": release}, indent=2) + "\n", encoding="utf-8")
    return release


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-directory", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--kustomization", type=Path, required=True)
    parser.add_argument("--release-file", type=Path, required=True)
    arguments = parser.parse_args()
    promoted = promote(arguments.artifact_directory, arguments.source_commit, arguments.kustomization, arguments.release_file)
    print(f"Pinned all {len(promoted)} service and site images from {arguments.source_commit}.")

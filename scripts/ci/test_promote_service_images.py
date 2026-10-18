import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

import yaml

spec = importlib.util.spec_from_file_location("promotion", Path(__file__).with_name("Promote-ServiceImages.py"))
promotion = importlib.util.module_from_spec(spec)
spec.loader.exec_module(promotion)


class PromotionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.artifacts = self.root / "images"
        self.artifacts.mkdir()
        self.commit = "a" * 40
        self.kustomization = self.root / "kustomization.yaml"
        self.release = self.root / "release.json"
        for service in promotion.SERVICES:
            artifact = dict(schemaVersion=1, service=service, image=promotion.IMAGE_PREFIX + service,
                            digest="sha256:" + hashlib.sha256(service.encode()).hexdigest(), sourceCommit=self.commit,
                            platforms=["linux/amd64", "linux/arm64"])
            (self.artifacts / f"{service}.json").write_text(json.dumps(artifact))
        self.unity_pin = {"name": "ghcr.io/hantse/arkya-mmorpg-dedicated", "digest": "sha256:" + "b" * 64}
        self.document = {"apiVersion": "kustomize.config.k8s.io/v1beta1", "kind": "Kustomization", "resources": ["services.yaml"],
                         "images": [dict(name=promotion.IMAGE_PREFIX + service, newTag="sha-old") for service in promotion.SERVICES] + [self.unity_pin]}
        self.save_document()

    def save_document(self):
        self.kustomization.write_text(yaml.safe_dump(self.document, sort_keys=False))

    def run_promotion(self):
        return promotion.promote(self.artifacts, self.commit, self.kustomization, self.release)

    def assert_refused_without_change(self):
        original = self.kustomization.read_bytes()
        with self.assertRaises(ValueError):
            self.run_promotion()
        self.assertEqual(original, self.kustomization.read_bytes())
        self.assertFalse(self.release.exists())

    def change_artifact(self, **values):
        path = self.artifacts / "characters.json"
        artifact = json.loads(path.read_text())
        artifact.update(values)
        path.write_text(json.dumps(artifact))

    def test_complete_release_pins_digests_and_preserves_unity_and_resources(self):
        self.run_promotion()
        result = yaml.safe_load(self.kustomization.read_text())
        self.assertEqual(["services.yaml"], result["resources"])
        self.assertEqual(self.unity_pin, result["images"][-1])
        for image in result["images"][:-1]:
            self.assertNotIn("newTag", image)
            self.assertEqual(image["name"], image["newName"])
            artifact = json.loads((self.artifacts / (image["name"].removeprefix(promotion.IMAGE_PREFIX) + ".json")).read_text())
            self.assertEqual(artifact["digest"], image["digest"])
        evidence = json.loads(self.release.read_text())
        self.assertEqual(self.commit, evidence["sourceCommit"])
        self.assertEqual(8, len(evidence["images"]))

    def test_partial_publication_is_refused(self):
        (self.artifacts / "frontend.json").unlink()
        self.assert_refused_without_change()

    def test_mixed_source_commits_are_refused(self):
        self.change_artifact(sourceCommit="c" * 40)
        self.assert_refused_without_change()

    def test_mutable_tags_or_foreign_images_are_refused(self):
        for change in [{"digest": "latest"}, {"image": "ghcr.io/other/characters"}, {"service": "identity"}]:
            with self.subTest(change=change):
                path = self.artifacts / "characters.json"
                original = path.read_bytes()
                self.change_artifact(**change)
                self.assert_refused_without_change()
                path.write_bytes(original)

    def test_single_platform_image_is_refused(self):
        self.change_artifact(platforms=["linux/amd64"])
        self.assert_refused_without_change()

    def test_missing_or_duplicate_gitops_entries_are_refused(self):
        self.document["images"].pop(0)
        self.save_document()
        self.assert_refused_without_change()
        self.document["images"].append(self.document["images"][0].copy())
        self.save_document()
        self.assert_refused_without_change()


if __name__ == "__main__":
    unittest.main()

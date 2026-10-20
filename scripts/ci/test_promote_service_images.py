import hashlib
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

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
        self.inference = self.root / "inference-kustomization.yaml"
        self.reports = self.root / "reports-kustomization.yaml"
        self.release = self.root / "release.json"
        for service in promotion.SERVICES:
            artifact = dict(schemaVersion=1, service=service, image=promotion.IMAGE_PREFIX + service,
                            digest="sha256:" + hashlib.sha256(service.encode()).hexdigest(), sourceCommit=self.commit,
                            platforms=["linux/amd64", "linux/arm64"])
            (self.artifacts / f"{service}.json").write_text(json.dumps(artifact))
        self.unity_pin = {"name": "ghcr.io/hantse/arkya-mmorpg-dedicated", "digest": "sha256:" + "b" * 64}
        self.document = {"apiVersion": "kustomize.config.k8s.io/v1beta1", "kind": "Kustomization", "resources": ["services.yaml"],
                         "images": [dict(name=promotion.IMAGE_PREFIX + service, newTag="sha-old") for service in promotion.BASE_SERVICES] + [self.unity_pin]}
        self.inference_document = {"apiVersion": "kustomize.config.k8s.io/v1alpha1", "kind": "Component",
                                   "resources": ["inference.yaml"], "images": [dict(name=promotion.IMAGE_PREFIX + "inference", newTag="pending-publication")]}
        self.reports_document = {"apiVersion": "kustomize.config.k8s.io/v1alpha1", "kind": "Component",
                                 "resources": ["reports.yaml"], "images": [dict(name=promotion.IMAGE_PREFIX + "reports", newTag="pending-publication")]}
        self.save_document()
        self.save_inference()
        self.reports.write_text(yaml.safe_dump(self.reports_document, sort_keys=False))

    def save_document(self):
        self.kustomization.write_text(yaml.safe_dump(self.document, sort_keys=False))

    def save_inference(self):
        self.inference.write_text(yaml.safe_dump(self.inference_document, sort_keys=False))

    def run_promotion(self):
        return promotion.promote(self.artifacts, self.commit, self.kustomization, self.release, self.inference, self.reports)

    def assert_refused_without_change(self):
        original = self.kustomization.read_bytes()
        original_inference = self.inference.read_bytes()
        original_reports = self.reports.read_bytes()
        with self.assertRaises(ValueError):
            self.run_promotion()
        self.assertEqual(original, self.kustomization.read_bytes())
        self.assertEqual(original_inference, self.inference.read_bytes())
        self.assertEqual(original_reports, self.reports.read_bytes())
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
        inference = yaml.safe_load(self.inference.read_text())
        self.assertEqual("Component", inference["kind"])
        self.assertEqual(["inference.yaml"], inference["resources"])
        reports = yaml.safe_load(self.reports.read_text())
        self.assertEqual("Component", reports["kind"])
        self.assertEqual(["reports.yaml"], reports["resources"])
        for image in result["images"][:-1] + inference["images"] + reports["images"]:
            self.assertNotIn("newTag", image)
            self.assertEqual(image["name"], image["newName"])
            artifact = json.loads((self.artifacts / (image["name"].removeprefix(promotion.IMAGE_PREFIX) + ".json")).read_text())
            self.assertEqual(artifact["digest"], image["digest"])
        evidence = json.loads(self.release.read_text())
        self.assertEqual(self.commit, evidence["sourceCommit"])
        self.assertEqual(10, len(evidence["images"]))
        self.assertEqual(set(promotion.SERVICES), {image["service"] for image in evidence["images"]})

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

    def test_missing_inference_digest_prevents_all_promotions(self):
        (self.artifacts / "inference.json").unlink()
        self.assert_refused_without_change()

    def test_missing_reports_digest_prevents_all_promotions(self):
        (self.artifacts / "reports.json").unlink()
        self.assert_refused_without_change()

    def test_reports_digest_from_another_commit_prevents_all_promotions(self):
        path = self.artifacts / "reports.json"
        artifact = json.loads(path.read_text())
        artifact["sourceCommit"] = "f" * 40
        path.write_text(json.dumps(artifact))
        self.assert_refused_without_change()

    def test_invalid_reports_source_prevents_all_promotions(self):
        for content in ("images: [broken", "images: []", yaml.safe_dump(self.inference_document)):
            with self.subTest(content=content):
                self.reports.write_text(content)
                self.assert_refused_without_change()

    def test_reports_pin_cannot_be_promoted_in_another_source(self):
        self.inference_document["images"].append({"name": promotion.IMAGE_PREFIX + "reports"})
        self.save_inference()
        self.assert_refused_without_change()

    def test_unrequested_image_cannot_join_the_release(self):
        (self.artifacts / "telemetry.json").write_text("{}")
        self.assert_refused_without_change()

    def test_invalid_component_leaves_the_valid_beta_source_untouched(self):
        for images in ([], [{"name": promotion.IMAGE_PREFIX + "inference"}] * 2,
                       [{"name": promotion.IMAGE_PREFIX + "admin"}]):
            with self.subTest(images=images):
                self.inference_document["images"] = images
                self.save_inference()
                self.assert_refused_without_change()

    def test_component_yaml_parse_failure_does_not_change_beta(self):
        self.inference.write_text("images: [broken")
        self.assert_refused_without_change()

    def test_inference_pin_cannot_be_silently_promoted_in_the_wrong_source(self):
        self.document["images"].append({"name": promotion.IMAGE_PREFIX + "inference"})
        self.save_document()
        self.assert_refused_without_change()

    def test_aliasing_a_target_or_evidence_file_is_refused(self):
        original = self.kustomization.read_bytes()
        for inference, evidence in ((self.kustomization, self.release), (self.inference, self.kustomization)):
            with self.subTest(inference=inference, evidence=evidence), self.assertRaises(ValueError):
                promotion.promote(self.artifacts, self.commit, self.kustomization, evidence, inference, self.reports)
        self.assertEqual(original, self.kustomization.read_bytes())

    def test_aliasing_reports_source_with_any_other_output_is_refused(self):
        original = self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()
        for reports in (self.kustomization, self.inference, self.release):
            with self.subTest(reports=reports), self.assertRaises(ValueError):
                promotion.promote(self.artifacts, self.commit, self.kustomization, self.release, self.inference, reports)
        self.assertEqual(original, (self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()))

    def test_missing_component_file_does_not_change_beta(self):
        original = self.kustomization.read_bytes()
        self.inference.unlink()
        with self.assertRaises(FileNotFoundError):
            self.run_promotion()
        self.assertEqual(original, self.kustomization.read_bytes())
        self.assertFalse(self.release.exists())

    def test_all_files_are_staged_before_any_target_is_replaced(self):
        original = self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()
        stage = promotion.stage_file

        def fail_on_component(path, content):
            if path == self.inference:
                raise OSError("simulated disk write failure")
            return stage(path, content)

        with mock.patch.object(promotion, "stage_file", side_effect=fail_on_component), self.assertRaises(OSError):
            self.run_promotion()
        self.assertEqual(original, (self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()))
        self.assertFalse(self.release.exists())
        self.assertEqual([], list(self.root.glob("*.tmp")))

    def test_failed_second_replace_restores_the_first_file_and_evidence(self):
        original = self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()
        self.release.write_text("previous release evidence")
        replace = Path.replace

        def fail_on_component(temporary, target):
            if target == self.inference:
                raise OSError("simulated replacement failure")
            return replace(temporary, target)

        with mock.patch.object(Path, "replace", fail_on_component), self.assertRaises(OSError):
            self.run_promotion()
        self.assertEqual(original, (self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()))
        self.assertEqual("previous release evidence", self.release.read_text())
        self.assertEqual([], list(self.root.glob("*.tmp")))

    def test_failed_third_replace_restores_both_prior_pins(self):
        original = self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()
        self.release.write_text("previous release evidence")
        replace = Path.replace

        def fail_on_reports(temporary, target):
            if target == self.reports:
                raise OSError("simulated Reports replacement failure")
            return replace(temporary, target)

        with mock.patch.object(Path, "replace", fail_on_reports), self.assertRaises(OSError):
            self.run_promotion()
        self.assertEqual(original, (self.kustomization.read_bytes(), self.inference.read_bytes(), self.reports.read_bytes()))
        self.assertEqual("previous release evidence", self.release.read_text())
        self.assertEqual([], list(self.root.glob("*.tmp")))


class SourceGuardTests(unittest.TestCase):
    def setUp(self):
        self.commit = "a" * 40
        self.beta = Path("backend/deploy/gitops/beta/kustomization.yaml")
        self.inference = Path("backend/deploy/gitops/inference-control/kustomization.yaml")
        self.reports = Path("backend/deploy/gitops/reports-control/kustomization.yaml")
        self.documents = {
            self.beta: {"kind": "Kustomization", "resources": ["services.yaml"], "configMapGenerator": [{"name": "config", "envs": ["identity.env"]}],
                        "images": [{"name": promotion.IMAGE_PREFIX + service, "digest": "sha256:" + "b" * 64} for service in promotion.BASE_SERVICES] +
                                  [{"name": "ghcr.io/hantse/arkya-mmorpg-dedicated", "digest": "sha256:" + "d" * 64}]},
            self.inference: {"kind": "Component", "resources": ["inference.yaml"], "patches": [{"path": "admin.patch.yaml"}],
                             "images": [{"name": promotion.IMAGE_PREFIX + "inference", "newTag": "pending-publication"}]}}
        self.documents[self.reports] = {"kind": "Component", "resources": ["reports.yaml"], "patches": [{"path": "admin.patch.yaml"}],
                                        "images": [{"name": promotion.IMAGE_PREFIX + "reports", "newTag": "pending-publication"}]}
        self.latest = copy.deepcopy(self.documents)

    def check(self):
        def read(commit, path):
            return yaml.safe_dump((self.documents if commit == self.commit else self.latest)[path], sort_keys=False)
        with mock.patch.object(promotion, "read_git_source", side_effect=read):
            promotion.assert_source_current(self.commit, self.beta, self.inference, self.reports)

    def test_concurrent_release_promotion_is_allowed_for_all_pin_files(self):
        for path in (self.beta, self.inference, self.reports):
            for image in self.latest[path]["images"]:
                if image["name"].startswith(promotion.IMAGE_PREFIX):
                    image.pop("newTag", None)
                    image.update(newName=image["name"], digest="sha256:" + "c" * 64)
        self.check()

    def test_concurrent_component_patch_or_resources_change_is_rejected(self):
        for component in (self.inference, self.reports):
            for field in ("resources", "patches"):
                with self.subTest(component=component, field=field):
                    self.latest = copy.deepcopy(self.documents)
                    self.latest[component][field].append("unvalidated-change.yaml")
                    with self.assertRaises(ValueError):
                        self.check()

    def test_concurrent_beta_configuration_change_is_rejected(self):
        self.latest[self.beta]["configMapGenerator"][0]["envs"] = ["different.env"]
        with self.assertRaises(ValueError):
            self.check()

    def test_unity_pin_change_is_not_treated_as_a_service_promotion(self):
        self.latest[self.beta]["images"][-1]["digest"] = "sha256:" + "e" * 64
        with self.assertRaises(ValueError):
            self.check()

    def test_image_identity_or_other_transform_field_change_is_rejected(self):
        for field, value in (("name", "ghcr.io/foreign/image"), ("extraConfiguration", "different")):
            with self.subTest(field=field):
                self.latest = copy.deepcopy(self.documents)
                self.latest[self.inference]["images"][0][field] = value
                with self.assertRaises(ValueError):
                    self.check()


class WorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(__file__).resolve().parents[2]
        cls.workflow = yaml.load((cls.root / ".github/workflows/services-images.yml").read_text(), Loader=yaml.BaseLoader)
        cls.pins = {"backend/deploy/gitops/beta/kustomization.yaml",
                    "backend/deploy/gitops/inference-control/kustomization.yaml",
                    "backend/deploy/gitops/reports-control/kustomization.yaml"}

    def test_published_matrix_and_project_allowlist_match_the_complete_release(self):
        jobs = self.workflow["jobs"]
        matrix = jobs["images"]["strategy"]["matrix"]["include"]
        self.assertEqual(set(promotion.SERVICES), {row["name"] for row in matrix})
        self.assertEqual(len(promotion.SERVICES), len(matrix))
        steps = jobs["packaging"]["steps"]
        applications = next(step["env"]["APPLICATIONS"].split() for step in steps if "APPLICATIONS" in step.get("env", {}))
        self.assertEqual({row["application"] for row in matrix if row["application"]}, set(applications))
        self.assertIn("Arkya.Reports.Api", applications)
        self.assertNotIn("Arkya.Admin.Mcp", applications)
        self.assertTrue(any("inference-control/Test-Manifests.py" in step.get("run", "") for step in steps))
        self.assertTrue(any("reports-control/Test-Manifests.py" in step.get("run", "") for step in steps))

    def test_only_the_three_promoted_pin_files_are_excluded_and_committed(self):
        push_paths = self.workflow["on"]["push"]["paths"]
        self.assertEqual(self.pins, {value[1:] for value in push_paths if value.startswith("!")})
        steps = self.workflow["jobs"]["promote"]["steps"]
        command = next(step["run"] for step in steps if "Promote-ServiceImages.py" in step.get("run", ""))
        exclusions = set(promotion.re.findall(r"'\:\(exclude\)([^']+)'", command))
        self.assertEqual(self.pins, exclusions)
        git_add = next(line.strip() for line in command.splitlines() if line.strip().startswith("git add -- "))
        self.assertEqual(self.pins, set(git_add.removeprefix("git add -- ").split()))
        self.assertIn("--inference-kustomization backend/deploy/gitops/inference-control/kustomization.yaml", command)
        self.assertIn("--reports-kustomization backend/deploy/gitops/reports-control/kustomization.yaml", command)
        guard = command.split("assert_current_source() {", 1)[1].split("\n}", 1)[0]
        self.assertIn('--assert-source-commit "$GITHUB_SHA"', guard)
        self.assertIn('git diff --quiet "$GITHUB_SHA" HEAD', guard)
        self.assertGreaterEqual(command.count("assert_current_source\n"), 2)
        checkout = steps[0]["with"]["sparse-checkout"].split()
        self.assertTrue(all(any(path.startswith(directory + "/") for directory in checkout) for path in self.pins))


if __name__ == "__main__":
    unittest.main()

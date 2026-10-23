"""Exercise legacy and handoff Admin contracts on temporary GitOps copies; never contacts a cluster."""
from copy import deepcopy
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

import yaml

GITOPS = Path(__file__).resolve().parents[2] / "backend/deploy/gitops"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PartitionHandoffManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="arkya-handoff-contract-")
        cls.root = Path(cls.temporary.name) / "gitops"
        shutil.copytree(GITOPS, cls.root)
        shutil.copytree(GITOPS.parent / "gateway/traefik", cls.root.parent / "gateway/traefik")
        cls.validator = load("arkya_handoff_admin_fixture", cls.root / "admin/Validate-PlayerManagement.py")
        cls.handoff = load("arkya_handoff_component_fixture", cls.root / "partition-handoff/Validate-Manifests.py")
        cls.admin_path = cls.root / "admin/kustomization.yaml"
        cls.config_path = cls.root / "partition-handoff/realms-config.yaml"
        cls.rollouts_path = cls.root / "partition-handoff/rollouts.yaml"
        cls.admin = yaml.safe_load(cls.admin_path.read_text(encoding="utf-8"))
        cls.admin["components"] = [value for value in cls.admin["components"] if value != "../partition-handoff"]
        cls.handoff.characters_patch(cls.admin, "island")
        cls.config = yaml.safe_load(cls.config_path.read_text(encoding="utf-8"))
        cls.rollouts = list(yaml.safe_load_all(cls.rollouts_path.read_text(encoding="utf-8")))
        realms = next(yaml.safe_load(entry["patch"])["data"] for entry in cls.admin["patches"] if "patch" in entry and
            yaml.safe_load(entry["patch"]).get("metadata", {}).get("name") == "arkya-realms-config")
        cls.legacy_image = realms["Deployment__Profiles__0__Image"]

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def stage(self, phase):
        admin, config, rollouts = deepcopy(self.admin), deepcopy(self.config), deepcopy(self.rollouts)
        active = phase != "inactive"
        if active:
            admin["components"].append("../partition-handoff")
            self.handoff.characters_patch(admin, None)
        config["data"]["Deployment__Profiles__1__Image"] = (
            "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:" + "1" * 64 if active else self.legacy_image)
        config["data"]["Deployment__RuntimeReleases__0__PreviousProfileSha256"] = "2" * 64 if active else "0" * 64
        config["data"]["Deployment__RuntimeReleases__0__Automatic"] = "true" if phase == "enabled" else "false"
        for patch in rollouts:
            patch["spec"]["template"]["metadata"]["annotations"]["arkya.online/partition-handoff-config-revision"] = (
                "enabled-v1" if phase == "enabled" and patch["metadata"]["name"] == "arkya-realms" else "prepared-v1")
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False, allow_unicode=True), encoding="utf-8")
        self.config_path.write_text(yaml.safe_dump(config, sort_keys=False, allow_unicode=True), encoding="utf-8")
        self.rollouts_path.write_text(yaml.safe_dump_all(rollouts, sort_keys=False, allow_unicode=True), encoding="utf-8")
        return self.validator.checks.render(self.admin_path.parent)

    def test_global_contract_accepts_inactive_prepared_and_enabled_exact_forms(self):
        for phase in ("inactive", "prepared", "enabled"):
            with self.subTest(phase=phase):
                self.validator.check_profile(self.stage(phase), require_digests=True)

    def test_undeclared_handoff_configuration_is_rejected(self):
        documents = self.stage("inactive")
        config = next(item for item in documents if item["kind"] == "ConfigMap" and item["metadata"]["name"].startswith("arkya-characters-config-"))
        config["data"]["RealmRuntime__ServerIds__0"] = "island"
        with self.assertRaisesRegex(AssertionError, "explicitly adopted"):
            self.validator.check_profile(documents)

    def test_active_component_rejects_placeholder_image_and_fingerprint(self):
        for field, value in (("Deployment__Profiles__1__Image", self.legacy_image),
                             ("Deployment__RuntimeReleases__0__PreviousProfileSha256", "0" * 64)):
            with self.subTest(field=field):
                self.stage("prepared")
                config = yaml.safe_load(self.config_path.read_text(encoding="utf-8"))
                config["data"][field] = value
                self.config_path.write_text(yaml.safe_dump(config, sort_keys=False), encoding="utf-8")
                with self.assertRaisesRegex(AssertionError, "placeholders"):
                    self.validator.check_profile(self.validator.checks.render(self.admin_path.parent))

    def test_legacy_server_id_and_wrong_enabled_rollout_phase_are_rejected(self):
        self.stage("prepared")
        admin = yaml.safe_load(self.admin_path.read_text(encoding="utf-8"))
        self.handoff.characters_patch(admin, "island")
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False), encoding="utf-8")
        with self.subTest(case="legacy ServerId"), self.assertRaises(AssertionError):
            self.validator.check_profile(self.validator.checks.render(self.admin_path.parent))
        self.stage("enabled")
        rollouts = list(yaml.safe_load_all(self.rollouts_path.read_text(encoding="utf-8")))
        rollouts[2]["spec"]["template"]["metadata"]["annotations"]["arkya.online/partition-handoff-config-revision"] = "prepared-v1"
        self.rollouts_path.write_text(yaml.safe_dump_all(rollouts), encoding="utf-8")
        with self.subTest(case="wrong phase"), self.assertRaisesRegex(AssertionError, "stage annotation"):
            self.validator.check_profile(self.validator.checks.render(self.admin_path.parent))

    def test_rendered_policy_and_route_drift_are_not_masked_by_normalization(self):
        for kind in ("policy", "route"):
            with self.subTest(kind=kind):
                documents = self.stage("prepared")
                if kind == "policy":
                    policy = next(item for item in documents if item["kind"] == "NetworkPolicy" and item["metadata"]["name"] == "arkya-beta-web-partitions-game")
                    policy["spec"]["ingress"][0]["from"] = [{"ipBlock": {"cidr": "0.0.0.0/0"}}]
                else:
                    config = next(item for item in documents if item["kind"] == "ConfigMap" and item["metadata"]["name"].startswith("arkya-gateway-config-"))
                    config["data"]["Gateway__RealmGamePartitions__0__Address"] = "http://unexpected.example/"
                with self.assertRaisesRegex(AssertionError, "exact validated component"):
                    self.validator.check_profile(documents)

    def test_existing_security_contract_still_rejects_source_overlay_drift(self):
        self.stage("prepared")
        admin = yaml.safe_load(self.admin_path.read_text(encoding="utf-8"))
        patch = next(entry for entry in admin["patches"] if "patch" in entry and
                     yaml.safe_load(entry["patch"]).get("metadata", {}).get("name") == "arkya-characters-config")
        value = yaml.safe_load(patch["patch"])
        value["data"]["GameAccess__RequireBetaAccess"] = "false"
        patch["patch"] = yaml.safe_dump(value, sort_keys=False)
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False), encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "enforce beta rights"):
            self.validator.check_profile(self.validator.checks.render(self.admin_path.parent))


if __name__ == "__main__":
    unittest.main()

"""Exercise finite Nova adoption and unchanged legacy guards on temporary manifests."""
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


class NativeNovaManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="arkya-native-nova-contract-")
        cls.root = Path(cls.temporary.name) / "gitops"
        shutil.copytree(GITOPS, cls.root)
        shutil.copytree(GITOPS.parent / "gateway/traefik", cls.root.parent / "gateway/traefik")
        cls.validator = load("arkya_native_nova_admin_fixture", cls.root / "admin/Validate-PlayerManagement.py")
        cls.native = load("arkya_native_nova_component_fixture", cls.root / "native-nova/Validate-Manifests.py")
        cls.admin_path = cls.root / "admin/kustomization.yaml"
        cls.admin = yaml.safe_load(cls.admin_path.read_text(encoding="utf-8"))
        cls.admin["components"] = [value for value in cls.admin["components"]
                                   if value not in {"../partition-handoff", "../native-nova"}]
        cls.originals = {path: path.read_bytes() for path in (cls.root / "native-nova").glob("*.yaml")}

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def stage(self, adopted=True):
        for path, value in self.originals.items():
            path.write_bytes(value)
        admin = deepcopy(self.admin)
        if adopted:
            admin["components"].append("../native-nova")
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False, allow_unicode=True), encoding="utf-8")
        return self.validator.checks.render(self.admin_path.parent)

    def write(self, name, value):
        (self.root / "native-nova" / name).write_text(
            yaml.safe_dump(value, sort_keys=False, allow_unicode=True), encoding="utf-8")

    def validate(self):
        return self.validator.check_profile(self.validator.checks.render(self.admin_path.parent), require_digests=True)

    def test_global_contract_accepts_preparation_and_exact_adoption(self):
        self.validator.check_profile(self.stage(False), require_digests=True)
        self.native.check()
        self.stage()
        self.validate()

    def test_adoption_preserves_legacy_profile_and_requires_no_new_credentials_or_workloads(self):
        self.stage()
        before, after = self.native.check(adopted=True)
        self.assertEqual(7, len(set(after) - set(before)))
        self.assertEqual({"Service", "NetworkPolicy"}, {key[0] for key in set(after) - set(before)})
        for app in ("characters", "realms"):
            name = next(key for key in before if key[:2] == ("ConfigMap", "arkya-beta") and
                        key[2].startswith(f"arkya-{app}-config"))
            for key, value in before[name]["data"].items():
                self.assertEqual(value, after[name]["data"][key])
        for key in before:
            if key[0] == "Deployment":
                self.assertEqual(before[key], after[key])

    def test_unadopted_native_markers_are_rejected(self):
        documents = self.stage(False)
        realms = next(document for document in documents if document["kind"] == "ConfigMap" and
                      document["metadata"]["name"].startswith("arkya-realms-config-"))
        realms["data"]["Deployment__Profiles__1__Id"] = "nova-native"
        with self.assertRaisesRegex(AssertionError, "explicitly adopted"):
            self.validator.check_profile(documents)

    def test_protocol_build_image_and_endpoint_drift_are_rejected(self):
        for field, value in (("Servers__0__ProtocolVersion", "2"),
                ("Servers__0__BuildId", "arkya-partition-handoff-v1"),
                ("Image", "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:" + "1" * 64),
                ("TopologyPublicEndpointTemplate", "wss://api.arkya.online/game/beta-web/{serverId}/")):
            with self.subTest(field=field):
                self.stage()
                config = self.native.read(self.root / "native-nova/realms-config.yaml")
                config["data"]["Deployment__Profiles__1__" + field] = value
                self.write("realms-config.yaml", config)
                with self.assertRaisesRegex(AssertionError, "immutable protocol 3"):
                    self.validate()

    def test_runtime_release_or_secret_settings_are_not_allowed(self):
        for field in ("Deployment__RuntimeReleases__0__Automatic", "RealmRuntime__DedicatedKeySeed",
                      "Deployment__Profiles__1__EnvironmentSecretName"):
            with self.subTest(field=field):
                self.stage()
                config = self.native.read(self.root / "native-nova/realms-config.yaml")
                config["data"][field] = "unexpected"
                self.write("realms-config.yaml", config)
                with self.assertRaisesRegex(AssertionError, "immutable protocol 3"):
                    self.validate()

    def test_missing_extra_or_wrong_realm_gateway_routes_are_rejected(self):
        for case in ("missing", "extra", "realm", "address"):
            with self.subTest(case=case):
                self.stage()
                config = self.native.read(self.root / "native-nova/gateway-config.yaml")
                if case == "missing":
                    config["data"].pop("Gateway__RealmGamePartitions__3__Address")
                elif case == "extra":
                    config["data"]["Gateway__RealmGamePartitions__4__RealmId"] = "nova"
                else:
                    field = "RealmId" if case == "realm" else "Address"
                    config["data"]["Gateway__RealmGamePartitions__0__" + field] = "beta-web"
                self.write("gateway-config.yaml", config)
                with self.assertRaisesRegex(AssertionError, "exact routes"):
                    self.validate()

    def test_rendered_route_or_policy_drift_is_not_masked_by_normalization(self):
        for case in ("route", "policy", "missing service"):
            with self.subTest(case=case):
                documents = self.stage()
                if case == "route":
                    config = next(document for document in documents if document["kind"] == "ConfigMap" and
                                  document["metadata"]["name"].startswith("arkya-gateway-config-"))
                    config["data"]["Gateway__RealmGamePartitions__0__Address"] = "http://unexpected.example/"
                elif case == "policy":
                    policy = next(document for document in documents if document["metadata"]["name"] == "arkya-nova-partitions-game")
                    policy["spec"]["ingress"][0]["from"] = [{"ipBlock": {"cidr": "0.0.0.0/0"}}]
                else:
                    documents = [document for document in documents if document["metadata"]["name"] != "arkya-nova-partition-04"]
                with self.assertRaisesRegex(AssertionError, "exact validated component"):
                    self.validator.check_profile(documents)

    def test_unbounded_source_policy_is_rejected(self):
        self.stage()
        path = self.root / "native-nova/network-policies.yaml"
        policies = list(yaml.safe_load_all(path.read_text(encoding="utf-8")))
        policies[0]["spec"]["podSelector"]["matchExpressions"][0]["values"].append("island-other-01")
        path.write_text(yaml.safe_dump_all(policies, sort_keys=False), encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "finite realm/server policies"):
            self.validate()

    def test_normalization_does_not_restore_tampered_legacy_values(self):
        for field, value in (("Deployment__Profiles__0__Servers__0__ProtocolVersion", "3"),
                ("Deployment__RuntimeReleases__0__Automatic", "true")):
            with self.subTest(field=field):
                documents = self.stage()
                config = next(document for document in documents if document["kind"] == "ConfigMap" and
                              document["metadata"]["name"].startswith("arkya-realms-config-"))
                config["data"][field] = value
                with self.assertRaisesRegex(AssertionError, "beta profile|explicitly adopted"):
                    self.validator.check_profile(documents)

    def test_foreign_service_selector_and_extra_owned_resources_are_rejected(self):
        for case in ("selector", "secret"):
            with self.subTest(case=case):
                self.stage()
                path = self.root / "native-nova/services.yaml"
                services = list(yaml.safe_load_all(path.read_text(encoding="utf-8")))
                if case == "selector":
                    services[0]["spec"]["selector"]["arkya.online/realm-id"] = "beta-web"
                else:
                    services.append({"apiVersion": "v1", "kind": "Secret", "metadata": {"name": "unwanted"}})
                path.write_text(yaml.safe_dump_all(services, sort_keys=False), encoding="utf-8")
                with self.assertRaisesRegex(AssertionError, "four private Services"):
                    self.validate()

    def test_source_beta_rights_and_dynamic_identity_guards_remain_enforced(self):
        for field in ("GameAccess__RequireBetaAccess", "RealmRuntime__DynamicRealmsEnabled"):
            with self.subTest(field=field):
                self.stage()
                admin = yaml.safe_load(self.admin_path.read_text(encoding="utf-8"))
                patch = next(entry for entry in admin["patches"] if "patch" in entry and
                             yaml.safe_load(entry["patch"]).get("metadata", {}).get("name") == "arkya-characters-config")
                config = yaml.safe_load(patch["patch"])
                config["data"][field] = "false"
                patch["patch"] = yaml.safe_dump(config, sort_keys=False)
                self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False), encoding="utf-8")
                with self.assertRaisesRegex(AssertionError, "beta rights|derive dedicated identities"):
                    self.validate()

    def test_source_legacy_profile_pair_remains_enforced(self):
        self.stage()
        admin = yaml.safe_load(self.admin_path.read_text(encoding="utf-8"))
        patch = next(entry for entry in admin["patches"] if "patch" in entry and
                     yaml.safe_load(entry["patch"]).get("metadata", {}).get("name") == "arkya-realms-config")
        config = yaml.safe_load(patch["patch"])
        config["data"]["Deployment__Profiles__0__Servers__0__ProtocolVersion"] = "3"
        patch["patch"] = yaml.safe_dump(config, sort_keys=False)
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False), encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "beta profile"):
            self.validate()

    def test_partition_handoff_cannot_be_adopted_simultaneously(self):
        self.stage()
        admin = yaml.safe_load(self.admin_path.read_text(encoding="utf-8"))
        admin["components"].append("../partition-handoff")
        self.admin_path.write_text(yaml.safe_dump(admin, sort_keys=False), encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "same configuration slots"):
            self.native.check(adopted=True)


if __name__ == "__main__":
    unittest.main()

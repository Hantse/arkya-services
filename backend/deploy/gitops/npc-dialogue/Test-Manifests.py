"""Validate optional or active NPC dialogue configuration without contacting a cluster."""
import argparse
from copy import deepcopy
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent
ADMIN = ROOT.parent / "admin"
COMPONENT = "../npc-dialogue"
NS = "arkya-beta"
POLICY = ("NetworkPolicy", NS, "arkya-inference-dedicateds")
ROLE = ("Role", "arkya-beta-realm", "arkya-realm-deployer")
ROLE_RULE = {"apiGroups": [""], "resources": ["secrets"],
             "resourceNames": ["arkya-npc-dialogue"], "verbs": ["get", "delete"]}
DESTINATION = "http://arkya-inference.arkya-beta.svc.cluster.local:8080/"
NPC_ENV = [{"name": "NpcDialogue__Enabled", "value": "true"},
           {"name": "NpcDialogue__KeySeed", "valueFrom": {"secretKeyRef": {
               "name": "arkya-admin-secrets", "key": "ContentApi__AdminKey"}}}]


def render(path):
    result = subprocess.run([os.environ.get("KUBECTL", "kubectl"), "kustomize", str(path)], check=True,
                            capture_output=True, text=True, encoding="utf-8")
    return [item for item in yaml.safe_load_all(result.stdout) if item]


def keyed(items):
    result = {(row["kind"], row["metadata"].get("namespace", ""), row["metadata"]["name"]): row for row in items}
    assert len(result) == len(items), "Duplicate Kubernetes resource"
    return result


def npc_environment(row):
    return row["name"].startswith(("NpcDialogue__", "RealmRuntime__Inference"))


def without_component(documents):
    """Validate every NPC addition, then retain all pre-existing profile invariants."""
    resources = keyed(documents)
    markers = POLICY in resources or any(ROLE_RULE == rule for rule in resources.get(ROLE, {}).get("rules", []))
    markers = markers or any(npc_environment(row) for document in documents if document["kind"] == "Deployment"
                             for container in document["spec"]["template"]["spec"]["containers"]
                             for row in container.get("env", []))
    if not markers:
        return documents
    expected_policy = yaml.safe_load((ROOT / "network-policy.yaml").read_text(encoding="utf-8"))
    assert resources.get(POLICY) == expected_policy, "NPC ingress must remain scoped to managed realm pods on TCP 8080"
    normalized = deepcopy([row for row in documents if (row["kind"], row["metadata"].get("namespace", ""), row["metadata"]["name"]) != POLICY])
    normalized_resources = keyed(normalized)
    for app in ("inference", "realms"):
        deployment = normalized_resources["Deployment", NS, "arkya-" + app]
        container, = deployment["spec"]["template"]["spec"]["containers"]
        expected = (NPC_ENV if app == "inference" else [NPC_ENV[1], {
            "name": "RealmRuntime__InferenceBaseUrl", "value": DESTINATION}])
        additions = [row for row in container.get("env", []) if npc_environment(row)]
        assert len(additions) == len(expected) and all(additions.count(row) == 1 for row in expected), \
            f"{app} NPC settings must use only the enabled flag, exact existing seed reference and private origin"
        container["env"] = [row for row in container["env"] if not npc_environment(row)]
    inference = normalized_resources["Deployment", NS, "arkya-inference"]["spec"]
    assert inference["replicas"] == 1 and inference["strategy"] == {"type": "Recreate"}, \
        "The temporary dialogue queue requires one non-overlapping coordinator"
    original = next(row for row in yaml.safe_load_all((ROOT.parent / "inference-control/inference.yaml").read_text(encoding="utf-8"))
                    if row["kind"] == "Deployment")["spec"]
    if "strategy" in original:
        inference["strategy"] = deepcopy(original["strategy"])
    else:
        del inference["strategy"]
    role = normalized_resources[ROLE]
    assert role["rules"].count(ROLE_RULE) == 1 and role["rules"][-1] == ROLE_RULE, \
        "NPC credentials may add only fixed-name get/delete permissions"
    role["rules"].pop()
    return normalized


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    source = yaml.safe_load((ADMIN / "kustomization.yaml").read_text(encoding="utf-8"))
    count = source.get("components", []).count(COMPONENT)
    assert count in (0, 1), "NPC dialogue must not be included more than once"
    active = render(ADMIN)
    with tempfile.TemporaryDirectory(prefix="arkya-npc-dialogue-") as temporary:
        copied = Path(temporary) / "gitops"
        shutil.copytree(ROOT.parent, copied)
        target = copied / "admin/kustomization.yaml"
        preview = deepcopy(source)
        preview["components"] = [item for item in preview.get("components", []) if item != COMPONENT]
        target.write_text(yaml.safe_dump(preview, sort_keys=False, allow_unicode=True), encoding="utf-8")
        before = render(target.parent)
        preview["components"].append(COMPONENT)
        target.write_text(yaml.safe_dump(preview, sort_keys=False, allow_unicode=True), encoding="utf-8")
        after = render(target.parent)
    assert keyed(active) == keyed(after if count else before), "Active Admin must match its selected NPC stage"
    assert POLICY in keyed(after), "NPC activation must add its exact dedicated ingress policy"
    assert keyed(without_component(after)) == keyed(before), "NPC dialogue must preserve every unrelated resource and field"
    assert POLICY not in keyed(before), "The inactive profile must not contain NPC additions"
    for document in after:
        assert document["kind"] != "Secret", "GitOps must not contain dialogue credentials"
        if document["kind"] == "ConfigMap":
            assert not any(key.endswith(("__Key", "__ServiceKey", "__KeySeed", "__AdminKey", "__ConnectionString", "__Password", "__ClientSecret"))
                           for key in document["data"]), "Credential configuration must not enter ConfigMaps"
    admin_spec = importlib.util.spec_from_file_location("arkya_npc_admin_contract", ADMIN / "Validate-PlayerManagement.py")
    admin = importlib.util.module_from_spec(admin_spec)
    admin_spec.loader.exec_module(admin)
    admin.check_profile(after, require_digests=args.require_digests)
    print(f"NPC dialogue {'active' if count else 'preview'} validated: purpose-specific seed references, single coordinator, scoped realm access, existing resources preserved.")


if __name__ == "__main__":
    main()

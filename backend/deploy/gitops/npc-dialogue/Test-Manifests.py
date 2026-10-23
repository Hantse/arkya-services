"""Render the optional NPC dialogue component without contacting a cluster."""
import os
from pathlib import Path
import subprocess
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent


def render(path):
    result = subprocess.run(["kubectl", "kustomize", str(path)], check=True,
                            capture_output=True, text=True, encoding="utf-8")
    return [item for item in yaml.safe_load_all(result.stdout) if item]


def main():
    baseline = render(ROOT.parent / "admin")
    with tempfile.TemporaryDirectory(prefix=".npc-dialogue-preview-", dir=ROOT.parent) as temporary:
        preview = Path(temporary)
        (preview / "kustomization.yaml").write_text(yaml.safe_dump({
            "apiVersion": "kustomize.config.k8s.io/v1beta1", "kind": "Kustomization",
            "resources": [Path(os.path.relpath(ROOT.parent / "admin", preview)).as_posix()],
            "components": [Path(os.path.relpath(ROOT, preview)).as_posix()]
        }), encoding="utf-8")
        objects = render(preview)

    def keyed(items):
        return {(row["kind"], row["metadata"].get("namespace", ""), row["metadata"]["name"]): row for row in items}

    before, after = keyed(baseline), keyed(objects)
    ns = "arkya-beta"
    changes = {("Deployment", ns, "arkya-inference"), ("Deployment", ns, "arkya-realms"),
               ("Role", "arkya-beta-realm", "arkya-realm-deployer")}
    for key in before:
        if key not in changes:
            assert after[key] == before[key], f"Unrelated resource changed: {key}"
    assert set(after) - set(before) == {("NetworkPolicy", ns, "arkya-inference-dedicateds")}
    inference = after["Deployment", ns, "arkya-inference"]["spec"]
    assert inference["replicas"] == 1 and inference["strategy"] == {"type": "Recreate"}
    for name, prefix in (("arkya-inference", "NpcDialogue"), ("arkya-realms", "RealmRuntime")):
        container, = after["Deployment", ns, name]["spec"]["template"]["spec"]["containers"]
        env = {row["name"]: row for row in container["env"]}
        key_name = prefix + ("__ServiceKey" if prefix == "NpcDialogue" else "__InferenceServiceKey")
        assert env[key_name]["valueFrom"]["secretKeyRef"] == {"name": "arkya-npc-dialogue-secrets", "key": "service-key"}
        assert "value" not in env[key_name]
        if prefix == "NpcDialogue":
            assert env["NpcDialogue__Enabled"]["value"] == "true"
        else:
            assert env["RealmRuntime__InferenceBaseUrl"]["value"] == "http://arkya-inference.arkya-beta.svc.cluster.local:8080/"
    role = after["Role", "arkya-beta-realm", "arkya-realm-deployer"]
    assert role["rules"][:-1] == before["Role", "arkya-beta-realm", "arkya-realm-deployer"]["rules"]
    assert role["rules"][-1] == {"apiGroups": [""], "resources": ["secrets"],
                                 "resourceNames": ["arkya-npc-dialogue"], "verbs": ["get", "delete"]}
    ingress = after["NetworkPolicy", ns, "arkya-inference-dedicateds"]["spec"]["ingress"]
    assert ingress == [{"from": [{"namespaceSelector": {"matchLabels": {
        "kubernetes.io/metadata.name": "arkya-beta-realm"}}, "podSelector": {"matchLabels": {
        "app.kubernetes.io/managed-by": "arkya-realms"}}}], "ports": [{"protocol": "TCP", "port": 8080}]}]
    print("NPC dialogue preview validated: secret references, single coordinator, scoped realm access, existing resources preserved.")


if __name__ == "__main__":
    main()

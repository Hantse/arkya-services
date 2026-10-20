"""Validate the additive Inference component against Admin without contacting Kubernetes."""
import argparse
import os
from pathlib import Path
import re
import subprocess
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent
ADMIN = ROOT.parent / "admin"
NS = "arkya-beta"


def render(path):
    result = subprocess.run(["kubectl", "kustomize", str(path)], check=False,
                            capture_output=True, text=True, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(result.stderr.strip())
    return [item for item in yaml.safe_load_all(result.stdout) if item]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    active = render(ADMIN)
    already_active = any(item["kind"] == "Deployment" and item["metadata"]["name"] == "arkya-inference" for item in active)
    if already_active:
        objects = active
    else:
        with tempfile.TemporaryDirectory(prefix=".inference-preview-", dir=ROOT.parent) as temporary:
            preview = Path(temporary)
            (preview / "kustomization.yaml").write_text(yaml.safe_dump({
                "apiVersion": "kustomize.config.k8s.io/v1beta1", "kind": "Kustomization",
                "resources": [Path(os.path.relpath(ADMIN, preview)).as_posix()],
                "components": [Path(os.path.relpath(ROOT, preview)).as_posix()]
            }), encoding="utf-8")
            objects = render(preview)
    by_key = {(item["kind"], item["metadata"].get("namespace", ""), item["metadata"]["name"]): item for item in objects}
    assert len(by_key) == len(objects), "Duplicate Kubernetes resource"
    component = yaml.safe_load((ROOT / "kustomization.yaml").read_text(encoding="utf-8"))
    assert "namespace" not in component, "A component must not rewrite its parent's namespaces"
    assert component["resources"] == ["inference.yaml"] and "secretGenerator" not in component
    declared = list(yaml.safe_load_all((ROOT / "inference.yaml").read_text(encoding="utf-8")))
    assert {item["kind"] for item in declared} == {"Deployment", "Service", "NetworkPolicy"}
    assert all(item["metadata"]["namespace"] == NS and item["metadata"]["name"] == "arkya-inference" for item in declared)
    assert not any("reports" in item["metadata"]["name"] for item in objects), "Reports must remain optional"
    if not already_active:
        before = {(item["kind"], item["metadata"].get("namespace", ""), item["metadata"]["name"])
                  for item in active if item["kind"] != "ConfigMap"}
        after = {key for key in by_key if key[0] != "ConfigMap"}
        assert after - before == {(kind, NS, "arkya-inference") for kind in ("Deployment", "Service", "NetworkPolicy")}
        assert before <= after, "Existing resources must be preserved"

    def config(name):
        matches = [item["data"] for item in objects if item["kind"] == "ConfigMap" and item["metadata"].get("namespace") == NS
                   and item["metadata"]["name"].startswith(name + "-config-")]
        value, = matches
        return value

    admin, gateway, inference = map(config, ["arkya-admin", "arkya-gateway", "arkya-inference"])
    assert admin["InferenceApi__Enabled"] == admin["InferenceApi__AllowClusterHttp"] == "true"
    assert gateway["Gateway__InferenceEnabled"] == "true"
    destination = "http://arkya-inference.arkya-beta.svc.cluster.local:8080/"
    assert admin["InferenceApi__BaseUrl"] == gateway["ReverseProxy__Clusters__inference__Destinations__primary__Address"] == destination
    assert inference["Mongo__DatabaseName"] == "arkya_inference"
    for item in objects:
        if item["kind"] == "ConfigMap":
            assert not any(key.endswith(("__Key", "__KeySeed", "__AdminKey", "__ConnectionString", "__Password", "__ClientSecret"))
                           for key in item["data"]), "A ConfigMap contains credential configuration"
    pod = by_key["Deployment", NS, "arkya-inference"]["spec"]["template"]["spec"]
    container, = pod["containers"]
    assert pod["automountServiceAccountToken"] is False
    assert pod["securityContext"]["runAsNonRoot"] is True
    assert container["securityContext"]["readOnlyRootFilesystem"] is True
    assert container["securityContext"]["allowPrivilegeEscalation"] is False
    assert container["securityContext"]["capabilities"]["drop"] == ["ALL"]
    assert all("configMapRef" in item for item in container["envFrom"]), "Only explicit existing secret keys may be imported"
    expected_seed = {"name": "InferenceOperator__KeySeed", "valueFrom": {"secretKeyRef": {
        "name": "arkya-admin-secrets", "key": "ContentApi__AdminKey"}}}
    expected_mongo = {"name": "Mongo__ConnectionString", "valueFrom": {"secretKeyRef": {
        "name": "arkya-content-secrets", "key": "Mongo__ConnectionString"}}}
    assert container["env"] == [expected_mongo, expected_seed]
    assert container["readinessProbe"]["httpGet"]["path"] == "/health/ready"
    admin_container, = by_key["Deployment", NS, "arkya-admin"]["spec"]["template"]["spec"]["containers"]
    assert expected_seed in admin_container["env"]
    assert {entry["secretRef"]["name"] for entry in admin_container["envFrom"] if "secretRef" in entry} == {"arkya-admin-secrets"}
    assert by_key["Service", NS, "arkya-inference"]["spec"]["type"] == "ClusterIP"
    policy = by_key["NetworkPolicy", NS, "arkya-inference"]["spec"]
    assert policy["policyTypes"] == ["Ingress"]
    assert policy["ingress"] == [{"from": [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": name}}}
                                               for name in ("arkya-gateway", "arkya-admin")],
                                  "ports": [{"protocol": "TCP", "port": 8080}]}]
    if args.require_digests:
        for item in objects:
            if item["kind"] == "Deployment":
                image = item["spec"]["template"]["spec"]["containers"][0]["image"]
                assert re.fullmatch(r"ghcr\.io/hantse/arkya-services-[a-z]+@sha256:[0-9a-f]{64}", image), f"Unpublished image: {image}"
    print(f"Validated {len(objects)} objects: additive Inference, existing secret references, no Reports ({'active' if already_active else 'preview'}).")


if __name__ == "__main__":
    main()

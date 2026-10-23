"""Validate the optional inference control plane without accessing a cluster."""
import argparse
from pathlib import Path
import re
import subprocess

import yaml


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    rendered = subprocess.run(["kubectl", "kustomize", str(Path(__file__).resolve().parent)],
                              check=True, capture_output=True, text=True, encoding="utf-8").stdout
    objects = [item for item in yaml.safe_load_all(rendered) if item]
    by_key = {(item["kind"], item["metadata"]["name"]): item for item in objects}
    assert not any(item["kind"] in {"Secret", "Ingress"} for item in objects)
    namespaces = [item["metadata"]["name"] for item in objects if item["kind"] == "Namespace"]
    assert namespaces == ["arkya-beta"], namespaces
    assert all(item["metadata"].get("namespace") == "arkya-beta"
               for item in objects if item["kind"] != "Namespace")

    def config(name):
        return next(item["data"] for item in objects if item["kind"] == "ConfigMap"
                    and item["metadata"]["name"].startswith(name + "-config-"))

    admin, gateway, inference = map(config, ["arkya-admin", "arkya-gateway", "arkya-inference"])
    assert admin["InferenceApi__Enabled"] == admin["InferenceApi__AllowClusterHttp"] == "true"
    assert gateway["Gateway__InferenceEnabled"] == "true"
    assert admin["InferenceApi__BaseUrl"] == gateway["ReverseProxy__Clusters__inference__Destinations__primary__Address"]
    assert inference["Mongo__DatabaseName"] == "arkya_inference"
    for item in objects:
        if item["kind"] == "ConfigMap":
            assert not any(key.endswith(("__Key", "__AdminKey", "__ConnectionString", "__Password", "__ClientSecret")) for key in item["data"])
    pod = by_key["Deployment", "arkya-inference"]["spec"]["template"]["spec"]
    container, = pod["containers"]
    assert pod["automountServiceAccountToken"] is False
    assert container["securityContext"]["readOnlyRootFilesystem"] is True
    assert container["securityContext"]["allowPrivilegeEscalation"] is False
    assert {"secretRef": {"name": "arkya-inference-secrets"}} in container["envFrom"]
    assert container["readinessProbe"]["httpGet"]["path"] == "/health/ready"
    admin_container, = by_key["Deployment", "arkya-admin"]["spec"]["template"]["spec"]["containers"]
    for secret in ["arkya-admin-secrets", "arkya-admin-reports-secrets", "arkya-admin-inference-secrets"]:
        assert {"secretRef": {"name": secret}} in admin_container["envFrom"]
    assert by_key["Service", "arkya-inference"]["spec"]["type"] == "ClusterIP"
    peers = by_key["NetworkPolicy", "arkya-inference"]["spec"]["ingress"][0]["from"]
    assert {peer["podSelector"]["matchLabels"]["app.kubernetes.io/name"] for peer in peers} == {"arkya-admin", "arkya-gateway"}
    if args.require_digests:
        for item in objects:
            if item["kind"] == "Deployment":
                image = item["spec"]["template"]["spec"]["containers"][0]["image"]
                assert re.fullmatch(r"ghcr\.io/hantse/arkya-services-[a-z]+@sha256:[0-9a-f]{64}", image), f"Unpublished image: {image}"
    print(f"Validated {len(objects)} objects: private inference API, agent heartbeat routing and Admin secrets.")


if __name__ == "__main__":
    main()

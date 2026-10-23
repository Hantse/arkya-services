"""Validate the opt-in reports profile without contacting Kubernetes."""
import argparse
from pathlib import Path
import re
import subprocess

import yaml

ROOT = Path(__file__).resolve().parent


def render(path):
    output = subprocess.run(["kubectl", "kustomize", str(path)], check=True, capture_output=True, text=True, encoding="utf-8")
    return [item for item in yaml.safe_load_all(output.stdout) if item]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    objects = render(ROOT)
    assert not any(item["kind"] in {"Secret", "Namespace", "Ingress"} for item in objects)
    by_key = {(item["kind"], item["metadata"]["name"]): item for item in objects}
    assert all(item["metadata"].get("namespace") == "arkya-beta" for item in objects)
    deployments = [item for item in objects if item["kind"] == "Deployment"]
    assert len(deployments) == 7
    assert {item["metadata"]["name"] for item in deployments} == {
        "arkya-identity", "arkya-gateway", "arkya-frontend", "arkya-admin",
        "arkya-content", "arkya-realms", "arkya-reports"}

    def config(name):
        matches = [item["data"] for item in objects if item["kind"] == "ConfigMap" and item["metadata"]["name"].startswith(name + "-config-")]
        assert len(matches) == 1
        return matches[0]

    reports = config("arkya-reports")
    admin = config("arkya-admin")
    gateway = config("arkya-gateway")
    assert reports["Mongo__DatabaseName"] == "arkya_reports"
    assert reports["Identity__AllowClusterHttp"] == "true"
    assert reports["Identity__BaseUrl"] == "http://arkya-identity.arkya-beta.svc.cluster.local:8080/"
    assert admin["ReportsApi__Enabled"] == admin["AdminMcp__Enabled"] == "true"
    assert admin["ReportsApi__AllowClusterHttp"] == "true"
    assert admin["ReportsApi__BaseUrl"] == gateway["ReverseProxy__Clusters__reports__Destinations__primary__Address"]
    assert gateway["Gateway__ReportsEnabled"] == "true"
    assert gateway["Gateway__AdminHost"] == "admin.arkya.online"
    for item in objects:
        if item["kind"] == "ConfigMap":
            assert not any(key.endswith(("__Key", "__AdminKey", "__Password", "__ClientSecret", "__ServiceKey", "__ConnectionString")) for key in item["data"])
    pod = by_key["Deployment", "arkya-reports"]["spec"]["template"]["spec"]
    assert pod["automountServiceAccountToken"] is False
    container, = pod["containers"]
    assert container["securityContext"]["readOnlyRootFilesystem"] is True
    assert container["securityContext"]["allowPrivilegeEscalation"] is False
    assert {"secretRef": {"name": "arkya-reports-secrets"}} in container["envFrom"]
    assert container["readinessProbe"]["httpGet"]["path"] == "/health/ready"
    if args.require_digests:
        for deployment in deployments:
            image = deployment["spec"]["template"]["spec"]["containers"][0]["image"]
            assert re.fullmatch(r"ghcr\.io/hantse/arkya-services-[a-z]+@sha256:[0-9a-f]{64}", image), f"Unpublished image: {image}"
    else:
        assert container["image"].startswith(("ghcr.io/hantse/arkya-services-reports:", "ghcr.io/hantse/arkya-services-reports@sha256:"))
    admin_container, = by_key["Deployment", "arkya-admin"]["spec"]["template"]["spec"]["containers"]
    assert {"secretRef": {"name": "arkya-admin-reports-secrets"}} in admin_container["envFrom"]
    assert {"secretRef": {"name": "arkya-admin-secrets"}} in admin_container["envFrom"]
    assert by_key["Service", "arkya-reports"]["spec"]["type"] == "ClusterIP"
    peers = by_key["NetworkPolicy", "arkya-reports"]["spec"]["ingress"][0]["from"]
    assert {peer["podSelector"]["matchLabels"]["app.kubernetes.io/name"] for peer in peers} == {"arkya-gateway", "arkya-admin"}
    identity_peers = by_key["NetworkPolicy", "arkya-identity"]["spec"]["ingress"]
    assert any(rule["from"] == [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-reports"}}}] for rule in identity_peers)
    print(f"Validated {len(objects)} reports-profile objects, seven deployments, private network rules and secret references.")
    if not args.require_digests:
        print("Reports image is not activated: pin a published digest and rerun with --require-digests before deployment.")


if __name__ == "__main__":
    main()

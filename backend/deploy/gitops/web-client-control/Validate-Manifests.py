"""Render and check the static WebGL component without contacting a cluster."""

import importlib.util
from pathlib import Path
import shutil
import tempfile

import yaml


ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
require = checks.require
NAMESPACE = "arkya-beta"
NAME = "arkya-web-client"
IMAGE = "ghcr.io/hantse/arkya-mmorpg-web-client"
DIGEST = "sha256:5b295795065c77fb517e4b49bfb82076c00d0325d33cd51141c7756bbf00e5b7"
REVISION = "2b34ab6ff45c0faf56c230136e9d115c6c7aa177"
LABELS = {"app.kubernetes.io/name": NAME}


def render_component():
    manifest = yaml.safe_load((ROOT / "kustomization.yaml").read_text(encoding="utf-8"))
    require(manifest == {
        "apiVersion": "kustomize.config.k8s.io/v1alpha1", "kind": "Component",
        "resources": ["client.yaml"],
        "images": [{"name": IMAGE, "newName": IMAGE, "digest": DIGEST}],
    }, "Static client component must not add other resources, transforms or image promotions")
    with tempfile.TemporaryDirectory(prefix="arkya-web-client-") as directory:
        harness = Path(directory)
        component = harness / "component"
        component.mkdir()
        for name in ("kustomization.yaml", "client.yaml"):
            shutil.copyfile(ROOT / name, component / name)
        (harness / "kustomization.yaml").write_text(yaml.safe_dump({
            "apiVersion": "kustomize.config.k8s.io/v1beta1",
            "kind": "Kustomization",
            "components": ["component"],
        }), encoding="utf-8")
        return checks.render(harness)


def check_profile(documents):
    resources = checks.index(documents)
    expected = {(kind, NAMESPACE, NAME) for kind in ("Deployment", "Service", "NetworkPolicy")}
    require(set(resources) == expected,
            "Static client must own exactly its Deployment, private Service and ingress policy in arkya-beta")
    deployment = resources[("Deployment", NAMESPACE, NAME)]
    require(deployment["metadata"].get("annotations", {}).get("arkya.online/source-revision") == REVISION,
            "Deployment source revision must identify the selected Unity release")
    require(deployment["spec"]["replicas"] == 1 and deployment["spec"]["revisionHistoryLimit"] == 2,
            "Unexpected static client replica or history budget")
    require(deployment["spec"]["selector"] == {"matchLabels": LABELS}, "Incorrect Deployment selector")
    template = deployment["spec"]["template"]
    require(template["metadata"]["labels"] == LABELS and
            template["metadata"].get("annotations", {}).get("arkya.online/source-revision") == REVISION,
            "Pod identity and source revision must match the selected client")
    pod = template["spec"]
    require(pod["nodeSelector"] == {"kubernetes.io/os": "linux", "kubernetes.io/arch": "amd64"},
            "Client must use the published Linux AMD64 image")
    require(pod["automountServiceAccountToken"] is False and not pod.get("serviceAccountName"),
            "Static serving must not receive Kubernetes credentials")
    require(not any(pod.get(key) for key in ("hostNetwork", "hostPID", "hostIPC", "initContainers")),
            "Static client must not acquire host access or extra initialization workloads")
    require(pod["imagePullSecrets"] == [{"name": "arkya-ghcr"}], "Reuse the existing namespace registry Secret")
    require(pod["securityContext"] == {
        "runAsNonRoot": True, "runAsUser": 101, "runAsGroup": 101, "fsGroup": 101,
        "seccompProfile": {"type": "RuntimeDefault"},
    }, "Static client must retain its unprivileged runtime")
    container, = pod["containers"]
    require(container["name"] == "client" and container["image"] == f"{IMAGE}@{DIGEST}",
            "Only the explicitly selected immutable Unity WebGL image may run")
    require(not any(container.get(key) for key in ("env", "envFrom", "command", "args")),
            "Static client must not receive account/service secrets or override the image startup")
    require(container["ports"] == [{"name": "http", "containerPort": 8080}], "Expose only the private HTTP port")
    require(container["securityContext"] == {
        "allowPrivilegeEscalation": False, "readOnlyRootFilesystem": True, "capabilities": {"drop": ["ALL"]},
    }, "Static client must retain a read-only root and no extra capabilities")
    require(container["resources"] == {
        "requests": {"cpu": "50m", "memory": "64Mi"}, "limits": {"cpu": "1", "memory": "256Mi"},
    }, "Static client resource budget changed")
    for name, period in (("readinessProbe", 5), ("startupProbe", 2), ("livenessProbe", 10)):
        probe = {"httpGet": {"path": "/healthz", "port": "http"}, "periodSeconds": period}
        if name == "startupProbe":
            probe["failureThreshold"] = 15
        require(container[name] == probe, f"Incorrect static client {name}")
    require(container["volumeMounts"] == [{"name": "tmp", "mountPath": "/tmp"}] and
            pod["volumes"] == [{"name": "tmp", "emptyDir": {"sizeLimit": "128Mi"}}],
            "Only bounded temporary storage may be mounted; no credentials or host paths")
    service = resources[("Service", NAMESPACE, NAME)]["spec"]
    require(service == {
        "type": "ClusterIP", "selector": LABELS,
        "ports": [{"name": "http", "port": 8080, "targetPort": "http", "protocol": "TCP"}],
    }, "Service must remain private and target exactly the static client HTTP listener")
    policy = resources[("NetworkPolicy", NAMESPACE, NAME)]["spec"]
    require(policy == {
        "podSelector": {"matchLabels": LABELS}, "policyTypes": ["Ingress"],
        "ingress": [{"from": [{
            "namespaceSelector": {"matchLabels": {"kubernetes.io/metadata.name": NAMESPACE}},
            "podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-frontend"}},
        }], "ports": [{"port": 8080, "protocol": "TCP"}]}],
    }, "Only the frontend in its exact namespace may reach the static client")
    return resources


if __name__ == "__main__":
    documents = render_component()
    check_profile(documents)
    print(f"Static WebGL client: {len(documents)} resources; pinned image, private serving and runtime confinement validated offline.")

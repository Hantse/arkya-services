"""Check the realm namespace, least-privilege RBAC and WSS routing offline."""

import importlib.util
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
require = checks.require
NS = "arkya-beta-realm"
OPERATIONS = "arkya-beta"
GAME = {
    "app.kubernetes.io/managed-by": "arkya-realms",
    "arkya.online/realm-id": "beta-web",
    "arkya.online/server-id": "island",
}


def check_profile(documents):
    resources = checks.index(documents)
    require(len(resources) == 9, "Unexpected realm control resources")
    require(not any(item["kind"] in {"Secret", "Deployment", "Ingress", "ClusterRole", "ClusterRoleBinding"}
                    for item in documents), "Realm GitOps must not own dedicated workloads, credentials, TLS ingress or cluster-wide RBAC")
    namespace = resources[("Namespace", "", NS)]
    account = resources[("ServiceAccount", OPERATIONS, "arkya-realm-deployer")]
    require(account["automountServiceAccountToken"] is False,
            "Kubernetes credentials must be explicitly projected only into Realms")
    role = resources[("Role", NS, "arkya-realm-deployer")]
    require(role["rules"] == [
        {"apiGroups": ["apps"], "resources": ["deployments"], "verbs": ["get", "create"]},
        {"apiGroups": [""], "resources": ["services"], "verbs": ["get", "create"]},
        {"apiGroups": [""], "resources": ["secrets"], "resourceNames": ["arkya-ghcr", "arkya-dedicated"], "verbs": ["get"]},
        {"apiGroups": [""], "resources": ["secrets"], "verbs": ["create"]},
    ], "Realms may create/observe workloads and provision only the fixed registry/dedicated Secrets without listing or updating credentials")
    binding = resources[("RoleBinding", NS, "arkya-realm-deployer")]
    require(binding["subjects"] == [{"kind": "ServiceAccount", "namespace": OPERATIONS,
                                     "name": "arkya-realm-deployer"}] and
            binding["roleRef"] == {"apiGroup": "rbac.authorization.k8s.io", "kind": "Role",
                                   "name": "arkya-realm-deployer"}, "Incorrect cross-namespace Role binding")
    require(checks.sync_wave(namespace) < checks.sync_wave(role) <= checks.sync_wave(binding),
            "Namespace and credentials must exist before workloads roll out")
    service = resources[("Service", NS, "arkya-beta-web-island")]
    require(service["spec"] == {
        "type": "ClusterIP", "selector": GAME,
        "ports": [{"name": "game", "port": 17943, "targetPort": 17943, "protocol": "TCP"}],
    }, "Public WSS routing must select only the configured beta island through a private TCP Service")
    deny = resources[("NetworkPolicy", NS, "arkya-realm-default-deny")]["spec"]
    require(deny == {"podSelector": {}, "policyTypes": ["Ingress"], "ingress": []},
            "Unselected realm pods must have no inbound access")
    game_policy = resources[("NetworkPolicy", NS, "arkya-beta-web-game-ingress")]["spec"]
    require(game_policy["podSelector"] == {"matchLabels": GAME} and game_policy["policyTypes"] == ["Ingress"] and
            game_policy["ingress"] == [{"from": [{"namespaceSelector": {"matchLabels": {
                "kubernetes.io/metadata.name": OPERATIONS}}, "podSelector": {"matchLabels": {
                    "app.kubernetes.io/name": "arkya-gateway"}}}], "ports": [{"port": 17943, "protocol": "TCP"}]}],
            "Only Gateway in its exact namespace may reach the game listener")
    admission = resources[("NetworkPolicy", OPERATIONS, "arkya-characters-realm-admission")]["spec"]
    require(admission["podSelector"] == {"matchLabels": {"app.kubernetes.io/name": "arkya-characters"}} and
            admission["ingress"] == [{"from": [{"namespaceSelector": {"matchLabels": {
                "kubernetes.io/metadata.name": NS}}, "podSelector": {"matchLabels": GAME}}],
                "ports": [{"port": 8080, "protocol": "TCP"}]}],
            "Dedicated admission must bind both the realm namespace and dedicated labels")
    catalogue = resources[("NetworkPolicy", OPERATIONS, "arkya-realms-player-catalogue")]["spec"]
    require(catalogue["podSelector"] == {"matchLabels": {"app.kubernetes.io/name": "arkya-realms"}} and
            catalogue["ingress"] == [{"from": [{"podSelector": {"matchLabels": {
                "app.kubernetes.io/name": "arkya-characters"}}}], "ports": [{"port": 8080, "protocol": "TCP"}]}],
            "Player realm discovery must remain internal to Characters")
    return resources


def check_project_patch():
    project = yaml.safe_load((ROOT.parent / "bootstrap" / "project.yaml").read_text(encoding="utf-8"))
    patch = yaml.safe_load((ROOT / "project-access.patch.yaml").read_text(encoding="utf-8"))
    expected_additions = [
        ("/spec/destinations/-", {"server": "https://kubernetes.default.svc", "namespace": NS}),
        ("/spec/namespaceResourceWhitelist/-", {"group": "", "kind": "ServiceAccount"}),
        ("/spec/namespaceResourceWhitelist/-", {"group": "rbac.authorization.k8s.io", "kind": "Role"}),
        ("/spec/namespaceResourceWhitelist/-", {"group": "rbac.authorization.k8s.io", "kind": "RoleBinding"}),
    ]
    expected = [
        {"op": "test", "path": "/spec/clusterResourceWhitelist/0/kind", "value": "Namespace"},
        {"op": "test", "path": "/spec/clusterResourceBlacklist", "value": [{"group": "*", "kind": "*"}]},
        {"op": "add", "path": "/spec/clusterResourceWhitelist/0/name", "value": NS},
        {"op": "replace", "path": "/spec/clusterResourceBlacklist", "value": []},
    ] + [{"op": "add", "path": path, "value": value} for path, value in expected_additions]
    require(patch == expected,
            "The historical project patch must guard the initial global deny before narrowing Namespace and appending the required access")
    require({"group": "", "kind": "Namespace"} in project["spec"]["clusterResourceWhitelist"],
            "The owning project must already permit Namespace creation")
    require(all(part in project["spec"] for part in ("destinations", "namespaceResourceWhitelist")),
            "The project patch must target existing lists")
    observations = yaml.safe_load((ROOT / "project-observability.patch.yaml").read_text(encoding="utf-8"))
    initial_whitelist = [{"group": group, "kind": kind} for group, kind in [
        ("", "ConfigMap"), ("", "Service"), ("", "PersistentVolumeClaim"), ("apps", "Deployment"),
        ("networking.k8s.io", "NetworkPolicy"), ("networking.k8s.io", "Ingress"), ("", "ServiceAccount"),
        ("rbac.authorization.k8s.io", "Role"), ("rbac.authorization.k8s.io", "RoleBinding")]]
    require(observations == [
        {"op": "test", "path": "/spec/namespaceResourceWhitelist", "value": initial_whitelist},
        {"op": "add", "path": "/spec/namespaceResourceWhitelist/-", "value": {"group": "", "kind": "Pod"}},
        {"op": "add", "path": "/spec/namespaceResourceWhitelist/-", "value": {"group": "apps", "kind": "ReplicaSet"}},
    ], "The observability patch must guard the exact observed list and append only Pod and ReplicaSet")


if __name__ == "__main__":
    documents = checks.render(ROOT)
    check_profile(documents)
    check_project_patch()
    print(f"Realm control: {len(documents)} resources; namespace, least-privilege RBAC, WSS route and network boundaries validated offline.")

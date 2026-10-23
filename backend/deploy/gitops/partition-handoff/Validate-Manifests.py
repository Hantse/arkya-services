"""Render the inactive handoff component and its planned Admin adoption offline."""
import argparse
from copy import deepcopy
import importlib.util
from pathlib import Path
import re
import shutil
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_handoff_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
require = checks.require
REALM_NAMESPACE = "arkya-beta-realm"
API_NAMESPACE = "arkya-beta"
SERVERS = [f"island-starter-island-{index:02}" for index in range(1, 5)]
COMPONENT = "../partition-handoff"
ZERO_HASH = "0" * 64


def read(path):
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def characters_patch(admin, server_id):
    for entry in admin["patches"]:
        if "patch" not in entry:
            continue
        patch = yaml.safe_load(entry["patch"])
        if not isinstance(patch, dict) or patch.get("kind") != "ConfigMap" or patch.get("metadata", {}).get("name") != "arkya-characters-config":
            continue
        if server_id is None:
            patch["data"].pop("RealmRuntime__ServerId", None)
        else:
            patch["data"]["RealmRuntime__ServerId"] = server_id
        entry["patch"] = yaml.safe_dump(patch, sort_keys=False, allow_unicode=True)
        return
    raise AssertionError("The Admin Characters patch was not found")


def canonical(documents):
    result = {}
    pattern = re.compile(r"\A(arkya-(?:gateway|characters|realms)-config)-[a-z0-9]+\Z")
    for document in deepcopy(documents):
        if document["kind"] == "ConfigMap":
            document["metadata"]["name"] = pattern.sub(r"\1", document["metadata"]["name"])
        if document["kind"] == "Deployment":
            for container in document["spec"]["template"]["spec"]["containers"]:
                for source in container.get("envFrom", []):
                    if "configMapRef" in source:
                        source["configMapRef"]["name"] = pattern.sub(r"\1", source["configMapRef"]["name"])
        key = checks.identity(document)
        require(key not in result, f"Duplicate normalized resource {key}")
        result[key] = document
    return result


def rendered_profiles(active):
    admin_source = read(ROOT.parent / "admin/kustomization.yaml")
    require((COMPONENT in admin_source["components"]) == active,
            "The component must remain inactive until the explicit --require-release validation")
    with tempfile.TemporaryDirectory(prefix="arkya-partition-handoff-") as directory:
        copied = Path(directory) / "gitops"
        shutil.copytree(ROOT.parent, copied)
        admin = copied / "admin/kustomization.yaml"
        baseline = deepcopy(admin_source)
        baseline["components"] = [value for value in baseline["components"] if value != COMPONENT]
        characters_patch(baseline, "island")
        admin.write_text(yaml.safe_dump(baseline, sort_keys=False, allow_unicode=True), encoding="utf-8")
        before = canonical(checks.render(admin.parent))
        adoption = deepcopy(baseline)
        adoption["components"].append(COMPONENT)
        characters_patch(adoption, None)
        admin.write_text(yaml.safe_dump(adoption, sort_keys=False, allow_unicode=True), encoding="utf-8")
        after = canonical(checks.render(admin.parent))
    if active:
        require(canonical(checks.render(ROOT.parent / "admin")) == after,
                "Active Admin must remove its legacy ServerId patch when adopting the component")
    return before, after


def partition_selector():
    return {"matchLabels": {"app.kubernetes.io/managed-by": "arkya-realms", "arkya.online/realm-id": "beta-web"},
            "matchExpressions": [{"key": "arkya.online/server-id", "operator": "In", "values": SERVERS}]}


def peer(namespace, selector):
    return {"namespaceSelector": {"matchLabels": {"kubernetes.io/metadata.name": namespace}}, "podSelector": selector}


def check(require_release=False, enable_migration=False):
    require(not enable_migration or require_release, "--enable-migration requires --require-release")
    require(read(ROOT / "kustomization.yaml") == {
        "apiVersion": "kustomize.config.k8s.io/v1alpha1", "kind": "Component",
        "resources": ["services.yaml", "network-policies.yaml"],
        "patches": [{"path": name} for name in ("gateway-config.yaml", "characters-config.yaml", "realms-config.yaml", "rollouts.yaml")],
    }, "The component may add only its explicit private Services, policies and three configuration patches")
    before, after = rendered_profiles(require_release)
    expected = deepcopy(before)
    for name in ("gateway", "characters", "realms"):
        patch = read(ROOT / f"{name}-config.yaml")
        require(patch["apiVersion"] == "v1" and patch["kind"] == "ConfigMap" and
                patch["metadata"] == {"name": f"arkya-{name}-config", "namespace": API_NAMESPACE},
                "Configuration patches must target the existing exact service ConfigMaps")
        allowed = {
            "gateway": lambda key: key.startswith("Gateway__RealmGamePartitions__"),
            "characters": lambda key: key == "RealmRuntime__ServerId" or key.startswith("RealmRuntime__ServerIds__"),
            "realms": lambda key: key.startswith("Deployment__Profiles__1__") or key.startswith("Deployment__RuntimeReleases__0__"),
        }[name]
        require(all(allowed(key) for key in patch["data"]), "Preparation must preserve every existing configuration and image-release pin")
        data = expected[("ConfigMap", API_NAMESPACE, f"arkya-{name}-config")]["data"]
        for key, value in patch["data"].items():
            if value is None:
                data.pop(key, None)
            else:
                data[key] = value
    rollout_annotation = "arkya.online/partition-handoff-config-revision"
    rollout_patches = list(yaml.safe_load_all((ROOT / "rollouts.yaml").read_text(encoding="utf-8")))
    require(len(rollout_patches) == 3, "Exactly three existing service Deployments must receive a stage annotation")
    for app, patch in zip(("gateway", "characters", "realms"), rollout_patches):
        revision = "enabled-v1" if app == "realms" and enable_migration else "prepared-v1"
        require(patch == {"apiVersion": "apps/v1", "kind": "Deployment",
            "metadata": {"name": "arkya-" + app, "namespace": API_NAMESPACE},
            "spec": {"template": {"metadata": {"annotations": {rollout_annotation: revision}}}}},
            "Rollout patches may change only the exact handoff stage annotation on three existing templates")
        metadata = expected[("Deployment", API_NAMESPACE, "arkya-" + app)]["spec"]["template"]["metadata"]
        metadata.setdefault("annotations", {})[rollout_annotation] = revision
    additions = [doc for name in ("services.yaml", "network-policies.yaml")
                 for doc in yaml.safe_load_all((ROOT / name).read_text(encoding="utf-8")) if doc]
    require(len(additions) == 7, "Exactly four private aliases and three scoped policies are required")
    for document in additions:
        key = checks.identity(document)
        require(key not in expected and document["kind"] in {"Service", "NetworkPolicy"} and
                checks.sync_wave(document) == -5, "The component must not own workloads, credentials, namespaces or RBAC")
        expected[key] = document
    require(after == expected, "Adoption changed resources beyond the seven additions, exact configurations and three stage annotations")
    for index, server in enumerate(SERVERS):
        name = f"arkya-beta-web-partition-{index + 1:02}"
        service = after[("Service", REALM_NAMESPACE, name)]["spec"]
        require(service == {"type": "ClusterIP", "selector": {
            "app.kubernetes.io/managed-by": "arkya-realms", "arkya.online/realm-id": "beta-web", "arkya.online/server-id": server},
            "ports": [{"name": "game", "port": 17943, "targetPort": 17943, "protocol": "TCP"}]},
            "Aliases must select only their exact realm/server on the private game port")
    game = after[("NetworkPolicy", REALM_NAMESPACE, "arkya-beta-web-partitions-game")]["spec"]
    require(game == {"podSelector": partition_selector(), "policyTypes": ["Ingress"], "ingress": [{
        "from": [peer(API_NAMESPACE, {"matchLabels": {"app.kubernetes.io/name": "arkya-gateway"}})],
        "ports": [{"port": 17943, "protocol": "TCP"}]}]}, "Only the existing Gateway may reach the four approved game listeners")
    for app, name in (("characters", "arkya-characters-partitions-admission"), ("realms", "arkya-realms-partitions-commands")):
        policy = after[("NetworkPolicy", API_NAMESPACE, name)]["spec"]
        require(policy == {"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-" + app}},
            "policyTypes": ["Ingress"], "ingress": [{"from": [peer(REALM_NAMESPACE, partition_selector())],
                "ports": [{"port": 8080, "protocol": "TCP"}]}]}, "Dedicated API access must bind the exact realm namespace and finite server list")
    config = lambda app: after[("ConfigMap", API_NAMESPACE, f"arkya-{app}-config")]["data"]
    gateway, characters, realms = config("gateway"), config("characters"), config("realms")
    route_data = {key: value for key, value in gateway.items() if key.startswith("Gateway__RealmGamePartitions__")}
    wanted_routes = {f"Gateway__RealmGamePartitions__{index}__{field}": value
        for index, server in enumerate(SERVERS) for field, value in {
            "RealmId": "beta-web", "ServerId": server,
            "Address": f"http://arkya-beta-web-partition-{index + 1:02}.{REALM_NAMESPACE}.svc.cluster.local:17943/"}.items()}
    require(route_data == wanted_routes and gateway["Gateway__RealmGameEnabled"] == "true", "Gateway partitions must map statically to their exact aliases")
    require("RealmRuntime__ServerId" not in characters and characters["RealmRuntime__RealmId"] == "beta-web" and
            {key: value for key, value in characters.items() if key.startswith("RealmRuntime__ServerIds__")} ==
            {f"RealmRuntime__ServerIds__{index}": value for index, value in enumerate(["island", *SERVERS])},
            "Characters must retain the legacy identity plus exactly four approved partition identities")
    profile = "Deployment__Profiles__1__"
    expected_profile = {
        "Id": "beta-web-handoff", "DisplayName": "Île bêta · quatre partitions", "Image": realms[profile + "Image"],
        "ImagePullSecretName": "arkya-ghcr", "TopologyTemplateServerId": "island",
        "TopologyPublicEndpointTemplate": "wss://api.arkya.online/game/beta-web/{serverId}/",
        "Servers__0__Id": "island", "Servers__0__Port": "17943", "Servers__0__Transport": "WebSocket",
        "Servers__0__PublicEndpoint": "wss://api.arkya.online/game/beta-web/island/",
        "Servers__0__ProtocolVersion": "2", "Servers__0__BuildId": "arkya-partition-handoff-v1"}
    require({key[len(profile):]: value for key, value in realms.items() if key.startswith(profile)} == expected_profile and
            re.fullmatch(r"ghcr\.io/hantse/arkya-mmorpg-dedicated@sha256:[0-9a-f]{64}", realms[profile + "Image"]),
            "The new profile must have one immutable image and the exact protocol 2 WebSocket template")
    release = "Deployment__RuntimeReleases__0__"
    expected_release = {
        "RealmId": "beta-web", "ProfileId": "beta-web-handoff", "Version": "1",
        "PreviousProfileSha256": realms[release + "PreviousProfileSha256"], "Automatic": "true" if enable_migration else "false",
        "Topology__Islands__0__IslandId": "starter-island", "Topology__Islands__0__DedicatedCount": "4",
        "Topology__Islands__0__GridColumns": "2", "Topology__Islands__0__GridRows": "2",
        "Topology__Islands__0__Width": "1536", "Topology__Islands__0__Depth": "1536",
        "Topology__Islands__0__OriginX": "0", "Topology__Islands__0__OriginZ": "0",
        "Topology__InstanceDedicatedCount": "0", "Topology__RaidDedicatedCount": "0"}
    require({key[len(release):]: value for key, value in realms.items() if key.startswith(release)} == expected_release and
            re.fullmatch(r"[0-9a-f]{64}", realms[release + "PreviousProfileSha256"]), "Runtime release must preserve the exact 1536m island plan and guarded transition")
    if require_release:
        require(realms[release + "PreviousProfileSha256"] != ZERO_HASH and
                realms[profile + "Image"] != realms["Deployment__Profiles__0__Image"],
                "Replace both preparation placeholders before activating the runtime migration")
    else:
        require(realms[release + "PreviousProfileSha256"] == ZERO_HASH and
                realms[profile + "Image"] == realms["Deployment__Profiles__0__Image"],
                "Inactive preparation must retain the current image and zero fingerprint placeholders")
    stage = "enabled migration" if enable_migration else "prepared adoption" if require_release else "inactive preparation"
    print(f"Partition handoff: {len(after)} resources; seven scoped additions, static routing, finite identities, preserved legacy pins, three exact rollout annotations and {stage} validated offline.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-release", action="store_true", help="Require adopted component, a new official image and production fingerprint; migration remains disabled")
    parser.add_argument("--enable-migration", action="store_true", help="Also require Automatic=true and the Realms enabled-v1 rollout annotation")
    arguments = parser.parse_args()
    check(arguments.require_release, arguments.enable_migration)

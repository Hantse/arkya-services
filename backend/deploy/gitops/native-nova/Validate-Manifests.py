"""Validate the finite Nova native configuration offline, without creating a realm."""
import argparse
from copy import deepcopy
import importlib.util
from pathlib import Path
import re
import shutil
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_native_nova_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
require = checks.require
COMPONENT = "../native-nova"
API_NAMESPACE = "arkya-beta"
REALM_NAMESPACE = "arkya-beta-realm"
SERVERS = [f"island-starter-island-{index:02}" for index in range(1, 5)]
NEXT_SERVERS = [f"v5-starter-island-{index:02}" for index in range(1, 5)]
IMAGE = "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:cbb0cb0dd74d8da65024e0e2beb760747e356734217533fd5d36d706f0de4b1f"
NEXT_IMAGE = "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:241b6d9eca943b6701c52aa99e76746e6fa3c5c7886e4a03e0c1067ea5ee9693"
GAME_VERSION = "0.1.49"
SOURCE_REVISION = "22bc5eeabce7dfadaba55a42e4b4e9cf40fd8460"
SOURCE_RUN_ID = "40095092545"
PREVIOUS_PROFILE_SHA256 = "c652962aa8072e2680ed4406fbcc3b3a6b373743560aa89ff50ef558f4ac1781"
CONFIG_PREFIXES = {"gateway": ("Gateway__RealmGamePartitions__",),
    "realms": ("Deployment__Profiles__1__", "Deployment__Profiles__2__", "Deployment__RuntimeReleases__0__")}


def read(path):
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def canonical(documents):
    indexed = {}
    pattern = re.compile(r"\A(arkya-(?:gateway|realms)-config)-[a-z0-9]+\Z")
    for document in deepcopy(documents):
        if document["kind"] == "ConfigMap":
            document["metadata"]["name"] = pattern.sub(r"\1", document["metadata"]["name"])
        if document["kind"] == "Deployment":
            for container in document["spec"]["template"]["spec"]["containers"]:
                for entry in container.get("envFrom", []):
                    if "configMapRef" in entry:
                        entry["configMapRef"]["name"] = pattern.sub(r"\1", entry["configMapRef"]["name"])
        key = checks.identity(document)
        require(key not in indexed, f"Duplicate normalized resource {key}")
        indexed[key] = document
    return indexed


def rendered_profiles(adopted):
    source = read(ROOT.parent / "admin/kustomization.yaml")
    require(source.get("components", []).count(COMPONENT) == int(adopted),
            "Native Nova adoption must match the explicitly selected component stage")
    require("../partition-handoff" not in source.get("components", []),
            "Native Nova and legacy partition-handoff reserve the same configuration slots")
    with tempfile.TemporaryDirectory(prefix="arkya-native-nova-") as directory:
        copied = Path(directory) / "gitops"
        shutil.copytree(ROOT.parent, copied)
        admin = copied / "admin/kustomization.yaml"
        baseline = deepcopy(source)
        baseline["components"] = [value for value in baseline.get("components", []) if value != COMPONENT]
        admin.write_text(yaml.safe_dump(baseline, sort_keys=False, allow_unicode=True), encoding="utf-8")
        before = canonical(checks.render(admin.parent))
        baseline["components"].append(COMPONENT)
        admin.write_text(yaml.safe_dump(baseline, sort_keys=False, allow_unicode=True), encoding="utf-8")
        after = canonical(checks.render(admin.parent))
    if adopted:
        require(canonical(checks.render(ROOT.parent / "admin")) == after,
                "Active Admin must match the exact native Nova adoption")
    return before, after


def metadata(name, namespace):
    return {"name": name, "namespace": namespace, "annotations": {"argocd.argoproj.io/sync-wave": "-5"}}


def partition_selector():
    return {"matchLabels": {"app.kubernetes.io/managed-by": "arkya-realms", "arkya.online/realm-id": "nova"},
            "matchExpressions": [{"key": "arkya.online/server-id", "operator": "In", "values": SERVERS + NEXT_SERVERS}]}


def peer(namespace, selector):
    return {"namespaceSelector": {"matchLabels": {"kubernetes.io/metadata.name": namespace}}, "podSelector": selector}


def expected_resources():
    resources = []
    for index, server in enumerate(SERVERS + NEXT_SERVERS):
        name = f"arkya-nova-partition-{index + 1:02}" if index < 4 else f"arkya-nova-v5-partition-{index - 3:02}"
        resources.append({"apiVersion": "v1", "kind": "Service",
            "metadata": metadata(name, REALM_NAMESPACE),
            "spec": {"type": "ClusterIP", "selector": {"app.kubernetes.io/managed-by": "arkya-realms",
                "arkya.online/realm-id": "nova", "arkya.online/server-id": server},
                "ports": [{"name": "game", "port": 17943, "targetPort": 17943, "protocol": "TCP"}]}})
    resources.append({"apiVersion": "networking.k8s.io/v1", "kind": "NetworkPolicy",
        "metadata": metadata("arkya-nova-partitions-game", REALM_NAMESPACE),
        "spec": {"podSelector": partition_selector(), "policyTypes": ["Ingress"], "ingress": [{
            "from": [peer(API_NAMESPACE, {"matchLabels": {"app.kubernetes.io/name": "arkya-gateway"}})],
            "ports": [{"port": 17943, "protocol": "TCP"}]}]}})
    for app, name in (("characters", "arkya-characters-nova-admission"), ("realms", "arkya-realms-nova-commands")):
        resources.append({"apiVersion": "networking.k8s.io/v1", "kind": "NetworkPolicy",
            "metadata": metadata(name, API_NAMESPACE),
            "spec": {"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-" + app}},
                "policyTypes": ["Ingress"], "ingress": [{"from": [peer(REALM_NAMESPACE, partition_selector())],
                    "ports": [{"port": 8080, "protocol": "TCP"}]}]}})
    return checks.index(resources)


def expected_configs():
    gateway = {f"Gateway__RealmGamePartitions__{index}__{field}": value
        for index, server in enumerate(SERVERS + NEXT_SERVERS) for field, value in {
            "RealmId": "nova", "ServerId": server,
            "Address": (f"http://arkya-nova-partition-{index + 1:02}" if index < 4 else
                        f"http://arkya-nova-v5-partition-{index - 3:02}") + f".{REALM_NAMESPACE}.svc.cluster.local:17943/"}.items()}
    profile = {"Id": "nova-native", "DisplayName": "Nova · Starter Island · 4 partitions (native)", "Image": IMAGE,
        "ImagePullSecretName": "arkya-ghcr", "TopologyTemplateServerId": "island",
        "TopologyPublicEndpointTemplate": "wss://api.arkya.online/game/{realmId}/{serverId}/",
        "Servers__0__Id": "island", "Servers__0__Port": "17943", "Servers__0__Transport": "WebSocket",
        "Servers__0__PublicEndpoint": "wss://api.arkya.online/game/nova/island/",
        "Servers__0__ProtocolVersion": "3", "Servers__0__BuildId": "arkya-multi-area-v1"}
    next_profile = dict(profile, Id="nova-native-v5", DisplayName="Nova · Starter Island · 4 partitions · 0.1.49",
        Image=NEXT_IMAGE, TopologyServerIdPrefix="v5", Servers__0__ProtocolVersion="5",
        Servers__0__BuildId="arkya-town-npcs-v1")
    release = {"RealmId": "nova", "ProfileId": "nova-native-v5", "Version": "1", "Automatic": "false",
        "PreviousProfileSha256": PREVIOUS_PROFILE_SHA256, "GameVersion": GAME_VERSION,
        "SourceRevision": SOURCE_REVISION, "SourceRunId": SOURCE_RUN_ID,
        "Topology__Islands__0__IslandId": "starter-island",
        "Topology__Islands__0__DedicatedCount": "4", "Topology__Islands__0__GridColumns": "2",
        "Topology__Islands__0__GridRows": "2", "Topology__Islands__0__Width": "1536", "Topology__Islands__0__Depth": "1536",
        "Topology__Islands__0__OriginX": "0", "Topology__Islands__0__OriginZ": "0",
        "Topology__InstanceDedicatedCount": "0", "Topology__RaidDedicatedCount": "0"}
    realms = {"Deployment__Profiles__1__" + key: value for key, value in profile.items()}
    realms.update({"Deployment__Profiles__2__" + key: value for key, value in next_profile.items()})
    realms.update({"Deployment__RuntimeReleases__0__" + key: value for key, value in release.items()})
    return {"gateway": gateway, "realms": realms}


def check(adopted=False):
    require(read(ROOT / "kustomization.yaml") == {
        "apiVersion": "kustomize.config.k8s.io/v1alpha1", "kind": "Component",
        "resources": ["services.yaml", "network-policies.yaml"],
        "patches": [{"path": "gateway-config.yaml"}, {"path": "realms-config.yaml"}]},
        "Native Nova may own only eight private aliases, three policies and two configuration patches")
    before, after = rendered_profiles(adopted)
    expected = deepcopy(before)
    for app, data in expected_configs().items():
        patch = {"apiVersion": "v1", "kind": "ConfigMap", "metadata": {
            "name": "arkya-" + app + "-config", "namespace": API_NAMESPACE}, "data": data}
        require(read(ROOT / f"{app}-config.yaml") == patch,
                f"Native Nova {app} patch must match its exact routes, immutable protocol 3 profile and approved manual protocol 5 release")
        config = expected[("ConfigMap", API_NAMESPACE, f"arkya-{app}-config")]["data"]
        require(not set(config).intersection(data), "Native Nova must not overwrite any existing configuration slot")
        config.update(data)
    additions = [document for name in ("services.yaml", "network-policies.yaml")
                 for document in yaml.safe_load_all((ROOT / name).read_text(encoding="utf-8")) if document]
    wanted_resources = expected_resources()
    require(checks.index(additions) == wanted_resources,
            "Native Nova resources must be exactly eight private Services and three finite realm/server policies")
    require(not set(expected).intersection(wanted_resources), "Native Nova must not replace any existing resource")
    expected.update(wanted_resources)
    require(after == expected,
            "Native Nova adoption must preserve every legacy resource, profile, image release, credential and workload")
    print(f"Native Nova: {len(after)} resources; preserved protocol 3 profile, manual protocol 5 release, eight WSS routes, eleven scoped additions, "
          f"unchanged legacy contracts and {'adopted configuration' if adopted else 'inactive preparation'} validated offline.")
    return before, after


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adopted", action="store_true", help="Require the component in Admin; does not deploy or alter Nova")
    check(parser.parse_args().adopted)

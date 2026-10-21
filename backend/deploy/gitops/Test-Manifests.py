"""Render real Kustomize overlays and verify the beta's deployment contracts.

Requires kubectl (or KUBECTL) and PyYAML. Never contacts a Kubernetes cluster.
"""

import argparse
import importlib.util
from copy import deepcopy
import ipaddress
import os
from pathlib import Path
import re
import subprocess
from urllib.parse import urlparse

import yaml


ROOT = Path(__file__).resolve().parent
IDENTITY = "arkya-identity"
FRONTEND_NAMESPACE = "arkya-beta"
OPERATIONS = "arkya-operations"
GAME = "arkya-realm-beta-web"
NAMESPACES = {IDENTITY, OPERATIONS}
APPS = {"identity", "gateway", "characters", "content", "admin", "realms", "backend", "frontend"}
PUBLIC_APPS = {"frontend"}
TLS_APPS = {"identity", "characters", "content", "realms"}
IMAGE_PREFIX = "ghcr.io/hantse/arkya-services-"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def render(path):
    result = subprocess.run(
        [os.environ.get("KUBECTL", "kubectl"), "kustomize", str(path)],
        check=True, capture_output=True, text=True, encoding="utf-8",
    )
    return [document for document in yaml.safe_load_all(result.stdout) if document]


def identity(document):
    metadata = document["metadata"]
    return document["kind"], metadata.get("namespace", ""), metadata["name"]


def sync_wave(document):
    return int(document["metadata"].get("annotations", {}).get("argocd.argoproj.io/sync-wave", "0"))


def index(documents):
    indexed = {}
    for document in documents:
        key = identity(document)
        require(key not in indexed, f"Duplicate resource {key}")
        indexed[key] = document
    return indexed


def namespace(app):
    return IDENTITY if app in {"identity", "gateway", "characters", "frontend"} else OPERATIONS


def peer_app(peer):
    ns = peer.get("namespaceSelector", {}).get("matchLabels", {}).get("kubernetes.io/metadata.name")
    app = peer.get("podSelector", {}).get("matchLabels", {}).get("app.kubernetes.io/name")
    require(ns and app, "Network peers must bind both an exact namespace and pod label")
    return ns, app


def check_services(documents, require_digests):
    resources = index(documents)
    require(not any(d["kind"] == "Secret" for d in documents), "Secrets must be provisioned outside Git")
    require({d["metadata"]["name"] for d in documents if d["kind"] == "Namespace"} == NAMESPACES,
            "Services must own only the two service namespaces")
    deployments = {d["metadata"]["name"]: d for d in documents if d["kind"] == "Deployment"}
    require(set(deployments) == {"arkya-" + app for app in APPS}, "Every service must have one Deployment")
    require(len([d for d in documents if d["kind"] == "Service"]) == len(APPS), "Service coverage is incomplete")
    configs = {}
    for app in sorted(APPS):
        name, ns = "arkya-" + app, namespace(app)
        deployment = deployments[name]
        require(deployment["metadata"]["namespace"] == ns, f"Wrong namespace for {name}")
        pod = deployment["spec"]["template"]["spec"]
        require(pod["automountServiceAccountToken"] is False, f"{name} must not control Kubernetes")
        require(pod["securityContext"]["runAsNonRoot"] is True, f"{name} must run without root")
        container, = pod["containers"]
        require(container["securityContext"]["readOnlyRootFilesystem"] is True, f"Writable root for {name}")
        require(container["securityContext"]["allowPrivilegeEscalation"] is False, f"Escalation enabled for {name}")
        require(container["securityContext"]["capabilities"]["drop"] == ["ALL"], f"Unnecessary capabilities in {name}")
        require(container.get("startupProbe") and container.get("readinessProbe") and container.get("livenessProbe"),
                f"{name} must have startup/readiness/liveness probes")
        image = container["image"]
        image_pattern = re.escape(IMAGE_PREFIX + app)
        suffix = r"@sha256:[0-9a-f]{64}" if require_digests else r"(?:@sha256:[0-9a-f]{64}|:sha-[0-9a-f]{40})"
        require(re.fullmatch(image_pattern + suffix, image), f"Unpinned or missing release image for {name}: {image}")
        config_name, = [entry["configMapRef"]["name"] for entry in container["envFrom"] if "configMapRef" in entry]
        require(config_name.startswith(name + "-config-"), f"Config changes must roll {name}'s pod")
        config = resources[("ConfigMap", ns, config_name)]["data"]
        configs[app] = config
        if app != "frontend":
            require(config["ASPNETCORE_ENVIRONMENT"] == "Production", f"Development auth enabled in {name}")
        if app not in {"gateway", "frontend", "backend"}:
            require({"secretRef": {"name": name + "-secrets"}} in container["envFrom"], f"Missing secret for {name}")
            require("Mongo__ConnectionString" not in config, f"Mongo credentials embedded in {name}'s ConfigMap")
        service = resources[("Service", ns, name)]
        if app == "gateway":
            require(service["spec"].get("type") == "LoadBalancer" and service["spec"].get("loadBalancerIP") == "192.168.100.218",
                    "The existing gateway LoadBalancer address must be preserved")
            require(service["spec"].get("externalTrafficPolicy") == "Local", "Gateway must preserve the external proxy's source")
        else:
            require(service["spec"].get("type") == "ClusterIP", f"{name} exposes a service outside ingress")
        require(service["spec"]["selector"] == deployment["spec"]["selector"]["matchLabels"], f"Broken selector for {name}")
        require(all(deployment["spec"]["template"]["metadata"]["labels"].get(k) == v
                    for k, v in service["spec"]["selector"].items()), f"Service misses {name}'s pods")
        ports = {port["name"]: port["containerPort"] for port in container["ports"]}
        for port in service["spec"]["ports"]:
            require(port["targetPort"] in ports, f"Unbound service port in {name}")
            expected_port = 80 if app == "gateway" else ports[port["targetPort"]]
            require(port["port"] == expected_port, f"Wrong service destination in {name}")
            require(port["targetPort"] != "management", f"Management port exposed by {name}'s Service")
        policy = resources[("NetworkPolicy", ns, name)]["spec"]
        require(policy["podSelector"]["matchLabels"] == service["spec"]["selector"], f"Policy misses {name}'s pods")
        for rule in policy["ingress"]:
            if app == "gateway":
                require(rule["from"] == [{"ipBlock": {"cidr": configs["gateway"]["ProxyTrust__KnownNetworks__0"]}}],
                        "Gateway ingress and forwarded-header trust must use the same external proxy network")
                require(rule["ports"] == [{"protocol": "TCP", "port": 8080}], "Gateway may only accept its public HTTP port")
                continue
            peers = {peer_app(peer) for peer in rule["from"]}
            if app in PUBLIC_APPS:
                require(peers == {("kube-system", "traefik")}, f"{name} bypasses the approved ingress")
            if app in TLS_APPS:
                port, = rule["ports"]
                if port["port"] == 8080:
                    require(peers == {(IDENTITY, "arkya-gateway")}, f"{name} accepts unauthenticated HTTP peers")
                else:
                    require(port["port"] == 8443, f"Unexpected internal port for {name}")
                    require(not peers.intersection({("kube-system", "traefik")}), f"Internal {name} routes exposed publicly")
        for key, value in config.items():
            if "KnownNetworks__" in key:
                require(ipaddress.ip_network(value).prefixlen > 0, f"Universal forwarded-header trust for {name}")
            require(not any(secret in key for secret in ("ServiceKey", "Password", "ClientSecret", "AdminKey")) and not key.endswith("__Key"),
                    f"Secret configuration {key} belongs in a Secret")
        if app in TLS_APPS:
            require(config["Kestrel__Endpoints__Internal__Url"] == "https://0.0.0.0:8443", f"Internal TLS absent for {name}")
            require(any(v.get("secret", {}).get("secretName") == name + "-tls" for v in pod["volumes"]),
                    f"TLS keypair missing for {name}")
        if app in {"characters", "admin"}:
            require(config["SSL_CERT_FILE"] == "/etc/arkya/internal-ca/ca.crt", f"Private CA trust missing in {name}")
        if app == "admin":
            require(not config.get("RealmsApi__CertificateAuthorityFile"),
                    "Admin must use SSL_CERT_FILE's full CA bundle: its Realms custom CA override loads only the first certificate")
    require(resources[("NetworkPolicy", OPERATIONS, "arkya-backend")]["spec"]["ingress"] == [],
            "Historical development API must remain isolated")
    require(configs["realms"]["Deployment__Enabled"] == "false", "Realms and Argo must not manage the same workload")
    require(configs["realms"]["GameAccess__StandaloneEntries__0__Enabled"] in {"false", "true"}, "Missing admission activation switch")
    require(configs["realms"]["GameAccess__StandaloneEntries__0__RealmId"] == configs["characters"]["GameAccess__DedicatedServers__0__RealmId"] == "beta-web", "Realm identity mismatch")
    require(configs["realms"]["GameAccess__StandaloneEntries__0__ServerId"] == configs["characters"]["GameAccess__DedicatedServers__0__ServerId"] == "island", "Dedicated identity mismatch")
    require(configs["realms"]["GameAccess__StandaloneEntries__0__BuildId"] == "arkya-web-beta-v1", "Game build mismatch")
    require(configs["realms"]["GameAccess__StandaloneEntries__0__ProtocolVersion"] == "1", "Protocol mismatch")
    require(configs["gateway"]["Gateway__CharactersEnabled"] == "true" and configs["gateway"]["Gateway__AdminEnabled"] == "true", "Gateway misses services")
    for app, key in [("characters", "Identity__BaseUrl"), ("characters", "Realms__BaseUrl"),
                     ("admin", "ContentApi__BaseUrl"), ("admin", "RealmsApi__BaseUrl")]:
        url = urlparse(configs[app][key])
        require(url.scheme == "https" and url.port == 8443 and url.path == "/", f"Unsafe internal URL {app}:{key}")
        service_name, service_ns, *_ = url.hostname.split(".")
        require(("Service", service_ns, service_name) in resources, f"Missing internal destination for {app}:{key}")
    for app in {"identity", "admin"}:
        pvc = resources[("PersistentVolumeClaim", namespace(app), "arkya-" + app + "-keys")]
        require("Prune=false" in pvc["metadata"]["annotations"]["argocd.argoproj.io/sync-options"], "Key storage must survive pruning")
        require(deployments["arkya-" + app]["spec"]["strategy"]["type"] == "Recreate", "RWO keys require a single pod during rollout")
        consumers = [deployment for deployment in deployments.values()
                     if deployment["metadata"]["namespace"] == pvc["metadata"]["namespace"] and
                     any(volume.get("persistentVolumeClaim", {}).get("claimName") == pvc["metadata"]["name"]
                         for volume in deployment["spec"]["template"]["spec"].get("volumes", []))]
        require(consumers and sync_wave(pvc) == min(sync_wave(consumer) for consumer in consumers),
                f"{pvc['metadata']['name']} must share its first consumer's sync wave: WaitForFirstConsumer binding otherwise deadlocks Argo")
    for ingress in (d for d in documents if d["kind"] == "Ingress"):
        require(ingress["metadata"]["namespace"] == IDENTITY, "Public route outside the gateway namespace")
        require(ingress["spec"]["ingressClassName"] == "traefik" and ingress["spec"].get("tls"), "Public ingress requires TLS")
        require(ingress["metadata"]["annotations"]["traefik.ingress.kubernetes.io/router.observability.accesslogs"] == "false", "OAuth callback access logging must be disabled")
        for rule in ingress["spec"]["rules"]:
            require(rule["host"] in ingress["spec"]["tls"][0]["hosts"], "TLS host mismatch")
            for path in rule["http"]["paths"]:
                require(path["backend"]["service"]["name"] in {"arkya-gateway", "arkya-frontend"}, "A private API is routed publicly")
    return configs


def check_frontend_discord(documents, beta_documents):
    resources, beta = index(documents), index(beta_documents)
    expected_keys = {
        ("PersistentVolumeClaim", FRONTEND_NAMESPACE, "arkya-identity-keys"),
    }
    require(all(d["kind"] != "Namespace" and d["metadata"].get("namespace") == FRONTEND_NAMESPACE
                for d in documents),
            "Frontend/Discord must render only namespaced resources; namespace creation belongs to Argo")

    def relocated(kind, name):
        expected = deepcopy(beta[(kind, IDENTITY, name)])
        expected["metadata"]["namespace"] = FRONTEND_NAMESPACE
        return expected

    require(resources[("PersistentVolumeClaim", FRONTEND_NAMESPACE, "arkya-identity-keys")] ==
            relocated("PersistentVolumeClaim", "arkya-identity-keys"),
            "Frontend/Discord must preserve Identity key storage protection in the beta namespace")
    load_balancers = [d for d in documents if d["kind"] == "Service"
                     and d["spec"].get("type") == "LoadBalancer"]
    require(len(load_balancers) == 1 and load_balancers[0]["metadata"]["name"] == "arkya-gateway"
            and load_balancers[0]["spec"].get("loadBalancerIP") == "192.168.100.218",
            "Frontend/Discord must expose only YARP through the existing LoadBalancer")
    for app in ("identity", "gateway", "frontend"):
        name = "arkya-" + app
        key = ("Deployment", FRONTEND_NAMESPACE, name)
        deployment, original = resources[key], beta[("Deployment", IDENTITY, name)]
        container, = deployment["spec"]["template"]["spec"]["containers"]
        original_container, = original["spec"]["template"]["spec"]["containers"]
        config_name, = [e["configMapRef"]["name"] for e in container["envFrom"] if "configMapRef" in e]
        original_config_name, = [e["configMapRef"]["name"] for e in original_container["envFrom"] if "configMapRef" in e]
        require(re.fullmatch(re.escape(name + "-config-") + r"[a-z0-9]{10}", config_name),
                f"Frontend/Discord requires a generated ConfigMap hash for {name}")
        config_key = ("ConfigMap", FRONTEND_NAMESPACE, config_name)
        expected_keys.add(config_key)
        expected = relocated("ConfigMap", original_config_name)
        expected["metadata"]["name"] = config_name
        if app == "identity":
            expected["data"]["AllowedHosts"] = (
                f"api.arkya.online;arkya-identity;arkya-identity.{FRONTEND_NAMESPACE}.svc.cluster.local")
            for suffix in ("Url", "Certificate__Path", "Certificate__KeyPath"):
                del expected["data"]["Kestrel__Endpoints__Internal__" + suffix]
            require(not any(key.startswith("Kestrel__Endpoints__Internal__")
                            for key in resources[config_key]["data"]),
                    "Frontend/Discord terminates TLS at Traefik; Identity must not require an internal certificate")
        if app == "gateway":
            expected["data"].update(AllowedHosts="api.arkya.online;arkya.online",
                                    Gateway__CharactersEnabled="false", Gateway__AdminEnabled="false",
                                    Gateway__FrontendEnabled="true", Gateway__FrontendHost="arkya.online",
                                    Gateway__FrontendAddress=f"http://arkya-frontend.{FRONTEND_NAMESPACE}.svc.cluster.local:8080/",
                                    Gateway__AdminAddress=f"http://arkya-admin.{FRONTEND_NAMESPACE}.svc.cluster.local:8080/")
            for destination in ("identity", "characters"):
                expected["data"][f"ReverseProxy__Clusters__{destination}__Destinations__primary__Address"] = (
                    f"http://arkya-{destination}.{FRONTEND_NAMESPACE}.svc.cluster.local:8080/")
        require(resources[config_key] == expected, f"Unexpected frontend/Discord configuration for {name}")
        require((config_name != original_config_name) == (app in {"identity", "gateway"}),
                f"Frontend/Discord must roll the changed Identity and gateway configurations: {name}")
        expected = relocated("Deployment", name)
        if app == "gateway":
            expected["metadata"].setdefault("annotations", {})["argocd.argoproj.io/sync-wave"] = "-1"
        expected_container, = expected["spec"]["template"]["spec"]["containers"]
        for entry in expected_container["envFrom"]:
            if "configMapRef" in entry:
                entry["configMapRef"]["name"] = config_name
        if app == "identity":
            expected_container["ports"] = [p for p in expected_container["ports"] if p["name"] != "https"]
            expected_container["volumeMounts"] = [v for v in expected_container["volumeMounts"] if v["name"] != "tls"]
            pod = expected["spec"]["template"]["spec"]
            pod["volumes"] = [v for v in pod["volumes"] if v["name"] != "tls"]
        if app == "frontend":
            for probe in ("startupProbe", "readinessProbe", "livenessProbe"):
                expected_container[probe]["httpGet"]["httpHeaders"] = [{
                    "name": "Host", "value": f"arkya-frontend.{FRONTEND_NAMESPACE}.svc.cluster.local",
                }]
        require(deployment == expected,
                f"Frontend/Discord must preserve {name}'s image, security and storage with beta DNS and config references")
        for kind in ("Deployment", "Service", "NetworkPolicy"):
            expected_keys.add((kind, FRONTEND_NAMESPACE, name))
        service_key = ("Service", FRONTEND_NAMESPACE, name)
        expected = relocated("Service", name)
        if app == "identity":
            expected["spec"]["ports"] = [p for p in expected["spec"]["ports"] if p["name"] != "https"]
        if app == "frontend":
            expected["metadata"]["annotations"]["argocd.argoproj.io/sync-wave"] = "0"
            require(resources[service_key]["spec"]["type"] == "ClusterIP"
                    and resources[service_key]["spec"]["ports"] == [
                        {"name": "http", "port": 8080, "targetPort": "http"}]
                    and {"name": "http", "containerPort": 8080} in container["ports"],
                    "YARP must reach the internal frontend over HTTP on port 8080")
        require(resources[service_key] == expected, f"Unexpected frontend/Discord Service for {name}")
        policy_key = ("NetworkPolicy", FRONTEND_NAMESPACE, name)
        expected = relocated("NetworkPolicy", name)
        if app == "frontend":
            expected["metadata"]["annotations"]["argocd.argoproj.io/sync-wave"] = "0"
        if app in {"identity", "frontend"}:
            expected["spec"]["ingress"] = [{
                "from": [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-gateway"}}}],
                "ports": [{"protocol": "TCP", "port": 8080}],
            }]
        require(resources[policy_key] == expected, f"Unexpected frontend/Discord network policy for {name}")
    require(len(resources) == 13 and set(resources) == expected_keys,
            "Frontend/Discord must contain only 3 workloads/configs/services/policies and Identity PVC in arkya-beta")


def check_admin(documents, frontend_documents, beta_documents, require_digests=False):
    if any(item["kind"] == "Deployment" and item["metadata"]["name"] == "arkya-characters" for item in documents):
        validator_spec = importlib.util.spec_from_file_location("arkya_player_management", ROOT / "admin" / "Validate-PlayerManagement.py")
        validator = importlib.util.module_from_spec(validator_spec)
        validator_spec.loader.exec_module(validator)
        return validator.check_profile(documents, frontend_documents, beta_documents, require_digests)
    resources, frontend, beta = index(documents), index(frontend_documents), index(beta_documents)
    apps = {"identity", "gateway", "frontend", "admin", "content", "realms"}
    require(len(resources) == 26 and
            {d["kind"] for d in documents} == {
                "Deployment", "ConfigMap", "Service", "NetworkPolicy", "PersistentVolumeClaim"},
            "Admin must render only six workloads/configs/services/policies and two key PVCs")
    require(all(d["metadata"].get("namespace") == FRONTEND_NAMESPACE for d in documents),
            "Every Admin profile resource must remain in arkya-beta")
    require({d["metadata"]["name"] for d in documents if d["kind"] == "Deployment"} ==
            {"arkya-" + app for app in apps},
            "Admin must not deploy Characters, the legacy backend or Unity")
    configs = {}
    config_names = {}
    for app in sorted(apps):
        name = "arkya-" + app
        deployment = resources[("Deployment", FRONTEND_NAMESPACE, name)]
        pod = deployment["spec"]["template"]["spec"]
        container, = pod["containers"]
        original = beta[("Deployment", namespace(app), name)]
        original_pod = original["spec"]["template"]["spec"]
        original_container, = original_pod["containers"]
        require(container["image"] == original_container["image"],
                f"Admin must inherit the CI-promoted image for {name}")
        require(pod["automountServiceAccountToken"] is False and "serviceAccountName" not in pod,
                f"{name} must not acquire Kubernetes credentials")
        require(pod["securityContext"] == original_pod["securityContext"] and
                container["securityContext"] == original_container["securityContext"] and
                container["resources"] == original_container["resources"],
                f"Admin must preserve {name}'s security and resource limits")
        expected_secrets = [] if app in {"frontend", "gateway"} else [name + "-secrets"]
        require([e["secretRef"]["name"] for e in container["envFrom"] if "secretRef" in e] == expected_secrets,
                f"Incorrect application secret reference for {name}")
        require(not any("secret" in v for v in pod["volumes"]),
                f"{name} must not require an internal TLS certificate or CA secret")
        require(not any(p["containerPort"] == 8443 for p in container["ports"]),
                f"{name} must not expose a TLS listener")
        config_name, = [e["configMapRef"]["name"] for e in container["envFrom"] if "configMapRef" in e]
        require(re.fullmatch(re.escape(name + "-config-") + r"[a-z0-9]{10}", config_name),
                f"{name} must roll when its generated config changes")
        config_names[app] = config_name
        config = resources[("ConfigMap", FRONTEND_NAMESPACE, config_name)]["data"]
        configs[app] = config
        require(not any("Certificate" in key or key == "SSL_CERT_FILE" for key in config),
                f"TLS terminates at Traefik; {name} must not need certificate settings")
        for key in config:
            require(not any(secret in key for secret in (
                "ConnectionString", "ServiceKey", "Password", "ClientSecret", "AdminKey")) and
                not key.endswith("__Key"), f"{name} config must not contain credentials")
        service = resources[("Service", FRONTEND_NAMESPACE, name)]
        require(service["spec"]["selector"] == deployment["spec"]["selector"]["matchLabels"] and
                all(pod_value == deployment["spec"]["template"]["metadata"]["labels"].get(label)
                    for label, pod_value in service["spec"]["selector"].items()),
                f"{name} Service must select its deployment")
        require(service["spec"]["ports"] == [{
            "name": "http", "port": 80 if app == "gateway" else 8080, "targetPort": "http",
        }], f"Unexpected public or management service port in {name}")
        require(service["spec"]["type"] == ("LoadBalancer" if app == "gateway" else "ClusterIP"),
                "Only YARP may expose the application's LoadBalancer")
        policy = resources[("NetworkPolicy", FRONTEND_NAMESPACE, name)]["spec"]
        require(policy["podSelector"]["matchLabels"] == service["spec"]["selector"] and
                policy["policyTypes"] == ["Ingress"], f"NetworkPolicy must select {name}")
        if app != "gateway":
            peer = "arkya-admin" if app in {"content", "realms"} else "arkya-gateway"
            require(policy["ingress"] == [{
                "from": [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": peer}}}],
                "ports": [{"protocol": "TCP", "port": 8080}],
            }], f"{name} must allow only {peer} in the same namespace")
        if app in {"content", "realms"}:
            require(config["ASPNETCORE_HTTP_PORTS"] == "8080" and
                    {v for k, v in config.items() if k.startswith("InternalHttp__AllowedNetworks__")} ==
                    {"10.42.0.0/16", "192.168.100.0/24"},
                    f"{name} must accept private pod and kubelet peers explicitly")
            require(not any(k.startswith("Kestrel__Endpoints__") for k in config),
                    f"{name} must not retain an inherited TLS endpoint")
            require(not any(k.startswith("ProxyTrust__") for k in config),
                    f"{name} must validate its direct private peer without trusting forwarded headers")
            internal_host = f"{name}.{FRONTEND_NAMESPACE}.svc.cluster.local"
            require(config["AllowedHosts"] == f"{name};{internal_host}", f"Incorrect host validation for {name}")
            for probe in ("startupProbe", "readinessProbe", "livenessProbe"):
                health = container[probe]["httpGet"]
                expected_path = "/health" if app == "content" else (
                    "/health/live" if probe == "livenessProbe" else "/health/ready")
                require(health == {
                    "path": expected_path, "port": "http", "scheme": "HTTP",
                    "httpHeaders": [{"name": "Host", "value": internal_host}],
                }, f"{name} must expose usable private HTTP health probes")
    # Switching profiles must leave the running frontend and Discord resources unchanged.
    for key, document in frontend.items():
        if key[0] == "ConfigMap" and key[2].startswith("arkya-gateway-config-"):
            expected = deepcopy(document)
            expected["metadata"]["name"] = config_names["gateway"]
            expected["data"].update(
                AllowedHosts="api.arkya.online;arkya.online;admin.arkya.online",
                Gateway__AdminEnabled="true")
            require(resources[identity(expected)] == expected, "Admin must only enable its host in YARP")
        elif key == ("Deployment", FRONTEND_NAMESPACE, "arkya-gateway"):
            expected = deepcopy(document)
            expected["spec"]["template"]["spec"]["containers"][0]["envFrom"] = [
                {"configMapRef": {"name": config_names["gateway"]}}]
            require(resources[key] == expected, "YARP may only roll its updated configuration")
        else:
            require(resources[key] == document, f"Admin must preserve the working frontend/Discord resource: {key}")
    for app in ("identity", "admin"):
        name = "arkya-" + app
        key = ("PersistentVolumeClaim", FRONTEND_NAMESPACE, name + "-keys")
        expected = deepcopy(beta[("PersistentVolumeClaim", namespace(app), name + "-keys")])
        expected["metadata"]["namespace"] = FRONTEND_NAMESPACE
        require(resources[key] == expected, f"{name} key PVC must survive prune and application deletion")
        deployment = resources[("Deployment", FRONTEND_NAMESPACE, name)]
        require(deployment["spec"]["replicas"] == 1 and deployment["spec"]["strategy"]["type"] == "Recreate",
                f"{name} must keep one owner of its RWO key volume")
        require(sync_wave(resources[key]) == sync_wave(deployment),
                f"{name} key PVC must bind in its consumer's sync wave")
        require({"name": "keys", "persistentVolumeClaim": {"claimName": name + "-keys"}} in
                deployment["spec"]["template"]["spec"]["volumes"], f"{name} must persist its keys")
    require(configs["admin"]["AllowedHosts"] ==
            "admin.arkya.online;arkya-admin;arkya-admin.arkya-beta.svc.cluster.local",
            "Admin must accept its public host and private service name")
    require(configs["admin"]["Admin__DiscordEnabled"] == "true" and
            {k: v for k, v in configs["admin"].items() if k.startswith("Admin__AllowedDiscordUserIds__")} ==
            {"Admin__AllowedDiscordUserIds__0": "195861708248449024"} and
            configs["admin"]["Discord__PublicBaseUrl"] == "https://admin.arkya.online",
            "Admin must require Discord login and authorize only the confirmed operator's immutable Discord ID")
    require("Admin__Password" not in configs["admin"] and "Discord__ClientId" not in configs["admin"] and
            "Discord__ClientSecret" not in configs["admin"],
            "Admin Discord credentials must come from its application Secret, with no password login configuration")
    require(configs["gateway"]["Gateway__AdminHost"] == "admin.arkya.online" and
            configs["gateway"]["Gateway__AdminAddress"] ==
            f"http://arkya-admin.{FRONTEND_NAMESPACE}.svc.cluster.local:8080/",
            "YARP must route only the Admin host to its private service")
    require(configs["admin"]["ProxyTrust__KnownNetworks__0"] == "10.42.0.0/16" and
            configs["admin"]["ProxyTrust__ForwardLimit"] == "1",
            "Admin must recover the public HTTPS scheme only from the private gateway hop")
    for module in ("Content", "Realms"):
        prefix = module + "Api__"
        require(configs["admin"][prefix + "Enabled"] == "true" and
                configs["admin"][prefix + "AllowClusterHttp"] == "true" and
                configs["admin"][prefix + "BaseUrl"] ==
                f"http://arkya-{module.lower()}.{FRONTEND_NAMESPACE}.svc.cluster.local:8080/",
                f"Admin must activate the private {module} module")
    require(configs["realms"]["Deployment__Enabled"] == "false" and
            configs["realms"]["GameAccess__StandaloneEntries__0__Enabled"] == "false",
            "Admin services must not activate an undeployed dedicated or a Kubernetes deployment controller")
    require(not any(k.startswith(("Deployment__Targets__", "Deployment__Profiles__")) for k in configs["realms"]),
            "Dedicated deployment profiles belong to their later deployment step")


def check_direct_application(application, workload_documents, project, profile="frontend-discord"):
    require(identity(application) == ("Application", "argocd", "arkya-services"),
            "The direct profile must use the existing arkya-services application")
    spec = application["spec"]
    require(spec["project"] == project["metadata"]["name"] == "arkya-beta",
            "The direct application must use the workload project")
    require("sources" not in spec and spec["source"] == {
        "repoURL": "git@github.com:Hantse/arkya-services.git",
        "path": f"backend/deploy/gitops/{profile}", "targetRevision": "master"},
            "The direct application must point at workloads, not the bootstrap")
    require(spec["source"]["repoURL"] in project["spec"]["sourceRepos"],
            "The workload project must allow the direct application's source")
    require(spec["destination"] == {"server": "https://kubernetes.default.svc", "namespace": FRONTEND_NAMESPACE}
            and spec["destination"] in project["spec"]["destinations"],
            "The direct application's destination must be allowed by its project")
    allowed_namespaces = {item["namespace"] for item in project["spec"]["destinations"]
                          if item["server"] == spec["destination"]["server"]}
    expected_namespaces = {FRONTEND_NAMESPACE, "arkya-beta-realm"} if profile == "admin" else {FRONTEND_NAMESPACE}
    require(expected_namespaces.issubset(allowed_namespaces) and
            all(d["metadata"].get("namespace") in expected_namespaces or
                d["kind"] == "Namespace" and d["metadata"]["name"] in expected_namespaces for d in workload_documents),
            "The direct profile may use only its explicit project-approved namespaces")
    require("automated" not in spec["syncPolicy"] and {
        "CreateNamespace=true", "FailOnSharedResource=true"}.issubset(spec["syncPolicy"]["syncOptions"]),
            "The direct application must start manually and detect competing ownership")


def check_bootstrap(documents, bootstrap_project, *, frontend_discord=False):
    resources = index(documents)
    application, = [d for d in documents if d["kind"] == "Application"]
    project, = [d for d in documents if d["kind"] == "AppProject"]
    require(application["metadata"]["namespace"] == project["metadata"]["namespace"] == "argocd",
            "Argo control resources must target the installed argocd namespace")
    require(application["spec"]["project"] == project["metadata"]["name"] == "arkya-beta",
            "The workload application must use the workload project")
    require(application["metadata"]["name"] == "arkya-beta",
            "Deployment profiles must retain one owning application")
    require(sync_wave(project) < sync_wave(application), "The workload project must sync before its application")
    require("automated" not in application["spec"].get("syncPolicy", {}), "Bootstrap must await the operator's first sync")
    sources = {(s["repoURL"], s["path"], s["targetRevision"]) for s in application["spec"]["sources"]}
    expected_sources = {
        ("git@github.com:Hantse/arkya-services.git", "backend/deploy/gitops/beta", "master"),
        ("git@github.com:Hantse/arkya-mmorpg.git", "deploy/gitops/beta", "main"),
    }
    expected_namespaces = NAMESPACES | {GAME, FRONTEND_NAMESPACE, "arkya-beta-realm"}
    destination_namespace = IDENTITY
    if frontend_discord:
        expected_sources = {
            ("git@github.com:Hantse/arkya-services.git", "backend/deploy/gitops/frontend-discord", "master"),
        }
        expected_namespaces = {FRONTEND_NAMESPACE}
        destination_namespace = FRONTEND_NAMESPACE
        require({"FailOnSharedResource=true", "CreateNamespace=true"}.issubset(
            application["spec"]["syncPolicy"]["syncOptions"]),
            "Frontend/Discord must use Argo namespace creation and reject conflicting resource ownership")
    require(sources == expected_sources and len(application["spec"]["sources"]) == len(expected_sources),
            "Argo sources must match the selected deployment profile")
    require(application["spec"]["destination"] == {"server": "https://kubernetes.default.svc", "namespace": destination_namespace},
            "Workloads must target the existing cluster and the selected profile's namespace")
    require({(d["server"], d["namespace"]) for d in project["spec"]["destinations"]} ==
            {("https://kubernetes.default.svc", ns) for ns in expected_namespaces}, "Argo namespace allowlist mismatch")
    require({s[0] for s in sources} == set(project["spec"]["sourceRepos"]), "Argo source allowlist mismatch")
    require(project["spec"]["clusterResourceWhitelist"] == [{"group": "", "kind": "Namespace"}], "Unnecessary cluster-wide Argo permissions")
    require({(item["group"], item["kind"]) for item in project["spec"]["namespaceResourceWhitelist"]} == {
        ("", "ConfigMap"), ("", "Service"), ("", "PersistentVolumeClaim"), ("apps", "Deployment"),
        ("networking.k8s.io", "NetworkPolicy"), ("networking.k8s.io", "Ingress"),
        ("", "ServiceAccount"), ("", "Pod"), ("apps", "ReplicaSet"),
        ("rbac.authorization.k8s.io", "Role"), ("rbac.authorization.k8s.io", "RoleBinding")},
            "The workload project must allow declared workloads and their observable Pod/ReplicaSet children")
    require(len(resources) == 2, "Unexpected bootstrap resources")
    require(identity(bootstrap_project) == ("AppProject", "argocd", "arkya-bootstrap"),
            "The parent needs a distinct administrative bootstrap project")
    administrative = bootstrap_project["spec"]
    require(set(administrative["sourceRepos"]) == {
        "https://github.com/Hantse/arkya-services.git", "git@github.com:Hantse/arkya-services.git"},
            "Bootstrap must be limited to the owning services repository")
    require(administrative["destinations"] == [{"server": "https://kubernetes.default.svc", "namespace": "argocd"}],
            "Bootstrap must target only the Argo control namespace")
    require(administrative["clusterResourceWhitelist"] == [], "Bootstrap must not create cluster resources")
    require({(item["group"], item["kind"]) for item in administrative["namespaceResourceWhitelist"]} == {
        ("argoproj.io", "AppProject"), ("argoproj.io", "Application")},
            "Bootstrap must administer only Argo projects and applications")


def check_unity(service_documents, unity_path, configs, require_digests):
    unity_documents = render(unity_path)
    index(service_documents + unity_documents)
    resources = index(unity_documents)
    for name, port in [("arkya-web-client", 8080), ("arkya-web-dedicated", 17943)]:
        service = resources[("Service", GAME, name)]
        require(any(p["port"] == port for p in service["spec"]["ports"]), f"Unity service contract mismatch: {name}")
        deployment = resources[("Deployment", GAME, name)]
        container, = deployment["spec"]["template"]["spec"]["containers"]
        require(service["spec"]["selector"] == deployment["spec"]["selector"]["matchLabels"], f"Unity service selector mismatch: {name}")
        require(container.get("readinessProbe") and container.get("livenessProbe"), f"Unity health probes missing: {name}")
        if require_digests:
            image_name = "ghcr.io/hantse/arkya-mmorpg-" + ("web-client" if name.endswith("client") else "dedicated")
            require(re.fullmatch(re.escape(image_name) + r"@sha256:[0-9a-f]{64}", container["image"]),
                    f"Unpublished Unity release image for {name}")
    dedicated = resources[("Deployment", GAME, "arkya-web-dedicated")]
    require(dedicated["spec"]["strategy"]["type"] == "Recreate", "A realm identity must not have two dedicated writers during rollout")
    game_container, = dedicated["spec"]["template"]["spec"]["containers"]
    game_env = {entry["name"]: entry for entry in game_container["env"]}
    require(game_env["ARKYA_REALM_ID"]["value"] == configs["characters"]["GameAccess__DedicatedServers__0__RealmId"], "Unity/Characters realm mismatch")
    require(game_env["ARKYA_SERVER_ID"]["value"] == configs["characters"]["GameAccess__DedicatedServers__0__ServerId"], "Unity/Characters server mismatch")
    require(game_env["ARKYA_DEDICATED_KEY"]["valueFrom"]["secretKeyRef"] == {"name": "arkya-beta-dedicated", "key": "ARKYA_DEDICATED_KEY"},
            "Dedicated credentials must come from its own Secret")
    characters_url = urlparse(game_env["ARKYA_CHARACTERS_URL"]["value"])
    require(characters_url.scheme == "https" and characters_url.hostname == "arkya-characters.arkya-identity.svc.cluster.local" and characters_url.port == 8443,
            "Dedicated must validate the internal Characters TLS endpoint")
    require(game_env["SSL_CERT_FILE"]["value"] == "/etc/arkya/internal-ca/ca.crt", "Unity private CA configuration missing")
    require(game_container["readinessProbe"]["exec"]["command"] == ["test", "-f", "/tmp/arkya-ready"], "Dedicated readiness must wait for Unity terrain and listener")
    endpoint = urlparse(configs["realms"]["GameAccess__StandaloneEntries__0__Endpoint"])
    require(endpoint.scheme == "wss", "Game endpoint must use WSS")
    ingress_hosts = {r["host"] for d in unity_documents if d["kind"] == "Ingress" for r in d["spec"]["rules"]}
    require(endpoint.hostname in ingress_hosts, "Realms advertises a host absent from the Unity WSS ingress")
    return len(unity_documents)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true", help="Reject bootstrap tags; required before first sync")
    parser.add_argument("--unity-path", type=Path, help="Also render the local Unity deploy/gitops/beta overlay")
    args = parser.parse_args()
    documents = render(ROOT / "beta")
    configs = check_services(documents, args.require_digests)
    bootstrap_project = yaml.safe_load((ROOT / "bootstrap-project.yaml").read_text(encoding="utf-8"))
    check_bootstrap(render(ROOT / "bootstrap"), bootstrap_project)
    frontend_documents = render(ROOT / "frontend-discord")
    check_frontend_discord(frontend_documents, documents)
    check_direct_application(
        yaml.safe_load((ROOT / "frontend-discord.application.example.yaml").read_text(encoding="utf-8")),
        frontend_documents, yaml.safe_load((ROOT / "bootstrap/project.yaml").read_text(encoding="utf-8")))
    check_bootstrap(render(ROOT / "bootstrap-frontend-discord"), bootstrap_project, frontend_discord=True)
    admin_documents = render(ROOT / "admin")
    check_admin(admin_documents, frontend_documents, documents, args.require_digests)
    check_direct_application(
        yaml.safe_load((ROOT / "admin.application.example.yaml").read_text(encoding="utf-8")),
        admin_documents, yaml.safe_load((ROOT / "bootstrap/project.yaml").read_text(encoding="utf-8")),
        profile="admin")
    unity_count = 0
    if args.unity_path:
        unity_count = check_unity(documents, args.unity_path, configs, args.require_digests)
    print(f"Validated {len(documents)} rendered service resources, 8 deployments, TLS/network/storage contracts and the Argo bootstrap.")
    print(f"Validated {len(frontend_documents)} frontend/Discord resources, 3 inherited deployments, the direct Argo application and optional bootstrap.")
    admin_deployments = sum(item["kind"] == "Deployment" for item in admin_documents)
    print(f"Validated {len(admin_documents)} Admin resources, {admin_deployments} deployments, private HTTP modules, existing frontend preservation and direct Argo application.")
    if unity_count:
        print(f"Validated {unity_count} Unity resources and the combined service/admission/WSS contracts.")
    if not args.require_digests:
        print("Image availability is separate: run again with --require-digests after the publication/promotion workflows.")


if __name__ == "__main__":
    main()

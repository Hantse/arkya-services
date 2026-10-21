"""Validate Admin, player APIs and the scoped realm deployer without cluster access."""

from copy import deepcopy
import argparse
import importlib.util
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
NS = "arkya-beta"
REALM_NS = "arkya-beta-realm"
REALM_ADDRESS = f"http://arkya-beta-web-island.{REALM_NS}.svc.cluster.local:17943/"
GAME_ENDPOINT = "wss://api.arkya.online/game/beta-web/"
APPS = {"identity", "gateway", "frontend", "admin", "content", "realms", "characters"}
require = checks.require
control_spec = importlib.util.spec_from_file_location("arkya_realm_control", ROOT.parent / "realm-control" / "Validate-Manifests.py")
control = importlib.util.module_from_spec(control_spec)
control_spec.loader.exec_module(control)


def secret_env(name, secret, key):
    return {"name": name, "valueFrom": {"secretKeyRef": {"name": secret, "key": key}}}


def ingress(*apps, nodes=False):
    peers = [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-" + app}}}
             for app in apps]
    if nodes:
        peers.append({"ipBlock": {"cidr": "192.168.100.0/24"}})
    return [{"from": peers, "ports": [{"protocol": "TCP", "port": 8080}]}]


def without_inference_component(documents, require_digests):
    """Validate the exact additive component before checking the unchanged seven-service profile."""
    component_root = ROOT.parent / "inference-control"
    component = checks.yaml.safe_load((component_root / "kustomization.yaml").read_text(encoding="utf-8"))
    require(component["kind"] == "Component" and "namespace" not in component,
            "Inference must not transform its parent's namespaces")
    resources = checks.index(documents)
    deployment = resources[("Deployment", NS, "arkya-inference")]
    pod = deployment["spec"]["template"]["spec"]
    container, = pod["containers"]
    config_name, = [entry["configMapRef"]["name"] for entry in container["envFrom"]]
    require(re.fullmatch(r"arkya-inference-config-[a-z0-9]{10}", config_name), "Inference configuration must trigger a rollout")
    config = resources[("ConfigMap", NS, config_name)]
    require(config["data"] == dict(line.split("=", 1) for line in
            (component_root / "inference.env").read_text(encoding="utf-8").splitlines() if line and not line.startswith("#")),
            "Unexpected Inference configuration")
    require(config["data"]["Mongo__DatabaseName"] == "arkya_inference" and
            not any(any(part in key for part in ("ConnectionString", "ServiceKey", "Password", "ClientSecret", "AdminKey", "KeySeed"))
                    or key.endswith("__Key") for key in config["data"]), "Inference must preserve its database and keep credentials out of ConfigMaps")
    seed = secret_env("InferenceOperator__KeySeed", "arkya-admin-secrets", "ContentApi__AdminKey")
    require(container["env"] == [secret_env("Mongo__ConnectionString", "arkya-content-secrets", "Mongo__ConnectionString"), seed],
            "Inference must use only the two approved existing Secret keys")
    require(pod["automountServiceAccountToken"] is False and "serviceAccountName" not in pod and
            pod["securityContext"]["runAsNonRoot"] is True and
            container["securityContext"] == {"allowPrivilegeEscalation": False, "readOnlyRootFilesystem": True,
                                               "capabilities": {"drop": ["ALL"]}}, "Inference must retain its restricted process permissions")
    pins = [entry for entry in component["images"] if entry["name"] == "ghcr.io/hantse/arkya-services-inference"]
    pin, = pins
    expected_image = pin["name"] + ("@" + pin["digest"] if "digest" in pin else ":" + pin["newTag"])
    require(container["image"] == expected_image, "Inference must inherit its separately promoted image pin")
    if require_digests:
        require(re.fullmatch(r"ghcr\.io/hantse/arkya-services-inference@sha256:[0-9a-f]{64}", expected_image),
                f"Unpublished image: {expected_image}")
    owned = checks.yaml.safe_load_all((component_root / "inference.yaml").read_text(encoding="utf-8"))
    owned_keys = set()
    for expected in owned:
        key = checks.identity(expected)
        owned_keys.add(key)
        if expected["kind"] == "Deployment":
            expected_container, = expected["spec"]["template"]["spec"]["containers"]
            expected_container["image"] = expected_image
            expected_container["envFrom"][0]["configMapRef"]["name"] = config_name
        require(resources.get(key) == expected, f"Unexpected Inference resource: {key}")
    require(owned_keys == {(kind, NS, "arkya-inference") for kind in ("Deployment", "Service", "NetworkPolicy")},
            "Inference must add exactly one private workload, service and policy")
    require(resources[("Service", NS, "arkya-inference")]["spec"]["type"] == "ClusterIP" and
            resources[("NetworkPolicy", NS, "arkya-inference")]["spec"]["ingress"] == ingress("gateway", "admin"),
            "Inference must accept only private Gateway and Admin traffic")
    owned_keys.add(("ConfigMap", NS, config_name))
    normalized = deepcopy([document for document in documents if checks.identity(document) not in owned_keys])
    admin_container, = next(document for document in normalized if checks.identity(document) ==
                            ("Deployment", NS, "arkya-admin"))["spec"]["template"]["spec"]["containers"]
    require(admin_container.get("env", []).count(seed) == 1, "Admin needs exactly one approved Inference seed reference")
    admin_container["env"].remove(seed)
    destination = f"http://arkya-inference.{NS}.svc.cluster.local:8080/"
    expected_modules = {
        "arkya-admin": {"InferenceApi__Enabled": "true", "InferenceApi__AllowClusterHttp": "true", "InferenceApi__BaseUrl": destination},
        "arkya-gateway": {"Gateway__InferenceEnabled": "true", "ReverseProxy__Clusters__inference__Destinations__primary__Address": destination}}
    for app, expected_values in expected_modules.items():
        config, = [document["data"] for document in normalized if document["kind"] == "ConfigMap" and
                   document["metadata"]["name"].startswith(app + "-config-")]
        require(all(config.get(key) == value for key, value in expected_values.items()), f"Invalid {app} Inference activation")
        for key in expected_values:
            del config[key]
    return normalized


def without_reports_component(documents, require_digests):
    """Check Reports and remove only its declared additions from the parent profile."""
    component_root = ROOT.parent / "reports-control"
    component = checks.yaml.safe_load((component_root / "kustomization.yaml").read_text(encoding="utf-8"))
    require(component["kind"] == "Component" and "namespace" not in component and
            component["resources"] == ["reports.yaml"] and "secretGenerator" not in component,
            "Reports must remain additive without namespace or Secret generation")
    resources = checks.index(documents)
    deployment = resources[("Deployment", NS, "arkya-reports")]
    pod = deployment["spec"]["template"]["spec"]
    container, = pod["containers"]
    config_name, = [entry["configMapRef"]["name"] for entry in container["envFrom"]]
    require(re.fullmatch(r"arkya-reports-config-[a-z0-9]{10}", config_name), "Reports configuration must trigger a rollout")
    config = resources[("ConfigMap", NS, config_name)]
    require(config["data"] == dict(line.split("=", 1) for line in
            (component_root / "reports.env").read_text(encoding="utf-8").splitlines() if line and not line.startswith("#")),
            "Unexpected Reports configuration")
    require(config["data"]["Mongo__DatabaseName"] == "arkya_reports" and
            not any(any(part in key for part in ("ConnectionString", "ServiceKey", "Password", "ClientSecret", "AdminKey", "KeySeed"))
                    or key.endswith("__Key") for key in config["data"]), "Reports credentials must stay out of ConfigMaps")
    seed = secret_env("ReportsOperator__KeySeed", "arkya-admin-secrets", "ContentApi__AdminKey")
    require(container["env"] == [secret_env("Mongo__ConnectionString", "arkya-content-secrets", "Mongo__ConnectionString"),
                                secret_env("Identity__ServiceKey", "arkya-identity-secrets", "ServiceAuthentication__Key"), seed],
            "Reports must reference exactly the approved existing Secret keys")
    pin, = component["images"]
    require(pin["name"] == "ghcr.io/hantse/arkya-services-reports", "Reports must own only its image pin")
    expected_image = pin["name"] + ("@" + pin["digest"] if "digest" in pin else ":" + pin["newTag"])
    require(container["image"] == expected_image, "Reports must inherit its separately promoted image pin")
    if require_digests:
        require(re.fullmatch(r"ghcr\.io/hantse/arkya-services-reports@sha256:[0-9a-f]{64}", expected_image),
                f"Unpublished image: {expected_image}")
    owned_keys = set()
    for expected in checks.yaml.safe_load_all((component_root / "reports.yaml").read_text(encoding="utf-8")):
        key = checks.identity(expected)
        owned_keys.add(key)
        if expected["kind"] == "Deployment":
            expected_container, = expected["spec"]["template"]["spec"]["containers"]
            expected_container["image"] = expected_image
            expected_container["envFrom"][0]["configMapRef"]["name"] = config_name
        require(resources.get(key) == expected, f"Unexpected Reports resource: {key}")
    require(owned_keys == {(kind, NS, "arkya-reports") for kind in ("Deployment", "Service", "NetworkPolicy")},
            "Reports must add exactly one private workload, service and policy")
    require(resources[("Service", NS, "arkya-reports")]["spec"]["type"] == "ClusterIP" and
            resources[("NetworkPolicy", NS, "arkya-reports")]["spec"]["ingress"] == ingress("gateway", "admin"),
            "Reports must accept only private Gateway and Admin traffic")
    require(pod["automountServiceAccountToken"] is False and "serviceAccountName" not in pod and
            pod["securityContext"]["runAsNonRoot"] is True and
            container["securityContext"] == {"allowPrivilegeEscalation": False, "readOnlyRootFilesystem": True,
                                               "capabilities": {"drop": ["ALL"]}} and
            container["readinessProbe"]["httpGet"]["path"] == "/health/ready", "Reports must retain secure runtime and dependency readiness")
    owned_keys.add(("ConfigMap", NS, config_name))
    normalized = deepcopy([document for document in documents if checks.identity(document) not in owned_keys])
    admin_container, = next(document for document in normalized if checks.identity(document) ==
                            ("Deployment", NS, "arkya-admin"))["spec"]["template"]["spec"]["containers"]
    require(admin_container.get("env", []).count(seed) == 1, "Admin needs exactly one Reports seed reference")
    admin_container["env"].remove(seed)
    destination = f"http://arkya-reports.{NS}.svc.cluster.local:8080/"
    expected_modules = {
        "arkya-admin": {"ReportsApi__Enabled": "true", "ReportsApi__AllowClusterHttp": "true",
                        "ReportsApi__BaseUrl": destination, "AdminMcp__Enabled": "true"},
        "arkya-gateway": {"Gateway__ReportsEnabled": "true", "ReverseProxy__Clusters__reports__Destinations__primary__Address": destination},
        "arkya-identity": {"InternalHttp__AllowedNetworks__1": "192.168.100.0/24"}}
    for app, expected_values in expected_modules.items():
        config, = [document["data"] for document in normalized if document["kind"] == "ConfigMap" and
                   document["metadata"]["name"].startswith(app + "-config-")]
        require(all(config.get(key) == value for key, value in expected_values.items()), f"Invalid {app} Reports activation")
        for key in expected_values:
            del config[key]
    identity_policy = next(document for document in normalized if checks.identity(document) ==
                           ("NetworkPolicy", NS, "arkya-identity"))["spec"]["ingress"]
    reports_ingress, = ingress("reports")
    require(identity_policy.count(reports_ingress) == 1, "Identity must admit exactly one private Reports peer")
    identity_policy.remove(reports_ingress)
    return normalized


def check_profile(documents, frontend_documents=None, beta_documents=None, require_digests=False):
    if any(document["kind"] == "Deployment" and document["metadata"]["name"] == "arkya-reports" for document in documents):
        documents = without_reports_component(documents, require_digests)
    if any(document["kind"] == "Deployment" and document["metadata"]["name"] == "arkya-inference" for document in documents):
        documents = without_inference_component(documents, require_digests)
    frontend_documents = frontend_documents or checks.render(ROOT.parent / "frontend-discord")
    beta_documents = beta_documents or checks.render(ROOT.parent / "beta")
    resources, frontend, beta = map(checks.index, (documents, frontend_documents, beta_documents))
    require(len(resources) == 40 and {d["kind"] for d in documents} == {
        "Deployment", "ConfigMap", "Service", "NetworkPolicy", "PersistentVolumeClaim",
        "Namespace", "ServiceAccount", "Role", "RoleBinding"},
        "Admin must render seven applications, two preserved PVCs and nine scoped realm resources without a Secret or Ingress")
    control_documents = checks.render(ROOT.parent / "realm-control")
    for key, document in control.check_profile(control_documents).items():
        require(resources.get(key) == document, f"Realm control resource was changed by the Admin overlay: {key}")
    control.check_project_patch()
    require(all(d["metadata"].get("namespace") in {NS, REALM_NS} or
                d["kind"] == "Namespace" and d["metadata"]["name"] == REALM_NS for d in documents),
            "Resources must remain within the services namespace and the one approved realm namespace")
    for kind in ("Deployment", "Service", "ConfigMap"):
        names = {d["metadata"]["name"] for d in documents if d["kind"] == kind and d["metadata"].get("namespace") == NS}
        expected = {"arkya-" + app for app in APPS}
        require(names == expected if kind != "ConfigMap" else
                all(any(name.startswith(prefix + "-config-") for prefix in expected) for name in names)
                and len(names) == len(expected), f"Unexpected {kind} coverage")
    configs, config_names = {}, {}
    identity_env = [secret_env("PlayerAdministration__IdentityKeySeed", "arkya-admin-secrets", "ContentApi__AdminKey")]
    admin_env = [secret_env("PlayerAdministration__IdentityKeySeed", "arkya-admin-secrets", "ContentApi__AdminKey"),
                 secret_env("PlayerAdministration__CharactersKeySeed", "arkya-admin-secrets", "RealmsApi__AdminKey")]
    characters_env = [
        secret_env("Mongo__ConnectionString", "arkya-content-secrets", "Mongo__ConnectionString"),
        secret_env("Identity__ServiceKey", "arkya-identity-secrets", "ServiceAuthentication__Key"),
        secret_env("PlayerAdministration__CharactersKeySeed", "arkya-admin-secrets", "RealmsApi__AdminKey"),
        secret_env("RealmRuntime__DedicatedKeySeed", "arkya-admin-secrets", "RealmsApi__AdminKey"),
        secret_env("Realms__ServiceKey", "arkya-realms-secrets", "GameAccess__ServiceKey")]
    realms_env = [secret_env("RealmRuntime__DedicatedKeySeed", "arkya-admin-secrets", "RealmsApi__AdminKey")]
    for app in sorted(APPS):
        name = "arkya-" + app
        deployment = resources[("Deployment", NS, name)]
        pod = deployment["spec"]["template"]["spec"]
        container, = pod["containers"]
        original_pod = beta[("Deployment", checks.namespace(app), name)]["spec"]["template"]["spec"]
        original_container, = original_pod["containers"]
        require(container["image"] == original_container["image"] and
                re.fullmatch(r"ghcr\.io/hantse/arkya-services-" + app + r"@sha256:[0-9a-f]{64}", container["image"]),
                f"{name} must inherit its CI-promoted digest")
        require(pod["automountServiceAccountToken"] is False,
                f"{name} must not receive the automatic Kubernetes credential volume")
        projected = [volume for volume in pod["volumes"] if "projected" in volume]
        if app == "realms":
            require(pod.get("serviceAccountName") == "arkya-realm-deployer" and projected == [{
                "name": "kubernetes-api", "projected": {"defaultMode": 288, "sources": [
                    {"serviceAccountToken": {"expirationSeconds": 3600, "path": "token"}},
                    {"configMap": {"name": "kube-root-ca.crt", "items": [{"key": "ca.crt", "path": "ca.crt"}]}}
                ]}}] and {"name": "kubernetes-api", "mountPath": "/var/run/secrets/arkya-kubernetes", "readOnly": True}
                in container["volumeMounts"], "Realms must receive only its explicit rotating Kubernetes token and CA")
        else:
            require("serviceAccountName" not in pod and not projected,
                    f"{name} must not acquire Kubernetes credentials")
        require(pod["securityContext"] == original_pod["securityContext"] and
                container["securityContext"] == original_container["securityContext"] and
                container["resources"] == original_container["resources"],
                f"{name} must preserve security and resource limits")
        expected_secrets = [] if app in {"frontend", "gateway", "characters"} else [name + "-secrets"]
        require([e["secretRef"]["name"] for e in container["envFrom"] if "secretRef" in e] == expected_secrets,
                f"{name} must preserve existing envFrom credentials; Characters uses exact key references")
        expected_env = {"identity": identity_env, "admin": admin_env, "characters": characters_env, "realms": realms_env}.get(app, [])
        require(container.get("env", []) == expected_env, f"Incorrect explicit Secret references in {name}")
        secret_volumes = [volume for volume in pod["volumes"] if "secret" in volume]
        registry_volume = {"name": "registry-credentials", "secret": {
            "secretName": "arkya-ghcr", "defaultMode": 288,
            "items": [{"key": ".dockerconfigjson", "path": "config.json"}]}}
        require(secret_volumes == ([registry_volume] if app == "realms" else []) and
                not any(port["containerPort"] == 8443 for port in container["ports"]),
                f"{name} must retain only the exact Realms registry credential mount and no listener TLS credentials")
        if app == "realms":
            require({"name": "registry-credentials", "mountPath": "/var/run/secrets/arkya-registry", "readOnly": True}
                    in container["volumeMounts"], "The registry Docker configuration must be read-only and mounted only in Realms")
        config_name, = [e["configMapRef"]["name"] for e in container["envFrom"] if "configMapRef" in e]
        require(re.fullmatch(re.escape(name + "-config-") + r"[a-z0-9]{10}", config_name),
                f"{name} configuration must trigger a rollout")
        config_names[app] = config_name
        config = resources[("ConfigMap", NS, config_name)]["data"]
        configs[app] = config
        require(not any(("Certificate" in key and not (app == "realms" and key == "Deployment__CertificateAuthorityFile"))
                        or key == "SSL_CERT_FILE" for key in config),
                f"{name} must not retain listener certificate configuration")
        require(not any(any(part in key for part in ("ConnectionString", "ServiceKey", "Password", "ClientSecret", "AdminKey", "KeySeed"))
                        or key.endswith("__Key") for key in config), f"Credentials found in {name} ConfigMap")
        service = resources[("Service", NS, name)]["spec"]
        labels = deployment["spec"]["template"]["metadata"]["labels"]
        require(service["selector"] == deployment["spec"]["selector"]["matchLabels"] and
                all(labels.get(key) == value for key, value in service["selector"].items()),
                f"Service selector mismatch for {name}")
        require(service["ports"] == [{"name": "http", "port": 80 if app == "gateway" else 8080, "targetPort": "http"}]
                and service["type"] == ("LoadBalancer" if app == "gateway" else "ClusterIP"),
                f"{name} must keep the existing private/public service boundary")
        policy = resources[("NetworkPolicy", NS, name)]["spec"]
        require(policy["podSelector"] == {"matchLabels": service["selector"]} and policy["policyTypes"] == ["Ingress"],
                f"NetworkPolicy selector mismatch for {name}")
        if app != "gateway":
            expected = ingress("admin", "gateway", nodes=True) if app == "characters" else ingress(
                "admin" if app in {"content", "realms"} else "gateway")
            require(policy["ingress"] == expected, f"Unexpected ingress in {name}")
        if app in {"content", "realms", "characters"}:
            require(config["ASPNETCORE_HTTP_PORTS"] == "8080" and
                    {v for k, v in config.items() if k.startswith("InternalHttp__AllowedNetworks__")} ==
                    {"10.42.0.0/16", "192.168.100.0/24"}, f"{name} must restrict private HTTP peers")
            require(not any(k.startswith("Kestrel__Endpoints__Internal__") for k in config),
                    f"Inherited TLS listener remains in {name}")
            host = f"{name}.{NS}.svc.cluster.local"
            expected_hosts = ("api.arkya.online;" if app == "characters" else "") + f"{name};{host}"
            require(config["AllowedHosts"] == expected_hosts, f"Unexpected public/private hosts in {name}")
            for probe in ("startupProbe", "readinessProbe", "livenessProbe"):
                path = "/health" if app != "realms" else ("/health/live" if probe == "livenessProbe" else "/health/ready")
                require(container[probe]["httpGet"] == {
                    "path": path, "port": "http", "scheme": "HTTP",
                    "httpHeaders": [{"name": "Host", "value": host}]}, f"Incorrect {name} health probe")

    # Preserve frontend, Discord, Identity storage and public gateway routing exactly.
    for key, document in frontend.items():
        expected = deepcopy(document)
        app = "gateway" if "arkya-gateway" in key[2] else "identity" if "arkya-identity" in key[2] else None
        if key[0] == "ConfigMap" and app:
            expected["metadata"]["name"] = config_names[app]
            expected["data"].update(
                {"AllowedHosts": "api.arkya.online;arkya.online;admin.arkya.online", "Gateway__AdminEnabled": "true",
                 "Gateway__CharactersEnabled": "true", "Gateway__RealmGameEnabled": "true", "Gateway__RealmGameAddress": REALM_ADDRESS}
                if app == "gateway" else {"Administration__Enabled": "true", "Administration__AllowClusterHttp": "true",
                                             "InternalHttp__AllowedNetworks__0": "10.42.0.0/16"})
        elif key[0] == "Deployment" and app:
            container = expected["spec"]["template"]["spec"]["containers"][0]
            container["envFrom"][0]["configMapRef"]["name"] = config_names[app]
            if app == "identity":
                container["env"] = identity_env
        require(resources[checks.identity(expected)] == expected,
                f"Unexpected change to running frontend/Discord resource: {key}")
    for app in ("identity", "admin"):
        name = "arkya-" + app
        key = ("PersistentVolumeClaim", NS, name + "-keys")
        expected = deepcopy(beta[("PersistentVolumeClaim", checks.namespace(app), name + "-keys")])
        expected["metadata"]["namespace"] = NS
        require(resources[key] == expected, f"{name} key PVC must survive pruning")
        deployment = resources[("Deployment", NS, name)]
        require(deployment["spec"]["replicas"] == 1 and deployment["spec"]["strategy"]["type"] == "Recreate" and
                checks.sync_wave(resources[key]) == checks.sync_wave(deployment), f"{name} must keep one RWO volume owner")
        require({"name": "keys", "persistentVolumeClaim": {"claimName": name + "-keys"}} in
                deployment["spec"]["template"]["spec"]["volumes"], f"{name} must retain its key volume")

    admin = configs["admin"]
    require(admin["AllowedHosts"] == "admin.arkya.online;arkya-admin;arkya-admin.arkya-beta.svc.cluster.local" and
            admin["Admin__DiscordEnabled"] == "true" and admin["Discord__PublicBaseUrl"] == "https://admin.arkya.online" and
            {k: v for k, v in admin.items() if k.startswith("Admin__AllowedDiscordUserIds__")} ==
            {"Admin__AllowedDiscordUserIds__0": "195861708248449024"}, "Existing Discord administrator access must remain unchanged")
    require(admin["ProxyTrust__KnownNetworks__0"] == "10.42.0.0/16" and admin["ProxyTrust__ForwardLimit"] == "1",
            "Admin must trust only its existing gateway hop")
    for module, app in (("ContentApi", "content"), ("RealmsApi", "realms"),
                        ("IdentityAdminApi", "identity"), ("CharactersAdminApi", "characters")):
        require(admin[module + "__Enabled"] == "true" and admin[module + "__AllowClusterHttp"] == "true" and
                admin[module + "__BaseUrl"] == f"http://arkya-{app}.{NS}.svc.cluster.local:8080/",
                f"{module} must use its explicitly-enabled private service")
    require(configs["gateway"]["Gateway__CharactersEnabled"] == "true",
            "The existing exact player route allowlist must expose Characters and admission through Gateway")
    require(configs["gateway"]["Gateway__RealmGameEnabled"] == "true" and
            configs["gateway"]["Gateway__RealmGameAddress"] == REALM_ADDRESS,
            "The existing Gateway must proxy only the configured beta game's private WebSocket Service")
    characters = configs["characters"]
    require(characters["Identity__BaseUrl"] == f"http://arkya-identity.{NS}.svc.cluster.local:8080/" and
            characters["Identity__AllowClusterHttp"] == "true" and characters["GameAccess__RequireBetaAccess"] == "true",
            "Characters must use private Identity introspection and enforce beta rights")
    require(characters["Realms__BaseUrl"] == f"http://arkya-realms.{NS}.svc.cluster.local:8080/" and
            characters["Realms__AllowClusterHttp"] == "true" and characters["GameAccess__AllowPrivateHttp"] == "true" and
            characters["RealmRuntime__RealmId"] == "beta-web" and characters["RealmRuntime__ServerId"] == "island" and
            not any(k.startswith("GameAccess__DedicatedServers__") for k in characters),
            "Characters must use the private Realms catalogue and derive its exact dedicated identity from the existing seed")
    realms = configs["realms"]
    require(realms["Deployment__Enabled"] == "true" and
            realms["Deployment__DeploymentTimeoutSeconds"] == "900" and
            realms["GameAccess__StandaloneEntries__0__Enabled"] == "false" and
            realms["Deployment__ApiServer"] == "https://kubernetes.default.svc" and
            realms["Deployment__TokenFile"] == "/var/run/secrets/arkya-kubernetes/token" and
            realms["Deployment__CertificateAuthorityFile"] == "/var/run/secrets/arkya-kubernetes/ca.crt" and
            realms["RealmRuntime__RegistryDockerConfigFile"] == "/var/run/secrets/arkya-registry/config.json" and
            realms["RealmRuntime__CharactersBaseUrl"] == f"http://arkya-characters.{NS}.svc.cluster.local:8080/" and
            realms["RealmRuntime__AllowClusterHttp"] == "true",
            "Realms must deploy through verified Kubernetes TLS with explicitly configured private Characters transport")
    require({k.removeprefix("Deployment__Targets__"): v for k, v in realms.items() if k.startswith("Deployment__Targets__")} == {
        "0__Id": "beta-web", "0__DisplayName": "Bêta web", "0__Namespace": REALM_NS, "0__RealmId": "beta-web"},
        "The only deployment target must bind beta-web to the exact user-approved namespace")
    profile = {k.removeprefix("Deployment__Profiles__"): v for k, v in realms.items() if k.startswith("Deployment__Profiles__")}
    image = profile.pop("0__Image", "")
    require(re.fullmatch(r"ghcr\.io/hantse/arkya-mmorpg-dedicated@sha256:[0-9a-f]{64}", image) and profile == {
        "0__Id": "beta-web", "0__DisplayName": "Île bêta · serveur WebSocket", "0__Servers__0__Id": "island",
        "0__ImagePullSecretName": "arkya-ghcr",
        "0__Servers__0__Port": "17943", "0__Servers__0__Transport": "WebSocket", "0__Servers__0__PublicEndpoint": GAME_ENDPOINT,
        "0__Servers__0__ProtocolVersion": "1", "0__Servers__0__BuildId": "arkya-web-beta-v1"},
        "The beta profile must pin its dedicated image and match the private listener and public WSS destination")
    policy = resources[("NetworkPolicy", NS, "arkya-identity-player-management")]["spec"]
    require(policy == {"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-identity"}},
                       "policyTypes": ["Ingress"], "ingress": ingress("admin", "characters")},
            "Only private Admin and Characters pods may gain new Identity ingress")
    return configs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    documents = checks.render(ROOT)
    check_profile(documents, require_digests=args.require_digests)
    count = sum(document["kind"] == "Deployment" for document in documents)
    print(f"Admin realm deployment: {len(documents)} resources, {count} applications, scoped Kubernetes credentials, private APIs, WSS routing and existing frontend/PVC preservation validated offline.")


if __name__ == "__main__":
    main()

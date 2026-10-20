"""Validate the in-place Admin/Identity/Characters profile without cluster access."""

from copy import deepcopy
import importlib.util
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_manifest_checks", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
NS = "arkya-beta"
APPS = {"identity", "gateway", "frontend", "admin", "content", "realms", "characters"}
require = checks.require


def secret_env(name, secret, key):
    return {"name": name, "valueFrom": {"secretKeyRef": {"name": secret, "key": key}}}


def ingress(*apps, nodes=False):
    peers = [{"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-" + app}}}
             for app in apps]
    if nodes:
        peers.append({"ipBlock": {"cidr": "192.168.100.0/24"}})
    return [{"from": peers, "ports": [{"protocol": "TCP", "port": 8080}]}]


def check_profile(documents, frontend_documents=None, beta_documents=None):
    frontend_documents = frontend_documents or checks.render(ROOT.parent / "frontend-discord")
    beta_documents = beta_documents or checks.render(ROOT.parent / "beta")
    resources, frontend, beta = map(checks.index, (documents, frontend_documents, beta_documents))
    require(len(resources) == 31 and {d["kind"] for d in documents} == {
        "Deployment", "ConfigMap", "Service", "NetworkPolicy", "PersistentVolumeClaim"},
        "Admin must render seven applications and two key PVCs, with no new resource kinds or declarative Secret")
    require(all(d["metadata"].get("namespace") == NS for d in documents),
            "Every resource must remain in the existing arkya-beta namespace")
    for kind in ("Deployment", "Service", "ConfigMap"):
        names = {d["metadata"]["name"] for d in documents if d["kind"] == kind}
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
        secret_env("PlayerAdministration__CharactersKeySeed", "arkya-admin-secrets", "RealmsApi__AdminKey")]
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
        require(pod["automountServiceAccountToken"] is False and "serviceAccountName" not in pod,
                f"{name} must not acquire Kubernetes credentials")
        require(pod["securityContext"] == original_pod["securityContext"] and
                container["securityContext"] == original_container["securityContext"] and
                container["resources"] == original_container["resources"],
                f"{name} must preserve security and resource limits")
        expected_secrets = [] if app in {"frontend", "gateway", "characters"} else [name + "-secrets"]
        require([e["secretRef"]["name"] for e in container["envFrom"] if "secretRef" in e] == expected_secrets,
                f"{name} must preserve existing envFrom credentials; Characters uses exact key references")
        expected_env = {"identity": identity_env, "admin": admin_env, "characters": characters_env}.get(app, [])
        require(container.get("env", []) == expected_env, f"Incorrect explicit Secret references in {name}")
        require(not any("secret" in volume for volume in pod["volumes"]) and
                not any(port["containerPort"] == 8443 for port in container["ports"]),
                f"{name} must not require TLS certificates or CA mounts")
        config_name, = [e["configMapRef"]["name"] for e in container["envFrom"] if "configMapRef" in e]
        require(re.fullmatch(re.escape(name + "-config-") + r"[a-z0-9]{10}", config_name),
                f"{name} configuration must trigger a rollout")
        config_names[app] = config_name
        config = resources[("ConfigMap", NS, config_name)]["data"]
        configs[app] = config
        require(not any("Certificate" in key or key == "SSL_CERT_FILE" for key in config),
                f"{name} must not retain certificate configuration")
        require(not any(any(part in key for part in ("ConnectionString", "ServiceKey", "Password", "ClientSecret", "AdminKey"))
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
            expected = ingress("admin", nodes=True) if app == "characters" else ingress(
                "admin" if app in {"content", "realms"} else "gateway")
            require(policy["ingress"] == expected, f"Unexpected ingress in {name}")
        if app in {"content", "realms", "characters"}:
            require(config["ASPNETCORE_HTTP_PORTS"] == "8080" and
                    {v for k, v in config.items() if k.startswith("InternalHttp__AllowedNetworks__")} ==
                    {"10.42.0.0/16", "192.168.100.0/24"}, f"{name} must restrict private HTTP peers")
            require(not any(k.startswith("Kestrel__Endpoints__Internal__") for k in config),
                    f"Inherited TLS listener remains in {name}")
            host = f"{name}.{NS}.svc.cluster.local"
            require(config["AllowedHosts"] == f"{name};{host}", f"Unexpected private hosts in {name}")
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
                {"AllowedHosts": "api.arkya.online;arkya.online;admin.arkya.online", "Gateway__AdminEnabled": "true"}
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
    require(configs["gateway"]["Gateway__CharactersEnabled"] == "false", "Characters must remain outside public YARP routing")
    characters = configs["characters"]
    require(characters["Identity__BaseUrl"] == f"http://arkya-identity.{NS}.svc.cluster.local:8080/" and
            characters["Identity__AllowClusterHttp"] == "true" and characters["GameAccess__RequireBetaAccess"] == "true",
            "Characters must use private Identity introspection and enforce beta rights")
    require(characters["Realms__BaseUrl"] == "" and
            not any(k.startswith("GameAccess__DedicatedServers__") for k in characters),
            "This profile must not invent dedicated identities or enable an undeployed game flow")
    require(configs["realms"]["Deployment__Enabled"] == "false" and
            configs["realms"]["GameAccess__StandaloneEntries__0__Enabled"] == "false" and
            not any(k.startswith(("Deployment__Targets__", "Deployment__Profiles__")) for k in configs["realms"]),
            "Dedicated deployment requires real targets and profiles before activation")
    policy = resources[("NetworkPolicy", NS, "arkya-identity-player-management")]["spec"]
    require(policy == {"podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-identity"}},
                       "policyTypes": ["Ingress"], "ingress": ingress("admin", "characters")},
            "Only private Admin and Characters pods may gain new Identity ingress")
    return configs


def main():
    documents = checks.render(ROOT)
    check_profile(documents)
    print(f"Admin player management: {len(documents)} resources, 7 applications, private API links, purpose-separated key seeds and existing frontend/PVC preservation validated offline.")


if __name__ == "__main__":
    main()

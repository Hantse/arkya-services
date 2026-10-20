"""Validate this opt-in overlay offline with kubectl kustomize and PyYAML."""

import importlib.util
from pathlib import Path


ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("arkya_manifests", ROOT.parent / "Test-Manifests.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)


def main():
    base = checks.render(ROOT.parent / "beta")
    documents = checks.render(ROOT)
    original = checks.index(base)
    rendered = checks.index(documents)
    base_config = checks.check_services(base, require_digests=True)
    config = checks.check_services(documents, require_digests=True)
    require = checks.require
    for key, resource in original.items():
        if key[0] == "NetworkPolicy":
            require(rendered.get(key) == resource, f"Existing policy changed: {key}")

    policy_key = ("NetworkPolicy", "arkya-identity", "arkya-admin-player-apis")
    new_policies = {key for key in rendered if key[0] == "NetworkPolicy"} - {
        key for key in original if key[0] == "NetworkPolicy"
    }
    require(new_policies == {policy_key}, "Unexpected network policy additions")
    policy = rendered[policy_key]["spec"]
    require(policy["podSelector"] == {"matchExpressions": [{
        "key": "app.kubernetes.io/name", "operator": "In",
        "values": ["arkya-identity", "arkya-characters"],
    }]}, "The new policy must select only Identity and Characters")
    require(policy["policyTypes"] == ["Ingress"], "Unexpected policy type")
    require(policy["ingress"] == [{"from": [{
        "namespaceSelector": {"matchLabels": {"kubernetes.io/metadata.name": "arkya-operations"}},
        "podSelector": {"matchLabels": {"app.kubernetes.io/name": "arkya-admin"}},
    }], "ports": [{"protocol": "TCP", "port": 8443}]}], "Admin access must bind both namespace and pod on TLS 8443")

    for prefix, service in [("IdentityAdminApi", "identity"), ("CharactersAdminApi", "characters")]:
        expected_url = f"https://arkya-{service}.arkya-identity.svc.cluster.local:8443/"
        require(config["admin"][prefix + "__Enabled"] == "true", f"{prefix} is not enabled")
        require(config["admin"][prefix + "__BaseUrl"] == expected_url, f"Wrong {prefix} destination")
        require(config["admin"][prefix + "__AllowClusterHttp"] == "false", f"HTTP enabled for {prefix}")
        require(config["admin"][prefix + "__CertificateAuthorityFile"] == "/etc/arkya/internal-ca/ca.crt",
                f"Explicit CA missing for {prefix}")
    require(config["identity"]["Administration__Enabled"] == "true", "Identity administration disabled")
    require(config["identity"]["Administration__AllowClusterHttp"] == "false", "Identity admin must require HTTPS")
    require(config["characters"]["GameAccess__RequireBetaAccess"] == "true", "Beta access must be enforced")
    auth_keys = {key for values in [base_config["admin"], config["admin"]] for key in values
                 if key.startswith(("Admin__", "Discord__"))}
    require(all(base_config["admin"].get(key) == config["admin"].get(key) for key in auth_keys),
            "This overlay must preserve Admin authentication settings")
    print(f"admin-console: {len(documents)} resources validated offline; existing policies and authentication preserved.")


if __name__ == "__main__":
    main()

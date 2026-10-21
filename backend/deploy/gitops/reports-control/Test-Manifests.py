"""Validate Reports alone and alongside Inference without contacting Kubernetes."""
import argparse
from copy import deepcopy
import importlib.util
import os
from pathlib import Path
import tempfile

import yaml

ROOT = Path(__file__).resolve().parent
ADMIN = ROOT.parent / "admin"
spec = importlib.util.spec_from_file_location("arkya_admin_contract", ADMIN / "Validate-PlayerManagement.py")
admin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(admin)


def render_preview(components):
    # Rebase Admin's relative sources into a temporary sibling to exercise both component orders.
    with tempfile.TemporaryDirectory(prefix=".reports-preview-", dir=ROOT.parent) as temporary:
        preview = Path(temporary)
        source = yaml.safe_load((ADMIN / "kustomization.yaml").read_text(encoding="utf-8"))
        def relative(value):
            return Path(os.path.relpath((ADMIN / value).resolve(), preview)).as_posix()
        rebased_resources = []
        for value in source["resources"]:
            if (ADMIN / value).is_file():
                (preview / value).write_bytes((ADMIN / value).read_bytes())
                rebased_resources.append(value)
            else:
                rebased_resources.append(relative(value))
        source["resources"] = rebased_resources
        source["components"] = [relative(value) for value in source.get("components", [])
                                if value not in ("../reports-control", "../inference-control")]
        source["components"] += [relative("../" + value + "-control") for value in components]
        # The Admin profile uses local env files; copies keep Kustomize's loader boundary intact.
        for generator in source.get("configMapGenerator", []):
            for name in generator.get("envs", []):
                (preview / name).write_bytes((ADMIN / name).read_bytes())
        for patch in source.get("patches", []):
            if "path" in patch:
                (preview / patch["path"]).write_bytes((ADMIN / patch["path"]).read_bytes())
        (preview / "kustomization.yaml").write_text(yaml.safe_dump(source, sort_keys=False), encoding="utf-8")
        return admin.checks.render(preview)


def check_rejections(documents):
    """Prove that shared validation rejects widened permissions and credential mistakes."""
    mutations = (
        lambda resources: resources[("Deployment", admin.NS, "arkya-reports")]["spec"]["template"]["spec"].update(automountServiceAccountToken=True),
        lambda resources: resources[("Service", admin.NS, "arkya-reports")]["spec"].update(type="LoadBalancer"),
        lambda resources: resources[("NetworkPolicy", admin.NS, "arkya-reports")]["spec"].update(ingress=[{}]),
        lambda resources: resources[("Deployment", admin.NS, "arkya-admin")]["spec"]["template"]["spec"]["containers"][0]["env"].append(
            {"name": "ReportsOperator__KeySeed", "value": "must-never-be-in-config"}),
    )
    for mutate in mutations:
        changed = deepcopy(documents)
        mutate(admin.checks.index(changed))
        try:
            admin.check_profile(changed)
        except (AssertionError, ValueError, KeyError):
            continue
        raise AssertionError("A malformed Reports component escaped validation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-digests", action="store_true")
    args = parser.parse_args()
    active = admin.checks.render(ADMIN)
    admin.check_profile(active, require_digests=args.require_digests)
    for components in (("reports",), ("inference", "reports"), ("reports", "inference")):
        documents = render_preview(components)
        admin.check_profile(documents, require_digests=args.require_digests)
        check_rejections(documents)
        print(f"Validated {len(documents)} objects: {' + '.join(components)}, existing Secret references and unchanged Admin services.")


if __name__ == "__main__":
    main()

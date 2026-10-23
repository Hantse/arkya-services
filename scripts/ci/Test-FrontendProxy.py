"""Smoke-test an already built frontend image and its Unity reverse proxy."""

import argparse
from contextlib import closing
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import ProxyHandler, Request, build_opener
import uuid


FIXTURE_CONFIG = """worker_processes 1;
pid /tmp/nginx.pid;
error_log /dev/stderr warn;
events { worker_connections 128; }
http {
    access_log /dev/stdout;
    client_body_temp_path /tmp/client_temp;
    proxy_temp_path /tmp/proxy_temp;
    fastcgi_temp_path /tmp/fastcgi_temp;
    uwsgi_temp_path /tmp/uwsgi_temp;
    scgi_temp_path /tmp/scgi_temp;
    default_type application/octet-stream;
    gzip off;
    server {
        listen 8080;
        server_name _;
        root /fixture;
        location = /healthz { default_type text/plain; return 200 "ok\\n"; }
        location = /game/build-config.json {
            default_type application/json;
            add_header Cache-Control "no-store" always;
            try_files $uri =404;
        }
        location = /game/Build/fixture.data.unityweb {
            add_header Cache-Control "no-cache" always;
            try_files $uri =404;
        }
        location = /game/inspect-headers {
            default_type text/plain;
            return 200 "authorization=$http_authorization\\ncookie=$http_cookie\\nhost=$http_host\\nuri=$request_uri\\n";
        }
        location / { return 404; }
    }
}
"""


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def docker(*arguments, timeout=60):
    result = subprocess.run(["docker", *arguments], capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"docker {' '.join(arguments)} failed:\n{result.stderr.strip()}")
    return result.stdout.strip()


def request(base_url, path, headers=None):
    # Always address the loopback port directly, including on runners with HTTP_PROXY.
    opener = build_opener(ProxyHandler({}))
    try:
        response = opener.open(Request(base_url + path, headers=headers or {}), timeout=15)
    except HTTPError as error:
        response = error
    with closing(response):
        return response.status, response.headers, response.read()


def wait_ready(base_url):
    deadline = time.monotonic() + 30
    last_error = "no response"
    while time.monotonic() < deadline:
        try:
            status, _, body = request(base_url, "/healthz")
            if status == 200 and body == b"ok\n":
                return
            last_error = f"HTTP {status}: {body[:100]!r}"
        except (OSError, URLError) as error:
            last_error = str(error)
        time.sleep(0.25)
    raise RuntimeError(f"Container did not become ready at {base_url}: {last_error}")


def published_url(container):
    binding = docker("port", container, "8080/tcp").splitlines()
    require(len(binding) == 1 and binding[0].startswith("127.0.0.1:"),
            f"Expected one loopback-only port for {container}: {binding}")
    return "http://" + binding[0]


def check_site(base_url):
    status, _, body = request(base_url, "/healthz")
    require(status == 200 and body == b"ok\n", "Frontend health probe failed")
    for path in ("/", "/play.html"):
        status, headers, body = request(base_url, path)
        require(status == 200 and headers.get_content_type() == "text/html" and body,
                f"Frontend page unavailable: {path} (HTTP {status})")
    status, headers, body = request(base_url, "/auth-client.mjs")
    require(status == 200 and headers.get_content_type() == "application/javascript" and body,
            "Frontend module must be served as application/javascript")
    status, _, _ = request(base_url, "/frontend-proxy-ci-missing.html")
    require(status == 404, "Unknown frontend paths must remain 404")


def write_fixture(directory, marker):
    directory.chmod(0o755)
    (directory / "game" / "Build").mkdir(parents=True)
    manifest = (json.dumps({"schemaVersion": 1, "buildId": "arkya-web-beta-v1",
                            "proxyFixture": marker,
                            "dataUrl": "Build/fixture.data.unityweb"}, sort_keys=True) + "\n").encode()
    payload = b"\x1f\x8b\x00Arkya proxy CI\x00" + bytes(range(256)) * 32 + marker.encode()
    (directory / "nginx.conf").write_text(FIXTURE_CONFIG, encoding="utf-8", newline="\n")
    (directory / "game" / "build-config.json").write_bytes(manifest)
    (directory / "game" / "Build" / "fixture.data.unityweb").write_bytes(payload)
    # The unprivileged container needs to traverse the runner-owned temporary directory.
    for path in directory.rglob("*"):
        path.chmod(0o755 if path.is_dir() else 0o644)
    return manifest, payload


def smoke_test(image):
    docker("image", "inspect", image)
    marker = uuid.uuid4().hex
    prefix = "arkya-proxy-ci-" + marker
    network = prefix + "-network"
    containers = []
    succeeded = False
    with tempfile.TemporaryDirectory(prefix="arkya-proxy-ci-") as temporary:
        fixture = Path(temporary).resolve()
        manifest, payload = write_fixture(fixture, marker)
        try:
            docker("network", "create", network)

            def start(name, *options, command=(), fixture_server=False):
                containers.append(name)
                arguments = ["run", "--detach", "--pull", "never", "--name", name,
                             "--network", network, "--read-only", "--user", "101:101",
                             "--tmpfs", "/tmp:rw,mode=1777", "--publish", "127.0.0.1::8080"]
                if fixture_server:
                    arguments += ["--entrypoint", "nginx"]
                docker(*arguments, *options, image, *command)
                base_url = published_url(name)
                wait_ready(base_url)
                return base_url

            absent = start(prefix + "-absent")
            check_site(absent)
            status, _, _ = request(absent, "/game/build-config.json")
            require(status == 502, f"Missing Unity upstream must return 502, received {status}")
            check_site(absent)

            alias = "unity-fixture"
            upstream = start(prefix + "-fixture", "--network-alias", alias,
                             "--mount", f"type=bind,src={fixture},dst=/fixture,readonly",
                             command=("-c", "/fixture/nginx.conf", "-g", "daemon off;"),
                             fixture_server=True)
            status, _, body = request(upstream, "/game/build-config.json")
            require(status == 200 and body == manifest, "Unity fixture must serve its unique manifest")

            frontend = start(prefix + "-frontend", "--env", f"ARKYA_UNITY_UPSTREAM={alias}:8080")
            check_site(frontend)
            status, headers, body = request(frontend, "/game/build-config.json")
            require(status == 200 and body == manifest, "Frontend must proxy the exact upstream manifest")
            require(headers.get_content_type() == "application/json" and headers.get("Cache-Control") == "no-store",
                    "Frontend must preserve manifest MIME and no-store caching")
            status, headers, body = request(frontend, "/game/Build/fixture.data.unityweb")
            require(status == 200 and body == payload, "Frontend must preserve every Unity payload byte")
            require(headers.get_content_type() == "application/octet-stream" and
                    headers.get("Cache-Control") == "no-cache" and headers.get("Content-Encoding") is None,
                    "Frontend must preserve binary MIME/cache without adding content encoding")
            require(headers.get("Content-Length") == str(len(payload)), "Unity payload length changed in transit")

            path = "/game/inspect-headers?probe=path%2Fpreserved"
            status, _, body = request(frontend, path, {
                "Host": "proxy-ci.arkya.invalid", "Authorization": "Bearer frontend-proxy-ci",
                "Cookie": "arkya_proxy_ci=fixture"})
            expected = f"authorization=\ncookie=\nhost=proxy-ci.arkya.invalid\nuri={path}\n".encode()
            require(status == 200 and body == expected,
                    f"Unity proxy must strip credentials and preserve Host/path: {body!r}")
            status, _, _ = request(frontend, "/game/frontend-proxy-ci-missing.bin")
            require(status == 404, "Upstream 404 must remain 404")
            succeeded = True
        finally:
            cleanup_errors = []
            for container in reversed(containers):
                if not succeeded:
                    try:
                        logs = subprocess.run(["docker", "logs", "--tail", "50", container],
                                              capture_output=True, text=True, timeout=15)
                        print(f"--- {container} ---\n{logs.stdout}{logs.stderr}", file=sys.stderr)
                    except (OSError, subprocess.TimeoutExpired):
                        pass
                try:
                    docker("rm", "--force", container, timeout=30)
                except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                    cleanup_errors.append(str(error))
            try:
                docker("network", "rm", network, timeout=30)
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                cleanup_errors.append(str(error))
            if cleanup_errors:
                print("Frontend proxy cleanup: " + "\n".join(cleanup_errors), file=sys.stderr)
                if succeeded:
                    raise RuntimeError("Frontend checks passed, but owned Docker resources could not all be removed")
    print("Frontend proxy validated: unavailable-upstream isolation, pages/MIME, DNS override, exact Unity bytes, cache and credential stripping.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="arkya-frontend-ci", help="Locally built frontend image; never pulled")
    arguments = parser.parse_args()
    try:
        smoke_test(arguments.image)
    except (AssertionError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        raise SystemExit(str(error)) from error

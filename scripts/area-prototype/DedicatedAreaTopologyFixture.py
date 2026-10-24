"""Loopback topology fixture for real dedicated scene-loading validation.

Only GET /internal/game/topology is implemented. This fixture does not emulate
admission, persistence, clients, or server gameplay.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import ipaddress
import json
from pathlib import Path
import threading
from urllib.parse import urlsplit


def read_configuration(path):
    configuration = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    servers = configuration["servers"]
    if len(servers) != 4 or len({item["serverId"] for item in servers}) != 4:
        raise ValueError("The fixture requires four distinct servers.")
    counts = Counter(item["areaId"] for item in servers)
    if counts != {"starter-island": 2, "handoff-valley": 2}:
        raise ValueError("The fixture requires two dedicateds per Area.")
    if len({item["key"] for item in servers}) != 4 or any(len(item["key"]) < 32 for item in servers):
        raise ValueError("The fixture requires distinct dedicated credentials.")
    for item in servers:
        endpoint = urlsplit(item["endpoint"])
        if endpoint.scheme != "ws" or not ipaddress.ip_address(endpoint.hostname).is_loopback or endpoint.port is None:
            raise ValueError("Game endpoints must use loopback WebSocket transport.")
        if endpoint.username or endpoint.password or endpoint.query or endpoint.fragment:
            raise ValueError("Game endpoints cannot contain credentials or parameters.")
    return configuration


def topology_for(configuration, own_server):
    servers = configuration["servers"]
    areas = {item["areaId"]: item["contentId"] for item in servers}
    return {
        "realmId": configuration["realmId"],
        "displayName": "Dedicated Area loading fixture",
        "serverId": own_server["serverId"],
        "endpoint": own_server["endpoint"],
        "buildId": configuration["buildId"],
        "protocolVersion": configuration["protocolVersion"],
        "topologyVersion": "dedicated-area-loading-2x2-v1",
        "ready": True,
        "partitions": [
            {
                "serverId": item["serverId"],
                "endpoint": item["endpoint"],
                "islandId": item["areaId"],
                "partitionIndex": index % 2,
                "bounds": item["bounds"],
            }
            for index, item in enumerate(servers)
        ],
        "areas": [{"areaId": area, "contentId": content} for area, content in areas.items()],
        "areaTransitions": [],
    }


def make_handler(configuration, events_path):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_arguments):
            pass

        def do_GET(self):
            if self.path != "/internal/game/topology":
                self.send_error(404)
                return
            credential = self.headers.get("X-Arkya-Dedicated-Key", "")
            own_server = next((item for item in configuration["servers"]
                               if hmac.compare_digest(item["key"], credential)), None)
            status = 200 if own_server else 401
            event = {
                "observedAt": datetime.now(timezone.utc).isoformat(),
                "status": status,
                "serverId": own_server["serverId"] if own_server else None,
            }
            with lock, events_path.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(event, separators=(",", ":")) + "\n")
            body = json.dumps(topology_for(configuration, own_server) if own_server else
                              {"error": "Unauthorized"}, separators=(",", ":")).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--ready", required=True)
    parser.add_argument("--events", required=True)
    arguments = parser.parse_args()
    configuration = read_configuration(arguments.configuration)
    server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(configuration, Path(arguments.events)))
    server.daemon_threads = True
    ready_path = Path(arguments.ready)
    temporary_path = ready_path.with_suffix(".tmp")
    temporary_path.write_text(json.dumps({"origin": f"http://127.0.0.1:{server.server_port}"}), encoding="utf-8")
    temporary_path.replace(ready_path)
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__ == "__main__":
    main()

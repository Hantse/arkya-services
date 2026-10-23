"""Arkya inference machine agent. Python standard library; Windows x64 deployment."""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import hashlib
import ipaddress
import json
import logging
import os
import pathlib
import re
import signal
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

VERSION = "1.1.0"


def load_catalog():
    root = pathlib.Path(__file__).resolve().parent
    path = root / "models.json"
    if not path.exists():
        path = root.parents[1] / "src/Arkya.ServiceDefaults/InferenceModels.json"
    catalog = json.loads(path.read_text(encoding="utf-8-sig"))
    models = {row["modelId"]: row for row in catalog["models"]}
    if catalog["defaultModelId"] not in models or not models:
        raise ValueError("Invalid inference model catalog")
    return models, catalog["defaultModelId"]


MODELS, DEFAULT_MODEL_ID = load_catalog()
MAX_WORKERS = 12
WORKER_ID = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")
AGENT_ID = re.compile(r"[a-z][a-z0-9-]{1,46}[a-z0-9]")
SYSTEM = """Tu incarnes Maëlle, aubergiste à Aubefont. Réponds au voyageur en français,
avec chaleur et simplicité, en une ou deux phrases courtes. Reste dans ton personnage.
Faits connus : une chambre coûte 8 pièces d'argent ; une soupe coûte 3 pièces d'argent ;
le moulin est au sud du village. Aucune récompense n'est disponible.
Utilise les souvenirs fournis. N'invente aucun fait, souvenir, prix ou promesse.
Si tu ne sais pas, dis-le simplement. Pour une chambre ou une soupe, annonce le prix
sans confirmer de transaction. Personnalise ta réponse avec un souvenir pertinent si tu en as un.
Réponds uniquement avec le JSON {"reply":"ta réplique"}."""
SCHEMA = {"type": "object", "properties": {"reply": {"type": "string"}},
          "required": ["reply"], "additionalProperties": False}
BUTTONS = [{"id": "rent_room", "label": "Louer une chambre"},
           {"id": "buy_meal", "label": "Commander une soupe"},
           {"id": "ask_news", "label": "Demander les nouvelles"}]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise urllib.error.HTTPError(req.full_url, code, "Redirect refused", headers, fp)


class HttpsDownloadRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if urllib.parse.urlsplit(newurl).scheme != "https":
            raise urllib.error.HTTPError(req.full_url, code, "Insecure download redirect refused", headers, fp)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


LOCAL_HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
CONTROL_HTTP = urllib.request.build_opener(NoRedirect())
DOWNLOAD_HTTP = urllib.request.build_opener(HttpsDownloadRedirect())


def atomic_json(path: pathlib.Path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)


def validate_backend(url: str, allow_loopback_http=False) -> str:
    parsed = urllib.parse.urlsplit(url)
    if (not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment
            or parsed.scheme not in ("https", "http")):
        raise ValueError("Backend URL must be an absolute HTTPS URL without credentials/query/fragment")
    if parsed.scheme != "https":
        try:
            loopback = ipaddress.ip_address(parsed.hostname).is_loopback
        except ValueError:
            loopback = parsed.hostname == "localhost"
        if not (allow_loopback_http and loopback):
            raise ValueError("HTTP is allowed only for explicitly enabled loopback tests")
    return url.rstrip("/")


def validate_desired(value):
    if not isinstance(value, list) or len(value) > MAX_WORKERS:
        raise ValueError("Invalid desired workers")
    desired = {}
    for row in value:
        worker_id = row.get("workerId") if isinstance(row, dict) else None
        threads = row.get("threads") if isinstance(row, dict) else None
        model_id = row.get("modelId", DEFAULT_MODEL_ID) if isinstance(row, dict) else None
        if (not isinstance(worker_id, str) or not WORKER_ID.fullmatch(worker_id)
                or worker_id in desired or type(threads) is not int or not 1 <= threads <= 24
                or not isinstance(model_id, str) or model_id not in MODELS):
            raise ValueError("Invalid worker ID or CPU allocation")
        desired[worker_id] = (threads, model_id)
    if sum(row[0] for row in desired.values()) > 96:
        raise ValueError("CPU thread allocation exceeds 96")
    return desired


def post_json(url, payload, token=None, timeout=15, local=False):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(url, json.dumps(payload).encode("utf-8"), headers)
    with (LOCAL_HTTP if local else CONTROL_HTTP).open(request, timeout=timeout) as response:
        return json.loads(response.read(1048576))


def local_json(port, path, payload=None, timeout=10):
    url = f"http://127.0.0.1:{port}{path}"
    if payload is not None:
        return post_json(url, payload, timeout=timeout, local=True)
    with LOCAL_HTTP.open(url, timeout=timeout) as response:
        return json.loads(response.read(1048576))


def unprotect_token(path):
    if os.name != "nt":
        raise RuntimeError("Protected agent credentials require Windows DPAPI")
    from ctypes import wintypes

    class Blob(ctypes.Structure):
        _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_byte))]

    encrypted = pathlib.Path(path).read_bytes()
    buffer = ctypes.create_string_buffer(encrypted)
    source = Blob(len(encrypted), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_byte)))
    result = Blob()
    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    crypt32.CryptUnprotectData.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p,
        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.LocalFree.argtypes = [ctypes.c_void_p]
    kernel.LocalFree.restype = ctypes.c_void_p
    if not crypt32.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 0,
                                     ctypes.byref(result)):
        raise RuntimeError("Cannot decrypt agent credential")
    try:
        return ctypes.string_at(result.data, result.size).decode("utf-8")
    finally:
        kernel.LocalFree(result.data)


def creation_filetime():
    if os.name != "nt":
        return None
    from ctypes import wintypes
    values = [wintypes.FILETIME() for _ in range(4)]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel.GetProcessTimes.restype = wintypes.BOOL
    if not kernel.GetProcessTimes(kernel.GetCurrentProcess(), *(ctypes.byref(v) for v in values)):
        raise ctypes.WinError(ctypes.get_last_error())
    return (values[0].dwHighDateTime << 32) | values[0].dwLowDateTime


def total_memory_mb():
    if os.name == "nt":
        class MemoryStatus(ctypes.Structure):
            _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong)] + [
                (name, ctypes.c_ulonglong) for name in (
                    "totalPhysical", "availablePhysical", "totalPage", "availablePage",
                    "totalVirtual", "availableVirtual", "availableExtended")]
        status = MemoryStatus()
        status.length = ctypes.sizeof(status)
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
        return status.totalPhysical // 1048576
    return os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES") // 1048576


class ProcessJob:
    """Closing this owned Windows job terminates only its assigned child processes."""

    def __init__(self):
        self.handle = None
        self.closed = False
        self.lock = threading.Lock()
        if os.name != "nt":
            return
        from ctypes import wintypes

        class Basic(ctypes.Structure):
            _fields_ = [("processTime", ctypes.c_longlong), ("jobTime", ctypes.c_longlong),
                        ("flags", wintypes.DWORD), ("minimum", ctypes.c_size_t),
                        ("maximum", ctypes.c_size_t), ("active", wintypes.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD),
                        ("scheduling", wintypes.DWORD)]

        class Io(ctypes.Structure):
            _fields_ = [(name, ctypes.c_ulonglong) for name in (
                "readOps", "writeOps", "otherOps", "readBytes", "writeBytes", "otherBytes")]

        class Extended(ctypes.Structure):
            _fields_ = [("basic", Basic), ("io", Io), ("processMemory", ctypes.c_size_t),
                        ("jobMemory", ctypes.c_size_t), ("peakProcess", ctypes.c_size_t),
                        ("peakJob", ctypes.c_size_t)]

        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateJobObjectW.restype = wintypes.HANDLE
        self.kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int,
                                                       ctypes.c_void_p, wintypes.DWORD]
        self.kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self.handle = self.kernel.CreateJobObjectW(None, None)
        limits = Extended()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.handle or not self.kernel.SetInformationJobObject(
                self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            raise ctypes.WinError(ctypes.get_last_error())

    def assign(self, process):
        with self.lock:
            if self.closed:
                process.kill()
                process.wait(timeout=5)
                raise RuntimeError("Worker was stopped during startup")
            if self.handle and not self.kernel.AssignProcessToJobObject(self.handle, int(process._handle)):
                process.kill()
                process.wait(timeout=5)
                raise ctypes.WinError(ctypes.get_last_error())

    def close(self):
        with self.lock:
            self.closed = True
            if self.handle:
                self.kernel.CloseHandle(self.handle)
                self.handle = None


@dataclass
class Worker:
    worker_id: str
    threads: int
    port: int
    model_id: str = DEFAULT_MODEL_ID
    state: str = "starting"
    busy: bool = False
    wanted: bool = True
    last_error: str | None = None
    process: object = None
    job: object = None
    thread: object = None
    retired_at: float | None = None
    stop_event: threading.Event = field(default_factory=threading.Event)

    def observed(self):
        return {"workerId": self.worker_id, "threads": self.threads, "port": self.port,
                "modelId": self.model_id,
                "pid": self.process.pid if self.process and self.process.poll() is None else None,
                "state": self.state, "lastError": self.last_error}


class PoolBusy(RuntimeError):
    pass


class WorkerPool:
    def __init__(self, config, stop_event=None):
        self.config = config
        self.stop_event = stop_event or threading.Event()
        self.condition = threading.Condition(threading.RLock())
        self.workers: dict[str, Worker] = {}
        self.desired = {}
        self.waiters = 0
        self.model_ready = set()
        self.model_locks = {model_id: threading.Lock() for model_id in MODELS}

    def snapshot(self):
        with self.condition:
            return [worker.observed() for worker in self.workers.values()]

    def reconcile(self, desired):
        desired = {key: (value, DEFAULT_MODEL_ID) if type(value) is int else tuple(value)
                   for key, value in desired.items()}
        with self.condition:
            self.desired = dict(desired)
            for worker in self.workers.values():
                if desired.get(worker.worker_id) != (worker.threads, worker.model_id):
                    worker.wanted = False
                    worker.state = "stopping" if worker.state != "stopped" else "stopped"
                    if not worker.busy and worker.job:
                        worker.job.close()
            occupied = {worker.port for worker in self.workers.values() if worker.state != "stopped"}
            for worker_id, (threads, model_id) in desired.items():
                worker = self.workers.get(worker_id)
                if worker and worker.state != "stopped":
                    continue
                if worker and worker.retired_at and time.monotonic() - worker.retired_at < 5:
                    continue
                port = next((self.config["baseWorkerPort"] + n for n in range(MAX_WORKERS)
                             if self.config["baseWorkerPort"] + n not in occupied), None)
                if port is None:
                    continue
                occupied.add(port)
                worker = Worker(worker_id, threads, port, model_id=model_id)
                self.workers[worker_id] = worker
                worker.thread = threading.Thread(target=self._worker_main, args=(worker,), daemon=True)
                worker.thread.start()
            for worker_id in list(self.workers):
                worker = self.workers[worker_id]
                if worker_id not in desired and worker.state == "stopped":
                    del self.workers[worker_id]
            self.condition.notify_all()

    def _worker_main(self, worker):
        outputs = []
        model = MODELS[worker.model_id]
        model_name = model["ollamaModel"]
        try:
            with socket.socket() as probe:
                probe.bind(("127.0.0.1", worker.port))
            env = os.environ.copy()
            env.update({"OLLAMA_HOST": f"127.0.0.1:{worker.port}",
                        "OLLAMA_MODELS": self.config["modelCache"], "OLLAMA_NUM_PARALLEL": "1",
                        "OLLAMA_MAX_LOADED_MODELS": "1", "OLLAMA_CONTEXT_LENGTH": "2048",
                        "OLLAMA_NO_CLOUD": "1", "OLLAMA_NOPRUNE": "1",
                        "CUDA_VISIBLE_DEVICES": "-1", "GGML_VK_VISIBLE_DEVICES": "-1"})
            for stream in ("stdout", "stderr"):
                outputs.append((pathlib.Path(self.config["logsDirectory"]) /
                                f"worker-{worker.worker_id}.{stream}.log").open("w", encoding="utf-8"))
            worker.job = ProcessJob()
            worker.process = subprocess.Popen([self.config["ollamaExecutable"], "serve"],
                env=env, stdout=outputs[0], stderr=outputs[1],
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            worker.job.assign(worker.process)
            deadline = time.monotonic() + 45
            while not self.stop_event.is_set() and worker.wanted:
                if worker.process.poll() is not None:
                    raise RuntimeError("Owned model process exited")
                try:
                    local_json(worker.port, "/api/version", timeout=1)
                    break
                except (OSError, ValueError):
                    if time.monotonic() >= deadline:
                        raise RuntimeError("Model worker readiness timed out")
                    self.stop_event.wait(0.3)
            if self.stop_event.is_set() or not worker.wanted:
                return
            worker.state = "loading"
            # All workers share immutable model blobs; download once, before warming others.
            model_lock = self.model_locks[worker.model_id]
            while not model_lock.acquire(timeout=0.3):
                if self.stop_event.is_set() or not worker.wanted:
                    return
            try:
                if worker.model_id not in self.model_ready:
                    self._prepare_model(worker, model)
                    self.model_ready.add(worker.model_id)
            finally:
                model_lock.release()
            if self.stop_event.is_set() or not worker.wanted:
                return
            local_json(worker.port, "/api/generate", {"model": model_name, "prompt": "Bonjour",
                "stream": False, "keep_alive": -1,
                "options": {"num_gpu": 0, "num_thread": worker.threads,
                            "num_ctx": 2048, "num_predict": 1}}, timeout=120)
            self._check_cpu(worker)
            with self.condition:
                worker.state = "ready" if worker.wanted else "stopping"
                self.condition.notify_all()
            logging.info("Worker %s ready on loopback port %s (%s CPU threads)",
                         worker.worker_id, worker.port, worker.threads)
            while not self.stop_event.wait(0.5):
                with self.condition:
                    if not worker.wanted and not worker.busy:
                        break
                if worker.process.poll() is not None:
                    raise RuntimeError("Owned model process exited")
        except Exception as error:
            worker.last_error = safe_error(error)
            logging.warning("Worker %s: %s", worker.worker_id, worker.last_error)
        finally:
            worker.state = "stopping"
            if worker.process and worker.process.poll() is None:
                # This Popen owns an open OS process handle, preventing PID-reuse mistakes.
                with contextlib.suppress(Exception):
                    local_json(worker.port, "/api/generate", {"model": model_name, "stream": False,
                               "keep_alive": 0}, timeout=2)
                worker.process.terminate()
                try:
                    worker.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    worker.process.kill()
                    worker.process.wait(timeout=5)
            if worker.job:
                worker.job.close()
            for output in outputs:
                output.close()
            with self.condition:
                worker.state = "stopped"
                worker.retired_at = time.monotonic()
                self.condition.notify_all()

    def _prepare_model(self, worker, model):
        if not model.get("ggufUrl"):
            try:
                local_json(worker.port, "/api/show", {"model": model["ollamaModel"]})
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
                logging.info("Downloading catalog model %s", worker.model_id)
                local_json(worker.port, "/api/pull", {"model": model["ollamaModel"], "stream": False}, timeout=1800)
            return
        digest = model["ggufSha256"]
        if not re.fullmatch(r"[a-f0-9]{64}", digest) or urllib.parse.urlsplit(model["ggufUrl"]).scheme != "https":
            raise ValueError("Catalog GGUF source must be pinned with SHA256 and HTTPS")
        blob = pathlib.Path(self.config["modelCache"]) / "blobs" / ("sha256-" + digest)
        blob.parent.mkdir(parents=True, exist_ok=True)
        existing_digest = None
        if blob.exists():
            with blob.open("rb") as source:
                existing_digest = hashlib.file_digest(source, "sha256").hexdigest()
        if existing_digest != digest:
            temporary = blob.with_suffix(".download")
            logging.info("Downloading pinned catalog model %s", worker.model_id)
            with DOWNLOAD_HTTP.open(model["ggufUrl"], timeout=60) as response, temporary.open("wb") as output:
                if urllib.parse.urlsplit(response.url).scheme != "https":
                    raise ValueError("Insecure model download redirect refused")
                checksum = hashlib.sha256()
                while chunk := response.read(1048576):
                    if self.stop_event.is_set() or not worker.wanted:
                        raise RuntimeError("Model download cancelled")
                    output.write(chunk)
                    checksum.update(chunk)
            if checksum.hexdigest() != digest:
                temporary.unlink(missing_ok=True)
                raise ValueError("Model SHA256 verification failed")
            temporary.replace(blob)
        # Explicit text-only import excludes the unsupported vision projector in Ollama 0.20.2.
        local_json(worker.port, "/api/create", {"model": model["ollamaModel"],
            "files": {"model.gguf": "sha256:" + digest}, "template": model["template"],
            "parameters": {"stop": model["stop"]}, "stream": False}, timeout=120)

    @staticmethod
    def _check_cpu(worker):
        models = local_json(worker.port, "/api/ps").get("models", [])
        if not models or any(model.get("size_vram", 0) != 0 for model in models):
            raise RuntimeError("Worker CPU-only verification failed")

    @contextlib.contextmanager
    def acquire(self, timeout=30):
        deadline = time.monotonic() + timeout
        with self.condition:
            if self.waiters >= 12:
                raise PoolBusy("La file d'attente est pleine.")
            self.waiters += 1
            try:
                while True:
                    if self.stop_event.is_set():
                        raise PoolBusy("Le moteur s'arrête.")
                    worker = next((w for w in self.workers.values()
                                   if w.wanted and w.state == "ready" and not w.busy), None)
                    if worker:
                        worker.busy, worker.state = True, "busy"
                        break
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        raise PoolBusy("Aucun moteur disponible dans le délai prévu.")
                    self.condition.wait(remaining)
            finally:
                self.waiters -= 1
        try:
            yield worker
        finally:
            with self.condition:
                worker.busy = False
                worker.state = "ready" if worker.wanted and worker.state == "busy" else "stopping"
                self.condition.notify_all()

    def close(self):
        self.stop_event.set()
        with self.condition:
            self.condition.notify_all()
        deadline = time.monotonic() + 10
        for worker in list(self.workers.values()):
            if worker.thread:
                worker.thread.join(timeout=max(0, deadline - time.monotonic()))
        for worker in list(self.workers.values()):
            if worker.job:
                worker.job.close()


def safe_error(error):
    # Never log headers, request bodies, remote response contents or credential values.
    if isinstance(error, urllib.error.HTTPError):
        return f"HTTP {error.code}"
    if isinstance(error, (ValueError, RuntimeError, PoolBusy)):
        return str(error)[:200]
    return type(error).__name__


def validate_chat(payload):
    if not isinstance(payload, dict):
        raise ValueError("Requête invalide.")
    question = payload.get("question")
    if not isinstance(question, str) or not 1 <= len(question.strip()) <= 300:
        raise ValueError("Écris une question de 1 à 300 caractères.")
    history = payload.get("history", [])
    if not isinstance(history, list) or len(history) > 8:
        raise ValueError("Historique invalide.")
    clean = []
    for row in history:
        if (not isinstance(row, dict) or row.get("role") not in ("user", "assistant")
                or not isinstance(row.get("content"), str) or len(row["content"]) > 1500):
            raise ValueError("Historique invalide.")
        clean.append({"role": row["role"], "content": row["content"]})
    return question.strip(), clean, payload.get("remember") is True


def infer(worker, question, history, remember):
    model = MODELS[worker.model_id]
    context = ("Ton souvenir personnel : tu as reçu hier une livraison de bois apportée par ce voyageur."
               if remember else "Première rencontre. Aucun souvenir de ce personnage.")
    payload = {"model": model["ollamaModel"], "messages": [{"role": "system", "content": SYSTEM +
        "\nSituation connue : " + context}] + history + [{"role": "user", "content": question}],
        "format": SCHEMA, "stream": True, "keep_alive": -1,
        "options": {"num_gpu": 0, "num_thread": worker.threads, "num_ctx": 2048,
                    "num_predict": 160, "temperature": 0.1, "top_k": 50, "repeat_penalty": 1.05}}
    if model.get("think") is False:
        payload["think"] = False
    request = urllib.request.Request(f"http://127.0.0.1:{worker.port}/api/chat",
        json.dumps(payload).encode("utf-8"), {"Content-Type": "application/json"})
    start, first, chunks, final = time.perf_counter(), None, [], {}
    with LOCAL_HTTP.open(request, timeout=90) as response:
        for raw in response:
            row = json.loads(raw)
            if "error" in row:
                raise RuntimeError("Model inference failed")
            content = row.get("message", {}).get("content", "")
            if content:
                first = first if first is not None else time.perf_counter() - start
                chunks.append(content)
            if row.get("done"):
                final = row
    reply = json.loads("".join(chunks))
    if (not isinstance(reply, dict) or set(reply) != {"reply"}
            or not isinstance(reply["reply"], str) or not reply["reply"].strip()
            or final.get("done_reason") == "length"):
        raise RuntimeError("Model produced an incomplete response")
    WorkerPool._check_cpu(worker)
    elapsed = time.perf_counter() - start
    decode = final.get("eval_duration", 0) / 1e9
    return {"model_id": worker.model_id, "model": model["label"], "reply": reply["reply"], "choices": BUTTONS,
            "worker_id": worker.worker_id, "format_valid": True,
            "metrics": {"wall_seconds": round(elapsed, 4), "first_token_seconds": first,
                        "decode_tokens_per_second": round(final.get("eval_count", 0) / decode, 2)
                        if decode else None}}


class DemoHandler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def respond(self, status, value, mime="application/json; charset=utf-8"):
        body = value if isinstance(value, bytes) else json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'unsafe-inline'; "
                         "style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'")
        self.end_headers()
        with contextlib.suppress(BrokenPipeError, ConnectionResetError):
            self.wfile.write(body)

    def valid_local_request(self):
        port = self.server.server_port
        if self.headers.get("Host") not in (f"127.0.0.1:{port}", f"localhost:{port}"):
            return False
        origin = self.headers.get("Origin")
        return not origin or origin in (f"http://127.0.0.1:{port}", f"http://localhost:{port}")

    def do_GET(self):
        if not self.valid_local_request():
            return self.respond(403, {"error": "Hôte ou origine refusé."})
        path = urllib.parse.urlsplit(self.path).path
        if path in ("/", "/index.html"):
            return self.respond(200, (pathlib.Path(__file__).parent / "index.html").read_bytes(),
                                "text/html; charset=utf-8")
        if path == "/api/status":
            return self.respond(200, self.server.agent.public_status())
        return self.respond(404, {"error": "Page introuvable."})

    def do_POST(self):
        if not self.valid_local_request():
            return self.respond(403, {"error": "Hôte ou origine refusé."})
        if self.path != "/api/chat":
            return self.respond(404, {"error": "Page introuvable."})
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 16384:
                raise ValueError("Requête trop longue.")
            self.connection.settimeout(15)
            question, history, remember = validate_chat(json.loads(self.rfile.read(length)))
        except (ValueError, TypeError, TimeoutError):
            return self.respond(400, {"error": "Question ou historique invalide."})
        try:
            queued = time.perf_counter()
            with self.server.agent.pool.acquire() as worker:
                queue_seconds = time.perf_counter() - queued
                result = infer(worker, question, history, remember)
                result["metrics"]["queue_seconds"] = round(queue_seconds, 4)
            return self.respond(200, result)
        except PoolBusy as error:
            return self.respond(429, {"error": str(error)})
        except Exception as error:
            logging.warning("Dialogue inference: %s", safe_error(error))
            return self.respond(503, {"error": "Le moteur local est indisponible ou sa réponse est incomplète."})


class Agent:
    def __init__(self, config, config_path):
        self.config = config
        self.config_path = pathlib.Path(config_path)
        self.state_directory = self.config_path.parent
        self.stop_event = threading.Event()
        self.pool = WorkerPool(config, self.stop_event)
        self.revision = 0
        self.applied_revision = 0
        self.last_error = None
        self.last_heartbeat = None
        self.token = None
        self.desired = {}
        if config.get("backendUrl"):
            self.config["backendUrl"] = validate_backend(config["backendUrl"], config.get("allowLoopbackHttp", False))
            self.token = unprotect_token(config["tokenFile"])
            cache = self.state_directory / "desired.json"
            if cache.exists():
                previous = json.loads(cache.read_text(encoding="utf-8"))
                if previous.get("agentId") == config["agentId"]:
                    self.desired = validate_desired(previous["workers"])
                    self.revision = previous["revision"]
        else:
            self.desired = validate_desired([{"workerId": f"local-{n + 1}",
                "threads": config["threadsPerWorker"], "modelId": config.get("modelId", DEFAULT_MODEL_ID)}
                for n in range(config["initialWorkers"])])

    def heartbeat(self):
        payload = {"agentId": self.config["agentId"], "machineName": socket.gethostname(),
                   "version": VERSION, "logicalProcessors": os.cpu_count() or 1,
                   "totalMemoryMb": total_memory_mb(), "workers": self.pool.snapshot(),
                   "demoPort": self.config["demoPort"], "lastError": self.last_error}
        payload["appliedRevision"] = self.applied_revision
        response = post_json(self.config["backendUrl"] + "/agent/inference/heartbeat", payload, self.token)
        if response.get("agentId") != self.config["agentId"] or type(response.get("revision")) is not int:
            raise ValueError("Heartbeat identity or revision mismatch")
        desired = validate_desired(response.get("workers"))
        self.desired, self.revision = desired, response["revision"]
        atomic_json(self.state_directory / "desired.json", {"agentId": self.config["agentId"],
            "revision": self.revision, "workers": response["workers"]})
        self.last_error = None
        self.last_heartbeat = time.time()
        return max(2, min(30, int(response.get("heartbeatSeconds", 5))))

    def public_status(self):
        workers = self.pool.snapshot()
        return {"agent_id": self.config["agentId"], "model": "Pool CPU · allocation au premier libre",
                "models": [{"id": key, "label": row["label"]} for key, row in MODELS.items()],
                "cpu_only": True, "workers": workers, "waiting_requests": self.pool.waiters,
                "ready_workers": sum(w["state"] == "ready" for w in workers),
                "revision": self.revision, "last_heartbeat": self.last_heartbeat,
                "applied_revision": self.applied_revision,
                "last_error": self.last_error, "registered": bool(self.config.get("backendUrl"))}

    def run(self):
        server = ThreadingHTTPServer(("127.0.0.1", self.config["demoPort"]), DemoHandler)
        server.daemon_threads = True
        server.agent = self
        serving = threading.Thread(target=server.serve_forever, daemon=True)
        serving.start()
        logging.info("Demo listening on http://127.0.0.1:%s", self.config["demoPort"])
        next_heartbeat = 0
        try:
            while not self.stop_event.wait(0.5):
                if (self.state_directory / "stop.request").exists():
                    break
                if self.token and time.monotonic() >= next_heartbeat:
                    delay = 5
                    try:
                        delay = self.heartbeat()
                    except Exception as error:
                        self.last_error = "Control plane: " + safe_error(error)
                        logging.warning("%s", self.last_error)
                    next_heartbeat = time.monotonic() + delay
                self.pool.reconcile(self.desired)
                self.applied_revision = self.revision
                atomic_json(self.state_directory / "status.json", self.public_status())
        finally:
            server.shutdown()
            server.server_close()
            self.pool.close()
            if self.token:
                with contextlib.suppress(Exception):
                    self.heartbeat()
            atomic_json(self.state_directory / "status.json", {"agent_id": self.config["agentId"],
                        "stopped": True, "workers": []})


def load_config(path):
    config = json.loads(pathlib.Path(path).read_text(encoding="utf-8-sig"))
    for key in ("agentId", "ollamaExecutable", "modelCache", "logsDirectory"):
        if not isinstance(config.get(key), str) or not config[key]:
            raise ValueError(f"Missing {key}")
    if not AGENT_ID.fullmatch(config["agentId"]):
        raise ValueError("Invalid agent ID")
    for key, default, lower, upper in (("demoPort", 8767, 1024, 65535),
                                     ("baseWorkerPort", 11441, 1024, 65524),
                                     ("initialWorkers", 3, 0, MAX_WORKERS),
                                     ("threadsPerWorker", 4, 1, 24)):
        config.setdefault(key, default)
        if type(config[key]) is not int or not lower <= config[key] <= upper:
            raise ValueError(f"Invalid {key}")
    if config["baseWorkerPort"] <= config["demoPort"] < config["baseWorkerPort"] + MAX_WORKERS:
        raise ValueError("Demo port conflicts with worker range")
    pathlib.Path(config["modelCache"]).mkdir(parents=True, exist_ok=True)
    pathlib.Path(config["logsDirectory"]).mkdir(parents=True, exist_ok=True)
    return config


@contextlib.contextmanager
def install_lock(directory):
    path = directory / "agent.lock"
    stream = path.open("a+b")
    stream.seek(0)
    stream.write(b"0")
    stream.flush()
    stream.seek(0)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        stream.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    args = parser.parse_args()
    config_path = pathlib.Path(args.config).resolve()
    config = load_config(config_path)
    from logging.handlers import RotatingFileHandler
    handler = RotatingFileHandler(pathlib.Path(config["logsDirectory"]) / "agent.log",
                                  maxBytes=2_000_000, backupCount=3, encoding="utf-8")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s", handlers=[handler])
    with install_lock(config_path.parent):
        identity = {"pid": os.getpid(), "executable": str(pathlib.Path(sys.executable).resolve()),
                    "script": str(pathlib.Path(__file__).resolve()), "config": str(config_path),
                    "creationFileTime": creation_filetime()}
        atomic_json(config_path.parent / "agent.pid.json", identity)
        agent = None
        try:
            agent = Agent(config, config_path)
            signal.signal(signal.SIGTERM, lambda *_: agent.stop_event.set())
            signal.signal(signal.SIGINT, lambda *_: agent.stop_event.set())
            agent.run()
        except Exception as error:
            logging.error("Agent stopped: %s", safe_error(error))
            raise SystemExit(1)
        finally:
            if agent:
                agent.pool.close()
            (config_path.parent / "agent.pid.json").unlink(missing_ok=True)


if __name__ == "__main__":
    main()

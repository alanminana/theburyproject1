#!/usr/bin/env python3
"""Opt-in destructive fault tests ONLY against a freshly named QA Compose project.

Uses existing images; never builds, prunes, or removes volumes. Stops its own stack
in finally. Generated test credentials stay in ignored artifacts/monitoring/.
"""
import json
import os
from pathlib import Path
import secrets
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import monitor

ROOT = Path(__file__).resolve().parents[2]


def main():
    project = "bury-monitor-qa-" + secrets.token_hex(3)
    artifact = ROOT / "artifacts" / "monitoring" / project
    artifact.mkdir(parents=True)
    cfg = json.loads(Path(__file__).with_name("config.example.json").read_text())
    cfg.update(project=project, base_url="https://localhost:18891", backup_dir=str(artifact / "backups"))
    (artifact / "backups" / "sql").mkdir(parents=True)
    env = artifact / "qa.env"
    password = "Qa!" + secrets.token_hex(18)
    env.write_text("\n".join([
        "MSSQL_SA_PASSWORD=" + password, "ERP_DB_USER=qa_runtime", "ERP_DB_PASSWORD=" + password,
        "ERP_MIGRATION_USER=qa_migrate", "ERP_MIGRATION_PASSWORD=" + password,
        "ADMIN_EMAIL=monitor@example.invalid", "ADMIN_PASSWORD=" + password,
        "ERP_DOMAIN=localhost", "ERP_IMAGE=theburyproject/erp:production-qa", "MSSQL_PID=Express",
        "BURY_NET_SUBNET=172.29.94.0/24", "BACKUP_DIR=" + (artifact / "backups").as_posix()
    ]), encoding="utf-8")
    caddyfile = artifact / "Caddyfile"
    caddyfile.write_text("localhost {\n tls internal\n reverse_proxy app:8080\n}\n", encoding="utf-8")
    overlay = artifact / "overlay.yml"
    overlay.write_text("services:\n  caddy:\n    ports: !override\n      - '127.0.0.1:18891:443'\n    volumes:\n      - " + caddyfile.as_posix() + ":/etc/caddy/Caddyfile:ro\n", encoding="utf-8")
    base = ["docker", "compose", "--env-file", str(env), "-p", project, "-f", str(ROOT / "docker-compose.yml"), "-f", str(overlay)]
    def dc(*args, timeout=240):
        return monitor.command([*base, *args], timeout)
    received = []
    class Receiver(BaseHTTPRequestHandler):
        def do_POST(self):
            received.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
            self.send_response(204)
            self.end_headers()
        def log_message(self, *args):
            pass
    server = ThreadingHTTPServer(("127.0.0.1", 0), Receiver)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    os.environ["BURY_MONITOR_WEBHOOK"] = f"http://127.0.0.1:{server.server_port}"
    store = monitor.Store(artifact / "monitor.sqlite")
    collector = monitor.Collector(cfg, store)
    alerts = monitor.Alerts(store)
    results = []
    elapsed = 10000
    def observe(label, expected):
        nonlocal elapsed
        collector.signals = {}
        collector.containers()
        for name in ("live", "ready"):
            ok = monitor.http_ok(cfg["base_url"] + "/health/" + name)
            collector.emit("http." + name, "INFO" if ok else "CRITICAL", label, 120)
        # Real collection, accelerated incident clock only (holds separately tested).
        for offset in (0, 60, 120):
            alerts.update(collector.signals, elapsed + offset)
        elapsed += 180
        for key, severity in expected.items():
            assert collector.signals[key]["severity"] == severity, (label, key, collector.signals[key])
        results.append({"case": label, "signals": expected})
        print(label + ": OK", flush=True)
    def wait_ready():
        deadline = time.monotonic() + 150
        while time.monotonic() < deadline:
            if monitor.http_ok(cfg["base_url"] + "/health/ready"):
                return
            time.sleep(3)
        raise RuntimeError("QA readiness timeout")
    try:
        print("Starting isolated project " + project, flush=True)
        dc("up", "-d", "--no-build", timeout=300)
        cid = dc("ps", "-q", "caddy")
        cert = artifact / "qa-root.crt"
        monitor.command(["docker", "cp", cid + ":/data/caddy/pki/authorities/local/root.crt", str(cert)])
        os.environ["SSL_CERT_FILE"] = str(cert)
        wait_ready()
        observe("healthy", {"http.live": "INFO", "http.ready": "INFO"})
        days = monitor.tls_days(cfg["base_url"])
        assert days > 0
        collector.sql()
        collector.resources()
        results.append({"case": "SQL query, CPU/RAM and trusted local TLS", "certificate_days": days,
                        "signals": collector.signals})
        dc("stop", "-t", "15", "db")
        observe("SQL stopped; live up ready down", {"docker.db": "CRITICAL", "http.live": "INFO", "http.ready": "CRITICAL"})
        dc("start", "db")
        wait_ready()
        observe("SQL recovery", {"http.ready": "INFO"})
        assert any(e["key"] == "http.ready" and e["status"] == "resolved" for e in received)
        dc("stop", "-t", "10", "app")
        observe("app stopped", {"docker.app": "CRITICAL", "http.live": "CRITICAL"})
        dc("start", "app")
        wait_ready()
        observe("app recovery", {"http.live": "INFO", "http.ready": "INFO"})
        dc("stop", "caddy")
        observe("Caddy stopped", {"docker.caddy": "CRITICAL", "http.live": "CRITICAL"})
        dc("start", "caddy")
        wait_ready()
        observe("Caddy recovery", {"http.live": "INFO", "http.ready": "INFO"})
        # Repeated identical samples must not produce an alert storm.
        before = len(received)
        observe("stable repeated samples", {"http.ready": "INFO"})
        assert len(received) == before
        (artifact / "report.json").write_text(json.dumps({"project": project, "tests": results, "alerts": received}, indent=2))
        print(f"Evidence: {artifact / 'report.json'}; notifications={len(received)}", flush=True)
    finally:
        dc("down", "--timeout", "15", timeout=90)
        server.shutdown()
        server.server_close()
        print("QA containers/network removed; volumes retained: " + project, flush=True)


if __name__ == "__main__":
    main()

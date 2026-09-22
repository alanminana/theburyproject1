#!/usr/bin/env python3
"""Linux host collector. Standard library only; never reads Compose secrets.

Docker access is administrative. Queries use the existing in-container SQL identity.
No workload changes, cleanup, or automatic remediation are performed.
"""
import argparse
import concurrent.futures
from contextlib import contextmanager
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import sqlite3
import ssl
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import uuid

GIB = 1024 ** 3
SERVICES = ("app", "db", "caddy")


def command(args, timeout=20):
    p = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if p.returncode:
        # stderr can include credentials from external tools: never forward it.
        raise RuntimeError("command failed: " + args[0])
    return p.stdout.strip()


def http_ok(url, expected_body=None):
    try:
        with urllib.request.urlopen(url, timeout=8) as response:
            if response.status != 200:
                return False
            return expected_body is None or (response.url == url and response.read(128).decode().strip() == expected_body)
    except Exception:
        return False


def tls_days(url):
    target = urllib.parse.urlsplit(url)
    if target.scheme != "https":
        raise ValueError("HTTPS required")
    with socket.create_connection((target.hostname, target.port or 443), timeout=8) as sock:
        with ssl.create_default_context().wrap_socket(sock, server_hostname=target.hostname) as conn:
            expires = ssl.cert_time_to_seconds(conn.getpeercert()["notAfter"])
    return (expires - time.time()) / 86400


class Store:
    def __init__(self, path):
        self.path = str(path)
        with self.connect() as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE IF NOT EXISTS history (ts REAL, kind TEXT, data TEXT);
                CREATE TABLE IF NOT EXISTS oom (service TEXT PRIMARY KEY, ts REAL);
            """)

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=10)
        try:
            with db:
                yield db
        finally:
            db.close()

    def get(self, key, default=None):
        with self.connect() as db:
            row = db.execute("SELECT value FROM state WHERE key=?", (key,)).fetchone()
        return json.loads(row[0]) if row else default

    def put(self, key, value):
        with self.connect() as db:
            db.execute("INSERT OR REPLACE INTO state VALUES (?,?)", (key, json.dumps(value)))

    def record(self, kind, value, now):
        with self.connect() as db:
            db.execute("INSERT INTO history VALUES (?,?,?)", (now, kind, json.dumps(value)))

    def latch_oom(self, service, now):
        with self.connect() as db:
            db.execute("INSERT OR REPLACE INTO oom VALUES (?,?)", (service, now))

    def ooms(self):
        with self.connect() as db:
            return dict(db.execute("SELECT service,ts FROM oom"))

    def prune(self, days, now):
        with self.connect() as db:
            db.execute("DELETE FROM history WHERE ts < ?", (now - days * 86400,))


def send(event):
    url = os.environ.get("BURY_MONITOR_WEBHOOK", "")
    if not url:
        return False
    headers = {"Content-Type": "application/json"}
    token = os.environ.get("BURY_MONITOR_TOKEN", "")
    if token:
        headers["Authorization"] = "Bearer " + token
    try:
        req = urllib.request.Request(url, json.dumps(event).encode(), headers=headers)
        with urllib.request.urlopen(req, timeout=8) as response:
            return 200 <= response.status < 300
    except Exception:
        return False


class Alerts:
    def __init__(self, store, cooldown=3600, deliver=send):
        self.store, self.cooldown, self.deliver = store, cooldown, deliver

    def update(self, signals, now):
        """Missing samples never resolve incidents; holds reset after a sampling gap."""
        for key, signal in signals.items():
            state = self.store.get("alert:" + key, {"active": "INFO", "since": {}})
            severity, hold = signal["severity"], signal.get("hold", 0)
            if now - state.get("sample", now) > 180:
                state["since"] = {}
            state["sample"] = now
            rank = {"INFO": 0, "WARNING": 1, "CRITICAL": 2}
            for level in ("WARNING", "CRITICAL"):
                if rank[severity] >= rank[level]:
                    state["since"].setdefault(level, now)
                else:
                    state["since"].pop(level, None)
            eligible = severity == "INFO" or now - state["since"][severity] >= hold
            changed = eligible and severity != state["active"]
            repeat = eligible and severity != "INFO" and not state.get("pending") and now - state.get("emitted", 0) >= self.cooldown
            if changed or repeat:
                event = {"event_id": str(uuid.uuid4()), "key": key, "severity": severity,
                         "status": "resolved" if severity == "INFO" else "firing",
                         "time": now, "detail": signal["detail"]}
                state.update(active=severity, emitted=now)
                # Preserve firing + recovery during receiver outages. Bound queue growth.
                pending = state.setdefault("pending", [])
                if len(pending) >= 100:
                    pending.pop(1)  # preserve first incident and latest transitions; history retains all
                pending.append(event)
                self.store.record("alert", event, now)
            self.store.put("alert:" + key, state)
        with self.store.connect() as db:
            states = list(db.execute("SELECT key,value FROM state WHERE key LIKE 'alert:%'"))
        deliveries = 0
        failed = False
        for key, raw in states:
            state = json.loads(raw)
            if deliveries < 10 and state.get("pending") and now - state.get("retry", 0) >= 60:
                deliveries += 1
                state["retry"] = now
                self.store.put(key, state)
                if self.deliver(state["pending"][0]):
                    state["pending"].pop(0)
                else:
                    failed = True
                self.store.put(key, state)
        self.store.put("delivery.failed", failed)


class Collector:
    def __init__(self, config, store):
        self.cfg, self.store = config, store
        self.signals = {}

    def emit(self, key, severity, detail, hold=0):
        self.signals[key] = dict(severity=severity, detail=detail, hold=hold)

    def threshold(self, key, value, warning, critical, hold=0):
        self.emit(key, "CRITICAL" if value >= critical else "WARNING" if value >= warning else "INFO",
                  f"value={value:.2f}; warning={warning}; critical={critical}", hold)

    def safe(self, key, fn):
        try:
            fn()
            self.emit("collector." + key, "INFO", "sample OK")
        except Exception as exc:
            self.emit("collector." + key, "CRITICAL", "sample unavailable: " + type(exc).__name__)

    def containers(self):
        ids = command(["docker", "ps", "-aq", "--filter", "label=com.docker.compose.project=" + self.cfg["project"]]).split()
        # Select fields only: full inspect exposes Config.Env (credentials).
        template = ('{"id":{{json .Id}},"state":{{json .State.Status}},'
                    '"oom":{{json .State.OOMKilled}},"exit":{{json .State.ExitCode}},'
                    '"health":{{with (index .State "Health")}}{{json .Status}}{{else}}null{{end}},'
                    '"service":{{json (index .Config.Labels "com.docker.compose.service")}},'
                    '"restart":{{.RestartCount}},"cpus":{{.HostConfig.NanoCpus}},'
                    '"memory":{{.HostConfig.Memory}}}')
        rows = command(["docker", "inspect", "--format", template, *ids]) if ids else ""
        self.containers_by_service = {}
        for row in rows.splitlines():
            item = json.loads(row)
            if item["service"] in (*SERVICES, "migrate", "db-init"):
                self.containers_by_service[item["service"]] = item
        for svc in SERVICES:
            c = self.containers_by_service.get(svc, {})
            good = c.get("state") == "running" and c.get("health") not in ("unhealthy", "starting")
            self.emit("docker." + svc, "INFO" if good else "CRITICAL", f"{c.get('state', 'missing')}; health={c.get('health')}", 120)
            if not c:
                continue
            previous = self.store.get("container:" + svc, {})
            increased = c["restart"] > previous.get("restart", 0) if previous.get("id") == c["id"] else c["restart"] > 0
            last_restart = time.time() if increased else previous.get("last_restart", 0)
            restarts = [r for r in previous.get("restarts", []) if time.time() - r < 600]
            if increased:
                delta = c["restart"] - previous.get("restart", 0) if previous.get("id") == c["id"] else c["restart"]
                restarts.extend([time.time()] * min(delta, 100))
            self.emit("restart." + svc, "CRITICAL" if len(restarts) >= 3 else "WARNING" if time.time() - last_restart < 600 else "INFO",
                      f"RestartCount={c['restart']}; last increase={last_restart}")
            if c["oom"] and not (previous.get("id") == c["id"] and previous.get("oom")):
                self.store.latch_oom(svc, time.time())
            self.store.put("container:" + svc, {**c, "last_restart": last_restart, "restarts": restarts})
        for svc in ("migrate", "db-init"):
            c = self.containers_by_service.get(svc)
            if c:
                bad = c["state"] in ("exited", "dead") and c["exit"] != 0
                self.emit("deploy." + svc, "CRITICAL" if bad else "INFO", f"state={c['state']}; exit={c['exit']}")

    def resources(self):
        for svc, c in self.containers_by_service.items():
            if c["state"] in ("exited", "dead", "created"):
                self.emit("cpu." + svc, "INFO", "container not running")
                self.emit("ram." + svc, "INFO", "container not running")
        running = {c["id"]: svc for svc, c in self.containers_by_service.items() if c["state"] == "running"}
        if not running:
            return
        rows = command(["docker", "stats", "--no-stream", "--no-trunc", "--format", "{{json .}}", *running], 25)
        for line in rows.splitlines():
            row = json.loads(line)
            svc = running[row["ID"]]
            c = self.containers_by_service[svc]
            if not c["cpus"] or not c["memory"]:
                if svc != "db-init":
                    self.emit("limits." + svc, "WARNING", "CPU or RAM limit absent")
                continue
            self.emit("limits." + svc, "INFO", "limits present")
            cpu = float(row["CPUPerc"].rstrip("%")) / (c["cpus"] / 1e9)
            ram = float(row["MemPerc"].rstrip("%"))
            self.threshold("cpu." + svc, cpu, 80, 95, 300)
            self.threshold("ram." + svc, ram, 80 if svc != "db" else 85, 95, 180)

    def host_resources(self):
        cpu = list(map(int, Path("/proc/stat").read_text().splitlines()[0].split()[1:9]))
        current = [sum(cpu), cpu[3] + cpu[4]]
        previous = self.store.get("host.cpu")
        if previous and current[0] > previous[0]:
            busy = 100 * (1 - (current[1] - previous[1]) / (current[0] - previous[0]))
            self.threshold("cpu.host", busy, 80, 95, 300)
        self.store.put("host.cpu", current)
        mem = {line.split(":")[0]: int(line.split()[1]) for line in Path("/proc/meminfo").read_text().splitlines()}
        self.threshold("ram.host", 100 * (1 - mem["MemAvailable"] / mem["MemTotal"]), 80, 95, 180)

    def disks(self):
        root = command(["docker", "info", "--format", "{{.DockerRootDir}}"])
        volumes = command(["docker", "volume", "ls", "-q", "--filter", "label=com.docker.compose.project=" + self.cfg["project"]]).splitlines()
        mounts = []
        if volumes:
            mounts = command(["docker", "volume", "inspect", "--format", "{{.Mountpoint}}", *volumes]).splitlines()
        paths = list(dict.fromkeys([*self.cfg["disk_paths"], root, self.cfg["backup_dir"], *mounts]))
        for path in paths:
            def check(path=path):
                usage = shutil.disk_usage(path)
                pct, free = usage.used / usage.total * 100, usage.free / GIB
                severity = "CRITICAL" if pct >= 90 or free < self.cfg["disk_free_critical_gib"] else "WARNING" if pct >= 70 or free < self.cfg["disk_free_warning_gib"] else "INFO"
                self.emit("disk." + path, severity, f"used={pct:.1f}%; free={free:.1f} GiB; action={pct >= 85}")
                self.emit("disk.action." + path, "WARNING" if pct >= 85 else "INFO",
                          f"action threshold 85%; used={pct:.1f}%; do not wait for 90%")
            self.safe("disk." + path, check)
        if self.slow:
            for path in [self.cfg["backup_dir"], *mounts]:
                def measure(path=path):
                    gib = int(command(["du", "-sx", "--block-size=1", path], 60).split()[0]) / GIB
                    self.threshold("volume." + path, gib, self.cfg["volume_warning_gib"], self.cfg["volume_critical_gib"])
                    self.growth("volume." + path, gib)
                self.safe("volume." + path, measure)

    def growth(self, key, gib):
        now = time.time()
        baseline = self.store.get("growth:" + key)
        if baseline and now - baseline[0] >= 3600:
            rate = (gib - baseline[1]) * 86400 / (now - baseline[0])
            self.threshold("growth." + key, rate, self.cfg["growth_warning_gib_day"], self.cfg["growth_warning_gib_day"] * 2)
        if not baseline or now - baseline[0] >= 86400:
            self.store.put("growth:" + key, [now, gib])

    def sql(self):
        dbname = self.cfg["database"]
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,63}", dbname):
            raise ValueError("database identifier")
        cid = self.containers_by_service["db"]["id"]
        query = (f"SET NOCOUNT ON; USE [{dbname}]; "
                 "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(100)), "
                 "SUM(CASE WHEN type=0 THEN CAST(size AS bigint) ELSE 0 END)*8.0/1024/1024, "
                 "SUM(CASE WHEN type=1 THEN CAST(size AS bigint) ELSE 0 END)*8.0/1024/1024 "
                 "FROM sys.database_files;")
        shell = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -b -l 5 -t 10 -S localhost -U sa -h -1 -W -s "|" -Q "$0"'
        output = command(["docker", "exec", cid, "bash", "-c", shell, query], 20)
        row = next(line for line in output.splitlines() if "|" in line).split("|")
        edition, data, log = row[0], float(row[1]), float(row[2])
        self.emit("sql.connection", "INFO", "query OK")
        if "Express" in edition:
            self.threshold("sql.express", data / 10 * 100, 70, 85)
        self.threshold("sql.log", log, self.cfg["sql_log_warning_gib"], self.cfg["sql_log_critical_gib"])
        self.emit("sql.data", "INFO", f"allocated={data:.3f} GiB; log={log:.3f} GiB; edition={edition}")
        self.growth("sql.data", data)
        self.growth("sql.log", log)

    def backups(self):
        base = Path(self.cfg["backup_dir"]) / "monitor-status"
        now = time.time()
        for kind, windows in self.cfg["backup_windows"].items():
            try:
                status = json.loads((base / (kind + ".json")).read_text())
                age = now - float(status["time"])
                if age < -60 or status["exit"] != 0 or status.get("verified") is not True:
                    raise ValueError("unverified or future status")
                self.threshold("backup." + kind, age, *windows)
            except Exception:
                self.emit("backup." + kind, "CRITICAL", "verified success missing/failed/invalid")
        for mode in ("all", "full", "log", "files"):
            path = base / ("run-" + mode + ".json")
            if path.exists():
                def check(path=path, mode=mode):
                    status = json.loads(path.read_text())
                    self.emit("backup.run-" + mode, "INFO" if status["exit"] == 0 else "CRITICAL", f"exit={status['exit']}; at={status['time']}")
                self.safe("backup.run-" + mode, check)

    def collect(self):
        self.signals = {}
        now = time.time()
        self.slow = now - self.store.get("slow", 0) >= self.cfg["slow_seconds"]
        base = self.cfg["base_url"].rstrip("/")
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            results = list(pool.map(lambda path: http_ok(base + path, None if path == "/" else "Healthy"),
                                    ["/", "/health/live", "/health/ready"]))
        for key, ok in zip(("http.erp", "http.live", "http.ready"), results):
            self.emit(key, "INFO" if ok else "CRITICAL", f"HTTP 200={ok}; live={results[1]}; ready={results[2]}", 120)
        self.containers_by_service = {}
        self.safe("docker", self.containers)
        self.safe("resources", self.resources)
        self.safe("host", self.host_resources)
        self.safe("disks", self.disks)
        self.safe("backups", self.backups)
        if self.slow:
            self.safe("sql", self.sql)
            self.safe("tls", lambda: self.threshold("tls.expiry", -tls_days(base), -21, -7))
            self.store.put("slow", now)
        ooms = self.store.ooms()
        for svc in SERVICES:
            self.emit("oom." + svc, "CRITICAL" if svc in ooms else "INFO", f"latched OOM={ooms.get(svc)}; requires acknowledgement")
        self.emit("collector.events", "INFO" if self.store.get("events.connected", False) else "CRITICAL", "Docker OOM event stream")
        self.emit("collector.channel", "INFO" if os.environ.get("BURY_MONITOR_WEBHOOK") else "WARNING", "webhook configured=" + str(bool(os.environ.get("BURY_MONITOR_WEBHOOK"))))
        return self.signals


def event_stream(config, store, stop, processes):
    """Stream OOMs persistently; inspect alone loses OOM evidence after restart."""
    while not stop.is_set():
        store.put("events.connected", False)
        since = str(store.get("events.cursor", int(time.time()) - 60))
        args = ["docker", "events", "--since", since, "--filter", "type=container", "--filter", "event=oom",
                "--filter", "label=com.docker.compose.project=" + config["project"], "--format", "{{json .}}"]
        try:
            with subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True) as proc:
                processes[:] = [proc]
                store.put("events.connected", True)
                for line in proc.stdout:
                    event = json.loads(line)
                    timestamp = event.get("timeNano", int(event.get("time", time.time()) * 1e9))
                    svc = event.get("Actor", {}).get("Attributes", {}).get("com.docker.compose.service")
                    if svc in SERVICES and timestamp > store.get("events.nano", 0):
                        store.latch_oom(svc, timestamp / 1e9)
                        store.record("oom", {"service": svc, "time": timestamp / 1e9}, time.time())
                    store.put("events.nano", timestamp)
                    store.put("events.cursor", timestamp // 1000000000)
                    if stop.is_set():
                        proc.terminate()
                        break
        except Exception:
            pass
        store.put("events.connected", False)
        stop.wait(10)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--ack-oom", choices=SERVICES)
    args = parser.parse_args()
    cfg = json.loads(Path(args.config).read_text(encoding="utf-8"))
    path = Path(cfg["state_dir"])
    path.mkdir(parents=True, exist_ok=True)
    store = Store(path / "monitor.sqlite")
    if args.ack_oom:
        with store.connect() as db:
            db.execute("DELETE FROM oom WHERE service=?", (args.ack_oom,))
        store.record("ack", {"service": args.ack_oom}, time.time())
        return
    # Linux process lock prevents two agents from advancing the incident state.
    import fcntl
    lock = (path / "agent.lock").open("w")
    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    stop = threading.Event()
    processes = []
    signal.signal(signal.SIGTERM, lambda *_: stop.set())
    signal.signal(signal.SIGINT, lambda *_: stop.set())
    thread = threading.Thread(target=event_stream, args=(cfg, store, stop, processes), daemon=True)
    thread.start()
    try:
        run_loop(cfg, store, stop, args.once)
    finally:
        stop.set()
        for proc in processes:
            if proc.poll() is None:
                proc.terminate()
                proc.wait(timeout=5)
        thread.join(timeout=5)
        lock.close()


def run_loop(cfg, store, stop, once):
    collector, alerts = Collector(cfg, store), Alerts(store, cfg["cooldown_seconds"])
    while not stop.is_set():
        start = time.monotonic()
        signals = collector.collect()
        now = time.time()
        alerts.update(signals, now)
        store.record("sample", signals, now)
        store.prune(cfg["retention_days"], now)
        store.put("last_sample", now)
        # Only a completed collection can heartbeat. Separate Kuma alerts cover host/agent loss.
        heartbeat = os.environ.get("BURY_MONITOR_HEARTBEAT")
        if heartbeat:
            # Push also reports delivery/collection trouble; URL and token never enter logs.
            parts = urllib.parse.urlsplit(heartbeat)
            query = dict(urllib.parse.parse_qsl(parts.query))
            unhealthy = store.get("delivery.failed", False) or any(
                key.startswith("collector.") and value["severity"] != "INFO" for key, value in signals.items())
            query.update(status="down" if unhealthy else "up", msg="collector requires attention" if unhealthy else "collection completed")
            http_ok(urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(query))))
        print(json.dumps({"at": dt.datetime.now(dt.timezone.utc).isoformat(), "signals": len(signals),
                          "seconds": round(time.monotonic() - start, 2)}), flush=True)
        if once:
            break
        stop.wait(max(1, cfg["interval_seconds"] - (time.monotonic() - start)))



if __name__ == "__main__":
    main()

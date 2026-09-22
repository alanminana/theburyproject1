"""Safe fault injection; no Docker mutations or real disks filled."""
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch, MagicMock
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import monitor


class MonitoringTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store = monitor.Store(Path(self.tmp.name) / "state.sqlite")
        self.events = []
        self.alerts = monitor.Alerts(self.store, deliver=lambda e: self.events.append(e) or True)
        self.cfg = json.loads(Path(__file__).with_name("config.example.json").read_text())
        self.cfg["backup_dir"] = self.tmp.name
        self.collector = monitor.Collector(self.cfg, self.store)

    def sample(self, severity, now, hold=120, key="http.ready"):
        self.alerts.update({key: dict(severity=severity, detail="injected", hold=hold)}, now)

    def test_down_resolved_dedup_and_persistence(self):
        for now in range(1000, 1600, 30):
            self.sample("CRITICAL", now)
        self.assertEqual(len(self.events), 1)
        self.alerts = monitor.Alerts(self.store, deliver=lambda e: self.events.append(e) or True)
        self.sample("CRITICAL", 1600)
        self.sample("INFO", 1630)
        self.assertEqual([e["status"] for e in self.events], ["firing", "resolved"])

    def test_spikes_gaps_and_sustained_cpu(self):
        self.sample("WARNING", 1000, 300, "cpu.app")
        self.sample("INFO", 1060, 300, "cpu.app")
        self.sample("WARNING", 2000, 300, "cpu.app")
        self.sample("WARNING", 2400, 300, "cpu.app")
        self.assertEqual(self.events, [])
        for now in range(2460, 2761, 60):
            self.sample("WARNING", now, 300, "cpu.app")
        self.assertEqual(len(self.events), 1)

    def test_missing_sample_never_resolves(self):
        self.sample("CRITICAL", 1000, 0)
        self.alerts.update({}, 1060)
        self.assertEqual(self.store.get("alert:http.ready")["active"], "CRITICAL")

    def test_transport_outage_preserves_firing_and_recovery(self):
        self.alerts.deliver = lambda e: False
        self.sample("CRITICAL", 1000, 0)
        self.sample("INFO", 1060, 0)
        self.alerts.deliver = lambda e: self.events.append(e) or True
        self.alerts.update({}, 1120)
        self.alerts.update({}, 1180)
        self.assertEqual([e["status"] for e in self.events], ["firing", "resolved"])

    def test_webhook_local_receiver(self):
        received = []
        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                received.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                self.send_response(204)
                self.end_headers()
            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with patch.dict(os.environ, {"BURY_MONITOR_WEBHOOK": f"http://127.0.0.1:{server.server_port}"}):
                self.assertTrue(monitor.send({"event_id": "local-test", "severity": "CRITICAL"}))
            self.assertEqual(received[0]["event_id"], "local-test")
        finally:
            server.shutdown()
            server.server_close()

    def test_backup_age_failure_and_future_timestamp(self):
        directory = Path(self.tmp.name) / "monitor-status"
        directory.mkdir()
        now = monitor.time.time()
        for kind in self.cfg["backup_windows"]:
            (directory / (kind + ".json")).write_text(json.dumps(dict(time=now, exit=0, verified=True)))
        self.collector.backups()
        self.assertTrue(all(v["severity"] == "INFO" for v in self.collector.signals.values()))
        for kind, status in {"full": dict(time=now-110000, exit=0, verified=True),
                             "log": dict(time=now+1000, exit=0, verified=True),
                             "offsite-sql": dict(time=now, exit=6, verified=False)}.items():
            (directory / (kind + ".json")).write_text(json.dumps(status))
        self.collector.backups()
        for key in ("full", "log", "offsite-sql"):
            self.assertEqual(self.collector.signals["backup." + key]["severity"], "CRITICAL")

    def test_disk_free_space_even_when_percentage_low(self):
        usage = monitor.shutil._ntuple_diskusage(100*monitor.GIB, 60*monitor.GIB, 5*monitor.GIB)
        self.collector.slow = False
        with patch.object(monitor, "command", side_effect=["/docker", ""]), patch.object(monitor.shutil, "disk_usage", return_value=usage):
            self.collector.disks()
        self.assertEqual(self.collector.signals["disk./"]["severity"], "CRITICAL")

    def test_restart_oom_and_migration(self):
        rows = [dict(id=svc, service=svc, state="running", health="healthy", oom=False,
                     exit=0, restart=0, cpus=1000000000, memory=1000000) for svc in monitor.SERVICES]
        def sample(data):
            with patch.object(monitor, "command", side_effect=["app db caddy", "\n".join(json.dumps(x) for x in data)]):
                self.collector.containers()
        sample(rows)
        rows[0].update(restart=3, oom=True)
        rows.append(dict(id="migrate", service="migrate", state="exited", exit=1))
        sample(rows)
        self.assertEqual(self.collector.signals["restart.app"]["severity"], "CRITICAL")
        self.assertEqual(self.collector.signals["deploy.migrate"]["severity"], "CRITICAL")
        rows[0]["oom"] = False
        sample(rows)
        self.assertIn("app", self.store.ooms())
        self.assertIn("app", monitor.Store(self.store.path).ooms())

    def test_cpu_normalized_to_docker_limit(self):
        self.collector.containers_by_service = {"app": dict(id="abc", state="running", cpus=2000000000, memory=2*monitor.GIB)}
        with patch.object(monitor, "command", return_value=json.dumps(dict(ID="abc", CPUPerc="180%", MemPerc="96%"))):
            self.collector.resources()
        self.assertEqual(self.collector.signals["cpu.app"]["severity"], "WARNING")
        self.assertEqual(self.collector.signals["ram.app"]["severity"], "CRITICAL")

    def test_oom_stream_survives_restart_and_replay(self):
        stop = threading.Event()
        timestamp = 1790000000123456789
        event = json.dumps({"timeNano": timestamp, "Actor": {"Attributes": {"com.docker.compose.service": "app"}}})
        def lines():
            yield event
            stop.set()
        proc = MagicMock()
        proc.__enter__.return_value = proc
        proc.stdout = lines()
        with patch.object(monitor.subprocess, "Popen", return_value=proc):
            monitor.event_stream(self.cfg, self.store, stop, [])
        self.assertIn("app", self.store.ooms())
        self.assertEqual(self.store.get("events.nano"), timestamp)
        with self.store.connect() as db:
            db.execute("DELETE FROM oom WHERE service='app'")
        stop.clear()
        proc.stdout = lines()
        with patch.object(monitor.subprocess, "Popen", return_value=proc):
            monitor.event_stream(self.cfg, self.store, stop, [])
        self.assertNotIn("app", self.store.ooms(), "replay must not undo acknowledgement")

    def test_tls_warning_and_critical(self):
        for days, expected in [(30, "INFO"), (20, "WARNING"), (6, "CRITICAL")]:
            self.collector.threshold("tls.expiry", -days, -21, -7)
            self.assertEqual(self.collector.signals["tls.expiry"]["severity"], expected)

    def test_unhealthy_with_running_process_is_not_healthy(self):
        row = dict(id="app", service="app", state="running", health="unhealthy", oom=False,
                   exit=0, restart=0, cpus=2000000000, memory=2*monitor.GIB)
        with patch.object(monitor, "command", side_effect=["app", json.dumps(row)]):
            self.collector.containers()
        self.assertEqual(self.collector.signals["docker.app"]["severity"], "CRITICAL")

    def test_sql_express_warns_before_limit_and_separates_log(self):
        self.collector.containers_by_service = {"db": {"id": "qa"}}
        for data, expected in [(7.5, "WARNING"), (8.6, "CRITICAL")]:
            with patch.object(monitor, "command", return_value=f"Express Edition|{data}|0.1"):
                self.collector.sql()
            self.assertEqual(self.collector.signals["sql.express"]["severity"], expected)
            self.assertEqual(self.collector.signals["sql.log"]["severity"], "INFO")


if __name__ == "__main__":
    unittest.main()

"""Safe fault injection; no Docker mutations or real disks filled."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
import time
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

    # ----- alimentacion y bateria (sysfs sintetico) -----
    def power_sample(self, now, ac="1", capacity=80, status="Charging", mains=True, battery=True):
        root = Path(self.tmp.name) / "power_supply"
        shutil.rmtree(root, ignore_errors=True)
        entries = {}
        if mains:
            entries["AC"] = {"type": "Mains", "online": ac}
        if battery:
            entries["BAT0"] = {"type": "Battery", "capacity": str(capacity), "status": status}
        entries["hid-mouse"] = {"type": "Battery", "scope": "Device", "capacity": "3", "status": "Discharging"}
        entries["ucsi"] = {"type": "USB", "online": "0"}
        for name, values in entries.items():
            (root / name).mkdir(parents=True)
            for key, value in values.items():
                (root / name / key).write_text(value + "\n")
        self.collector.cfg["power"] = {**self.cfg.get("power", {}), "sysfs": str(root)}
        self.collector.signals = {}
        self.collector.safe("power", self.collector.power)
        self.alerts.update(self.collector.signals, now)
        return self.collector.signals

    def power_events(self):
        return [(e["key"], e["status"], e["severity"]) for e in self.events if e["key"].startswith("power.")]

    def firewall_signal(self, text, mode=True, age=0):
        path = Path(self.tmp.name) / "fw-estado"
        if text is None:
            if path.exists():
                path.unlink()
        else:
            path.write_text(f"latido={int(time.time()) - age} {text}")
        self.cfg["firewall"] = {"enabled": mode, "state_file": str(path), "stale_seconds": 60}
        self.collector.signals = {}
        self.collector.firewall()
        return self.collector.signals.get("firewall.watch")

    def test_firewall_watch_states(self):
        ok = self.firewall_signal("estado=ok ultima_ok=1 incidente=-")
        self.assertEqual(ok["severity"], "INFO")
        self.assertEqual(self.firewall_signal("estado=docker-inactivo ultima_ok=1 incidente=-")["severity"], "INFO")
        for text in ("estado=ok ultima_ok=1 incidente=123", "estado=incidente ultima_ok=1 incidente=123",
                     "estado=detenido ultima_ok=1 incidente=123", "estado=config-invalida ultima_ok=1 incidente=-",
                     "estado=desconocido ultima_ok=1 incidente=-"):
            self.assertEqual(self.firewall_signal(text)["severity"], "CRITICAL", text)
        self.assertEqual(self.firewall_signal("estado=ilegible ultima_ok=1 incidente=-")["severity"], "WARNING")
        self.assertEqual(self.firewall_signal("estado=ok ultima_ok=1 incidente=-", age=120)["severity"], "CRITICAL")

    def test_firewall_watch_auto_is_silent_without_state_and_required_mode_alerts(self):
        self.assertIsNone(self.firewall_signal(None, mode="auto"))
        self.assertEqual(self.firewall_signal(None, mode=True)["severity"], "CRITICAL")
        self.assertIsNone(self.firewall_signal("estado=detenido ultima_ok=0 incidente=1", mode=False))

    def test_power_loss_30_15_and_restore_emit_one_event_each(self):
        self.power_sample(1000)
        self.assertEqual(self.power_events(), [])
        timeline = [(1060, 90), (1120, 89), (1180, 60), (1240, 30), (1300, 29), (1360, 16), (1420, 15), (1480, 9)]
        for now, capacity in timeline:
            self.power_sample(now, ac="0", capacity=capacity, status="Discharging")
        # horas despues: ninguna repeticion por tiempo
        for now in range(1540, 1540 + 5 * 3600, 60):
            self.power_sample(now, ac="0", capacity=9, status="Discharging")
        self.assertEqual(self.power_events(), [
            ("power.ac", "firing", "WARNING"),
            ("power.battery", "firing", "WARNING"),
            ("power.battery", "firing", "CRITICAL"),
        ])
        end = 1540 + 5 * 3600
        for offset in (0, 60, 120):
            self.power_sample(end + offset, ac="1", capacity=9, status="Charging")
        self.assertEqual(self.power_events()[3:], [
            ("power.ac", "resolved", "INFO"), ("power.battery", "resolved", "INFO")])

    def test_power_microcut_is_ignored_and_state_survives_restart(self):
        self.power_sample(1000)
        self.power_sample(1060, ac="0", capacity=80, status="Discharging")  # una sola muestra
        self.power_sample(1120)
        self.assertEqual(self.power_events(), [])
        for now in (2000, 2060):
            self.power_sample(now, ac="0", capacity=25, status="Discharging")
        self.assertEqual(len(self.power_events()), 2)
        # el equipo se apaga y vuelve con corriente: mismo estado persistido -> restablecimiento, sin duplicar el corte
        self.alerts = monitor.Alerts(self.store, deliver=lambda e: self.events.append(e) or True)
        self.power_sample(90000, ac="1", capacity=5, status="Charging")
        self.power_sample(90060, ac="1", capacity=6, status="Charging")
        self.assertEqual([x[1] for x in self.power_events()], ["firing", "firing", "resolved", "resolved"])

    def test_power_charger_connected_but_discharging_counts_as_loss(self):
        for now in (1000, 1060):
            self.power_sample(now, ac="1", capacity=70, status="Discharging")
        self.assertEqual(self.power_events(), [("power.ac", "firing", "WARNING")])

    def test_power_low_battery_while_charging_does_not_alert(self):
        for now in (1000, 1060):
            signals = self.power_sample(now, ac="1", capacity=3, status="Charging")
        self.assertEqual(signals["power.battery"]["severity"], "INFO")
        self.assertEqual(self.power_events(), [])

    def test_power_without_mains_entry_uses_battery_status(self):
        signals = self.power_sample(1000, mains=False, capacity=14, status="Discharging")
        self.assertEqual(signals["power.battery"]["severity"], "CRITICAL")

    def test_power_indeterminate_reading_never_resolves_an_open_incident(self):
        for now in (1000, 1060):
            self.power_sample(now, mains=False, capacity=12, status="Discharging")
        before = self.power_events()
        for now in (1120, 1180, 1240):
            signals = self.power_sample(now, mains=False, capacity=12, status="Unknown")
        self.assertNotIn("power.ac", signals)
        self.assertEqual(signals["collector.power"]["severity"], "CRITICAL")
        self.assertEqual(self.power_events(), before)
        self.assertEqual(self.store.get("alert:power.battery")["active"], "CRITICAL")
        # con una fuente en linea el estado Unknown si es concluyente
        self.assertEqual(self.power_sample(1300, ac="1", capacity=12, status="Unknown")["power.ac"]["severity"], "INFO")

    def test_power_auto_without_battery_is_silent_and_required_mode_fails_closed(self):
        signals = self.power_sample(1000, mains=False, battery=False)
        self.assertNotIn("power.ac", signals)
        self.assertEqual(signals["collector.power"]["severity"], "INFO")
        self.cfg["power"] = {"enabled": True}
        signals = self.power_sample(1060, mains=False, battery=False)
        self.assertEqual(signals["collector.power"]["severity"], "CRITICAL")
        self.cfg["power"] = {"enabled": False}
        self.assertEqual(set(self.power_sample(1120, ac="0", status="Discharging")), {"collector.power"})

    def test_channel_check_delivers_firing_then_resolved_and_never_prints_secrets(self):
        received, headers = [], []
        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                headers.append(self.headers.get("Authorization"))
                received.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                self.send_response(204)
                self.end_headers()
            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        pings = []
        try:
            env = {"BURY_MONITOR_WEBHOOK": f"http://127.0.0.1:{server.server_port}/secret-path", "BURY_MONITOR_TOKEN": "tok-synthetic",
                   "BURY_MONITOR_HEARTBEAT": "http://127.0.0.1:9/push/hb-synthetic?x=1"}
            with patch.dict(os.environ, env), patch("sys.stdout") as out:
                code = monitor.test_channel(ping=lambda url: pings.append(url) or True)
            printed = "".join(call.args[0] for call in out.write.call_args_list)
        finally:
            server.shutdown()
            server.server_close()
        self.assertEqual(code, 0)
        self.assertEqual([e["status"] for e in received], ["firing", "resolved"])
        self.assertEqual({e["key"] for e in received}, {"monitor.test"})
        self.assertEqual(len({e["event_id"] for e in received}), 2)
        self.assertEqual(headers, ["Bearer tok-synthetic"] * 2)
        self.assertIn("status=up", pings[0])
        for secret in ("secret-path", "tok-synthetic", "hb-synthetic"):
            self.assertNotIn(secret, printed)

    def test_channel_check_invalid_heartbeat_url_fails_without_leaking(self):
        env = {"BURY_MONITOR_WEBHOOK": "http://127.0.0.1:9", "BURY_MONITOR_HEARTBEAT": "https://[synthetic-secret-token]/push"}
        with patch.dict(os.environ, env), patch("sys.stdout") as out, patch("sys.stderr") as err:
            code = monitor.test_channel(deliver=lambda event: True)
        printed = "".join(call.args[0] for call in out.write.call_args_list + err.write.call_args_list)
        self.assertEqual(code, 1)
        self.assertIn("FALLO", printed)
        self.assertNotIn("synthetic-secret-token", printed)

    def test_heartbeat_url_styles_kuma_and_healthchecks(self):
        base = "https://observer.invalid/ping/abc"
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("BURY_MONITOR_HEARTBEAT_STYLE", None)
            self.assertIn("status=up", monitor.heartbeat_url(base + "?x=1", True, "ok"))
            self.assertIn("status=down", monitor.heartbeat_url(base, False, "mal"))
        with patch.dict(os.environ, {"BURY_MONITOR_HEARTBEAT_STYLE": "healthchecks"}):
            self.assertEqual(monitor.heartbeat_url(base + "/", True, "ok"), base)
            self.assertEqual(monitor.heartbeat_url(base, False, "mal"), base + "/fail")
        with patch.dict(os.environ, {"BURY_MONITOR_HEARTBEAT_STYLE": "otro"}):
            with self.assertRaises(ValueError):
                monitor.heartbeat_url(base, True, "ok")

    def test_channel_check_uses_healthchecks_style_and_unknown_style_fails_without_leaking(self):
        pings = []
        env = {"BURY_MONITOR_WEBHOOK": "http://127.0.0.1:9", "BURY_MONITOR_HEARTBEAT": "https://observer.invalid/ping/hb-synthetic",
               "BURY_MONITOR_HEARTBEAT_STYLE": "healthchecks"}
        with patch.dict(os.environ, env), patch("sys.stdout"):
            self.assertEqual(monitor.test_channel(deliver=lambda event: True, ping=lambda url: pings.append(url) or True), 0)
        self.assertEqual(pings, ["https://observer.invalid/ping/hb-synthetic"])
        env["BURY_MONITOR_HEARTBEAT_STYLE"] = "otro"
        with patch.dict(os.environ, env), patch("sys.stdout") as out:
            code = monitor.test_channel(deliver=lambda event: True, ping=lambda url: True)
        printed = "".join(call.args[0] for call in out.write.call_args_list)
        self.assertEqual(code, 1)
        self.assertNotIn("hb-synthetic", printed)

    def test_channel_check_heartbeat_only_is_tested_without_webhook(self):
        pings = []
        env = {"BURY_MONITOR_HEARTBEAT": "https://observer.invalid/ping/hb-synthetic", "BURY_MONITOR_HEARTBEAT_STYLE": "healthchecks"}
        with patch.dict(os.environ, env), patch("sys.stdout"):
            os.environ.pop("BURY_MONITOR_WEBHOOK", None)
            self.assertEqual(monitor.test_channel(ping=lambda url: pings.append(url) or True), 0)
            self.assertEqual(monitor.test_channel(ping=lambda url: False), 1)
        self.assertEqual(len(pings), 1)

    def test_channel_check_exit_codes_without_webhook_and_on_delivery_failure(self):
        with patch.dict(os.environ, {"BURY_MONITOR_WEBHOOK": ""}), patch("sys.stdout"):
            self.assertEqual(monitor.test_channel(), 2)
        with patch.dict(os.environ, {"BURY_MONITOR_WEBHOOK": "http://127.0.0.1:9", "BURY_MONITOR_HEARTBEAT": ""}), patch("sys.stdout"):
            self.assertEqual(monitor.test_channel(deliver=lambda event: False), 1)
        with patch.dict(os.environ, {"BURY_MONITOR_WEBHOOK": "http://127.0.0.1:9", "BURY_MONITOR_HEARTBEAT": "http://127.0.0.1:9/hb"}), patch("sys.stdout"):
            self.assertEqual(monitor.test_channel(deliver=lambda event: True, ping=lambda url: False), 1)

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

    def write_restore(self, **status):
        directory = Path(self.tmp.name) / "monitor-status"
        directory.mkdir(exist_ok=True)
        (directory / "restore-test.json").write_text(json.dumps(status))

    def restore_severity(self):
        self.collector.restore_test()
        return self.collector.signals["backup.restore-test"]["severity"]

    def test_restore_test_ok_failed_stale_and_recovery(self):
        now = monitor.time.time()
        self.write_restore(time=now - 86400, exit=0, verified=True, last_success=now - 86400, message="ok")
        self.assertEqual(self.restore_severity(), "INFO")
        self.write_restore(time=now, exit=9, verified=False, last_success=now - 86400, message="restore FALLO")
        self.assertEqual(self.restore_severity(), "CRITICAL")
        self.assertIn("FAILED", self.collector.signals["backup.restore-test"]["detail"])
        self.write_restore(time=now, exit=0, verified=True, last_success=now, message="ok")   # recuperacion
        self.assertEqual(self.restore_severity(), "INFO")
        for age, expected in [(7 * 86400, "INFO"), (8.5 * 86400, "WARNING"), (11 * 86400, "CRITICAL")]:
            self.write_restore(time=now - age, exit=0, verified=True, last_success=now - age, message="ok")
            self.assertEqual(self.restore_severity(), expected)
        self.write_restore(time=now - 86400, exit=9, verified=False, last_success=now - 11 * 86400)   # fallo Y vencido
        self.assertEqual(self.restore_severity(), "CRITICAL")

    def test_restore_test_recovery_resolves_alert(self):
        now = monitor.time.time()
        self.write_restore(time=now, exit=9, verified=False, last_success=None, message="x")
        self.collector.restore_test()
        self.alerts.update({"backup.restore-test": {**self.collector.signals["backup.restore-test"], "hold": 0}}, 1000)
        self.write_restore(time=now, exit=0, verified=True, last_success=now, message="ok")
        self.collector.restore_test()
        self.alerts.update({"backup.restore-test": {**self.collector.signals["backup.restore-test"], "hold": 0}}, 1060)
        self.assertEqual([e["status"] for e in self.events], ["firing", "resolved"])

    def test_restore_test_never_run_grace_then_alert(self):
        self.assertEqual(self.restore_severity(), "INFO")          # primera observacion: ventana normal
        self.assertIn("never executed", self.collector.signals["backup.restore-test"]["detail"])
        first = self.store.get("restore_test.first_seen")
        for age, expected in [(7 * 86400, "INFO"), (9 * 86400, "WARNING"), (11 * 86400, "CRITICAL")]:
            self.store.put("restore_test.first_seen", monitor.time.time() - age)
            self.assertEqual(self.restore_severity(), expected)
        self.assertIsNotNone(first)

    def test_restore_test_invalid_or_future_status(self):
        (Path(self.tmp.name) / "monitor-status").mkdir()
        (Path(self.tmp.name) / "monitor-status" / "restore-test.json").write_text("{not json")
        self.assertEqual(self.restore_severity(), "CRITICAL")
        self.write_restore(time=monitor.time.time() + 5000, exit=0, verified=True, last_success=None)
        self.assertEqual(self.restore_severity(), "CRITICAL")

    def test_disk_free_space_even_when_percentage_low(self):
        usage = monitor.shutil._ntuple_diskusage(100*monitor.GIB, 60*monitor.GIB, 5*monitor.GIB)
        self.collector.slow = False
        with patch.object(monitor, "command", side_effect=["/docker", ""]), patch.object(monitor.shutil, "disk_usage", return_value=usage):
            self.collector.disks()
        self.assertEqual(self.collector.signals["disk./"]["severity"], "CRITICAL")

    def disk_severity(self, total, used, free):
        gib = monitor.GIB
        usage = monitor.shutil._ntuple_diskusage(int(total * gib), int(used * gib), int(free * gib))
        self.collector.cfg.update(disk_paths=["/"], backup_dir="/"); self.collector.slow = False
        with patch.object(monitor, "command", side_effect=["/docker", ""]), patch.object(monitor.shutil, "disk_usage", return_value=usage):
            self.collector.disks()
        return self.collector.signals["disk./"]["severity"]

    def test_disk_small_volume_thresholds_scale_with_capacity(self):
        # pendrive de 15 GB casi vacio: menos de 20 GiB libres es imposible de cumplir y NO es una alerta
        self.assertEqual(self.disk_severity(14.6, 0.4, 13.5), "INFO")

    def test_disk_small_volume_still_alerts_when_really_filling(self):
        self.assertEqual(self.disk_severity(14.6, 10.5, 3.4), "WARNING")    # 72 % de uso
        self.assertEqual(self.disk_severity(14.6, 9.0, 2.5), "WARNING")     # libre < 20 % aunque el uso informado sea 62 %
        self.assertEqual(self.disk_severity(14.6, 13.4, 0.7), "CRITICAL")
        self.assertEqual(self.disk_severity(14.6, 9.0, 1.2), "CRITICAL")    # libre < 10 %

    def test_disk_large_volume_keeps_absolute_minimums(self):
        self.assertEqual(self.disk_severity(233, 150, 19), "WARNING")
        self.assertEqual(self.disk_severity(233, 150, 9), "CRITICAL")
        self.assertEqual(self.disk_severity(233, 34, 188), "INFO")

    def test_disk_paths_on_same_filesystem_emit_one_signal(self):
        """Rutas del mismo dispositivo (/, DockerRootDir, BACKUP_DIR, volumenes) = una sola alerta de disco, no una por ruta."""
        usage = monitor.shutil._ntuple_diskusage(100*monitor.GIB, 95*monitor.GIB, 5*monitor.GIB)
        a, b, c = (Path(self.tmp.name) / n for n in ("a", "b", "c"))
        for d in (a, b, c): d.mkdir()
        self.collector.cfg.update(disk_paths=[str(a)], backup_dir=str(b))
        self.collector.slow = False
        with patch.object(monitor, "command", side_effect=[str(c), "vol1" + chr(10) + "vol2", f"{a}/x" + chr(10) + f"{b}/y"]), \
             patch.object(monitor.shutil, "disk_usage", return_value=usage), patch.object(monitor.os, "stat") as stat:
            stat.return_value.st_dev = 7
            self.collector.disks()
        disk = [k for k in self.collector.signals if k.startswith("disk.") and not k.startswith("disk.action.")]
        self.assertEqual(disk, ["disk." + str(a)])
        self.assertIn("same filesystem as", self.collector.signals["disk." + str(a)]["detail"])
        self.assertEqual(self.collector.signals["disk." + str(a)]["severity"], "CRITICAL")

    def test_disk_paths_on_different_filesystems_are_all_monitored(self):
        usage = monitor.shutil._ntuple_diskusage(100*monitor.GIB, 10*monitor.GIB, 90*monitor.GIB)
        a, b = (Path(self.tmp.name) / n for n in ("a", "b"))
        for d in (a, b): d.mkdir()
        self.collector.cfg.update(disk_paths=[str(a)], backup_dir=str(b))
        self.collector.slow = False
        devs = {str(a): 1, str(b): 2, str(a / "docker"): 1}
        with patch.object(monitor, "command", side_effect=[str(a / "docker"), ""]), patch.object(monitor.shutil, "disk_usage", return_value=usage),              patch.object(monitor.os, "stat", side_effect=lambda p: type("S", (), {"st_dev": devs.get(str(p), 9)})()):
            self.collector.disks()
        disk = sorted(k for k in self.collector.signals if k.startswith("disk.") and not k.startswith("disk.action."))
        self.assertEqual(disk, sorted(["disk." + str(a), "disk." + str(b)]))

    def test_disk_unstatable_path_is_kept_and_action_signal_follows_filesystem(self):
        """Una ruta cuyo stat falla se conserva (su fallo es informacion); disk.action.* se emite solo para la ruta representante."""
        usage = monitor.shutil._ntuple_diskusage(100*monitor.GIB, 90*monitor.GIB, 10*monitor.GIB)
        a, b = (Path(self.tmp.name) / n for n in ("a", "b"))
        for d in (a, b): d.mkdir()
        self.collector.cfg.update(disk_paths=[str(a)], backup_dir=str(b))
        self.collector.slow = False
        def stat(path):
            if str(path).endswith("roto"): raise OSError("sin acceso")
            return type("S", (), {"st_dev": 3})()
        with patch.object(monitor, "command", side_effect=[str(a / "roto"), ""]), patch.object(monitor.shutil, "disk_usage", return_value=usage), \
             patch.object(monitor.os, "stat", side_effect=stat):
            self.collector.disks()
        keys = sorted(k for k in self.collector.signals if k.startswith("disk."))
        self.assertIn("disk." + str(a / "roto"), keys)          # conservada pese al stat fallido
        self.assertIn("disk.action." + str(a), keys)
        self.assertNotIn("disk." + str(b), keys)                # mismo dispositivo que `a`: agrupada
        self.assertNotIn("disk.action." + str(b), keys)

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

    def test_tls_short_lived_certificate_measures_fraction_of_lifetime(self):
        """CA interna de Caddy: hoja de 12 h. 10,8 h restantes (90 %) es normal; renovacion fallando (20 %, 5 %) si debe alertar."""
        for remaining, expected in [(0.45, "INFO"), (0.20, "INFO"), (0.12, "WARNING"), (0.04, "CRITICAL")]:
            self.collector.tls_expiry("https://x", info=lambda _u, r=remaining: (r, 0.5))
            self.assertEqual(self.collector.signals["tls.expiry"]["severity"], expected, remaining)

    def test_tls_short_lived_boundaries_and_expired(self):
        """Bordes: exactamente 25 % / 10 % NO alertan (el umbral es estricto, como dice la doc); expirado o nulo es CRITICAL."""
        for remaining, expected in [(0.125, "INFO"), (0.0625 + 0.0000001, "WARNING"), (0.05, "WARNING"), (0.0499, "CRITICAL"),
                                    (0.0, "CRITICAL"), (-0.1, "CRITICAL")]:
            self.collector.tls_expiry("https://x", info=lambda _u, r=remaining: (r, 0.5))
            self.assertEqual(self.collector.signals["tls.expiry"]["severity"], expected, remaining)

    def test_tls_lifetime_limit_between_short_and_long(self):
        """7 dias exactos de vida total = hoja corta (fraccion); mas de 7 = umbrales de 21/7 dias, tambien en sus bordes."""
        self.collector.tls_expiry("https://x", info=lambda _u: (6.5, 7.0))
        self.assertEqual(self.collector.signals["tls.expiry"]["severity"], "INFO")
        self.collector.tls_expiry("https://x", info=lambda _u: (6.5, 7.01))
        self.assertEqual(self.collector.signals["tls.expiry"]["severity"], "CRITICAL")  # 6,5 dias <= 7: regla larga
        for remaining, expected in [(21.01, "INFO"), (21, "WARNING"), (7.01, "WARNING"), (7, "CRITICAL")]:
            self.collector.tls_expiry("https://x", info=lambda _u, r=remaining: (r, 90))
            self.assertEqual(self.collector.signals["tls.expiry"]["severity"], expected, remaining)

    def test_tls_nonpositive_lifetime_is_rejected(self):
        with self.assertRaises(ValueError):
            self.collector.tls_expiry("https://x", info=lambda _u: (60, 0))

    def test_tls_info_extracts_dates_and_requires_https(self):
        now = time.time()
        fmt = lambda t: time.strftime("%b %d %H:%M:%S %Y GMT", time.gmtime(t))
        cert = {"notBefore": fmt(now - 3600), "notAfter": fmt(now + 3 * 3600)}
        conn = MagicMock(); conn.getpeercert.return_value = cert
        ctx = MagicMock(); ctx.wrap_socket.return_value.__enter__.return_value = conn
        with patch.object(monitor.socket, "create_connection"), patch.object(monitor.ssl, "create_default_context", return_value=ctx):
            remaining, lifetime = monitor.tls_info("https://tbp")
        self.assertAlmostEqual(lifetime, 4 / 24, places=3)
        self.assertAlmostEqual(remaining, 3 / 24, places=3)
        with self.assertRaises(ValueError):
            monitor.tls_info("http://tbp")
        ctx.wrap_socket.side_effect = monitor.ssl.SSLCertVerificationError("expired")  # el error TLS se propaga (-> collector.tls CRITICAL)
        with patch.object(monitor.socket, "create_connection"), patch.object(monitor.ssl, "create_default_context", return_value=ctx):
            with self.assertRaises(monitor.ssl.SSLCertVerificationError):
                monitor.tls_info("https://tbp")

    def test_tls_long_lived_certificate_keeps_day_thresholds(self):
        for remaining, expected in [(60, "INFO"), (20, "WARNING"), (5, "CRITICAL")]:
            self.collector.tls_expiry("https://x", info=lambda _u, r=remaining: (r, 90))
            self.assertEqual(self.collector.signals["tls.expiry"]["severity"], expected, remaining)

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

    # --- P-06/P-07/P-08: copia externa, restore desde el remoto y custodia de claves ---------------------------------------
    def write_status(self, name, **status):
        directory = Path(self.tmp.name) / "monitor-status"
        directory.mkdir(exist_ok=True)
        (directory / (name + ".json")).write_text(json.dumps(status))

    def severity(self, method, key):
        getattr(self.collector, method)()
        return self.collector.signals[key]["severity"]

    def test_offsite_success_missing_or_unverified_is_critical(self):
        now = monitor.time.time()
        for kind in self.cfg["backup_windows"]:
            self.write_status(kind, time=now, exit=0, verified=True)
        self.collector.backups()
        self.assertEqual(self.collector.signals["backup.offsite-sql"]["severity"], "INFO")
        self.assertEqual(self.collector.signals["backup.offsite-files"]["severity"], "INFO")
        self.write_status("offsite-files", time=now, exit=6, verified=False)           # copia externa fallida
        (Path(self.tmp.name) / "monitor-status" / "offsite-sql.json").unlink()           # nunca hubo exito verificado
        self.collector.backups()
        for key in ("backup.offsite-sql", "backup.offsite-files"):
            self.assertEqual(self.collector.signals[key]["severity"], "CRITICAL")
        self.write_status("offsite-sql", time=now - 3 * 86400, exit=0, verified=True)   # exito demasiado viejo
        self.collector.backups()
        self.assertEqual(self.collector.signals["backup.offsite-sql"]["severity"], "CRITICAL")

    def test_custody_ok_failed_stale_and_recovery(self):
        now = monitor.time.time()
        self.write_status("custody", time=now - 86400, exit=0, verified=True, last_success=now - 86400, message="custodia OK (0 aviso(s))")
        self.assertEqual(self.severity("custody", "backup.custody"), "INFO")
        self.write_status("custody", time=now, exit=11, verified=False, last_success=now - 86400, message="2 fallo(s): C03,C09")
        self.assertEqual(self.severity("custody", "backup.custody"), "CRITICAL")
        detail = self.collector.signals["backup.custody"]["detail"]
        self.assertIn("FAILED", detail)
        self.assertIn("C03,C09", detail)                       # reglas, sin valores
        self.write_status("custody", time=now, exit=0, verified=True, last_success=now, message="ok")
        self.assertEqual(self.severity("custody", "backup.custody"), "INFO")
        for age, expected in [(7 * 86400, "INFO"), (9 * 86400, "WARNING"), (16 * 86400, "CRITICAL")]:
            self.write_status("custody", time=now - age, exit=0, verified=True, last_success=now - age, message="ok")
            self.assertEqual(self.severity("custody", "backup.custody"), expected)

    def test_custody_never_run_grace_then_alert_and_can_be_disabled(self):
        self.assertEqual(self.severity("custody", "backup.custody"), "INFO")
        self.assertIn("never executed", self.collector.signals["backup.custody"]["detail"])
        for age, expected in [(7 * 86400, "INFO"), (9 * 86400, "WARNING"), (16 * 86400, "CRITICAL")]:
            self.store.put("custody.first_seen", monitor.time.time() - age)
            self.assertEqual(self.severity("custody", "backup.custody"), expected)
        self.collector.signals = {}
        self.cfg["custody"] = {"enabled": False}
        self.collector.custody()
        self.assertNotIn("backup.custody", self.collector.signals)

    def test_custody_alert_fires_and_resolves_without_secrets(self):
        now = monitor.time.time()
        self.write_status("custody", time=now, exit=11, verified=False, last_success=None, message="1 fallo(s): C06")
        self.collector.custody()
        self.alerts.update({"backup.custody": {**self.collector.signals["backup.custody"], "hold": 0}}, 1000)
        self.write_status("custody", time=now, exit=0, verified=True, last_success=now, message="custodia OK")
        self.collector.custody()
        self.alerts.update({"backup.custody": {**self.collector.signals["backup.custody"], "hold": 0}}, 1060)
        self.assertEqual([e["status"] for e in self.events], ["firing", "resolved"])
        self.assertNotIn("password", json.dumps(self.events).lower())

    def test_offsite_restore_test_auto_enabled_only_with_offsite(self):
        self.collector.offsite_restore_test()
        self.assertNotIn("backup.offsite-restore-test", self.collector.signals)          # sin copia externa: no aplica
        self.write_status("offsite-sql", time=monitor.time.time(), exit=0, verified=True)
        self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), "INFO")   # gracia de primera vez
        self.assertIn("never executed", self.collector.signals["backup.offsite-restore-test"]["detail"])

    def test_offsite_restore_test_failed_stale_and_recovery(self):
        now = monitor.time.time()
        self.write_status("offsite-sql", time=now, exit=0, verified=True)
        self.write_status("offsite-restore-test", time=now - 86400, exit=0, verified=True, last_success=now - 86400, message="ok")
        self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), "INFO")
        self.write_status("offsite-restore-test", time=now, exit=6, verified=False, last_success=now - 86400,
                          message="no hay backups full en secure:*/sql (o el remoto no responde / las passwords crypt no descifran)")
        self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), "CRITICAL")
        self.assertIn("offsite-restore-test FAILED", self.collector.signals["backup.offsite-restore-test"]["detail"])
        for age, expected in [(30 * 86400, "INFO"), (40 * 86400, "WARNING"), (50 * 86400, "CRITICAL")]:
            self.write_status("offsite-restore-test", time=now - age, exit=0, verified=True, last_success=now - age, message="ok")
            self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), expected)
        self.write_status("offsite-restore-test", time=now, exit=0, verified=True, last_success=now, message="ok")
        self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), "INFO")

    def test_offsite_restore_test_explicit_enable_and_thresholds_from_config(self):
        self.cfg["offsite_restore_test"] = {"enabled": True, "warning": 100, "critical": 200}
        now = monitor.time.time()
        self.write_status("offsite-restore-test", time=now - 150, exit=0, verified=True, last_success=now - 150, message="ok")
        self.assertEqual(self.severity("offsite_restore_test", "backup.offsite-restore-test"), "WARNING")
        self.cfg["offsite_restore_test"]["enabled"] = False
        self.collector.signals = {}
        self.collector.offsite_restore_test()
        self.assertNotIn("backup.offsite-restore-test", self.collector.signals)

    def test_example_config_documents_custody_and_offsite_restore_thresholds(self):
        self.assertIn("custody", self.cfg)
        self.assertIn("offsite_restore_test", self.cfg)
        for key in ("custody", "offsite_restore_test"):
            self.assertLess(self.cfg[key]["warning"], self.cfg[key]["critical"])

    def test_enabled_accepts_string_values(self):
        for value in ("false", "False", "0", "off", "no", "disabled", False):
            self.cfg["custody"] = {"enabled": value}
            self.cfg["offsite_restore_test"] = {"enabled": value}
            self.write_status("offsite-sql", time=monitor.time.time(), exit=0, verified=True)
            self.collector.signals = {}
            self.collector.custody()
            self.collector.offsite_restore_test()
            self.assertNotIn("backup.custody", self.collector.signals, value)
            self.assertNotIn("backup.offsite-restore-test", self.collector.signals, value)
        for value in ("true", "1", "yes", True):
            self.cfg["custody"] = {"enabled": value}
            self.cfg["offsite_restore_test"] = {"enabled": value}
            self.collector.signals = {}
            self.collector.custody()
            self.collector.offsite_restore_test()
            self.assertIn("backup.custody", self.collector.signals, value)
            self.assertIn("backup.offsite-restore-test", self.collector.signals, value)
        self.cfg["offsite_restore_test"] = {"enabled": "AUTO"}
        self.collector.signals = {}
        self.collector.offsite_restore_test()
        self.assertIn("backup.offsite-restore-test", self.collector.signals)   # auto: hay offsite-sql.json


    def test_capacity_absent_ok_failed_and_recovery(self):
        now = monitor.time.time()
        self.collector.capacity()
        self.assertNotIn("backup.capacity", self.collector.signals)   # sin limites configurados no hay estado: no se alerta
        self.write_status("capacity", time=now - 900, exit=0, verified=True, last_success=now - 900, message="ok")
        self.assertEqual(self.severity("capacity", "backup.capacity"), "INFO")
        self.write_status("capacity", time=now, exit=7, verified=False, last_success=now - 900, message="disco insuficiente o fallo")
        self.assertEqual(self.severity("capacity", "backup.capacity"), "CRITICAL")
        self.assertIn("FAILED", self.collector.signals["backup.capacity"]["detail"])
        self.write_status("capacity", time=now, exit=0, verified=True, last_success=now, message="ok")
        self.assertEqual(self.severity("capacity", "backup.capacity"), "INFO")
        for age, expected in ((3 * 86400, "WARNING"), (5 * 86400, "CRITICAL")):
            self.write_status("capacity", time=now - age, exit=0, verified=True, last_success=now - age, message="ok")
            self.assertEqual(self.severity("capacity", "backup.capacity"), expected)
        self.cfg["capacity"] = {"enabled": False}
        self.collector.signals = {}
        self.collector.capacity()
        self.assertNotIn("backup.capacity", self.collector.signals)

    def test_removable_verify_absent_ok_failed_and_recovery(self):
        now = monitor.time.time()
        self.collector.removable_verify()
        self.assertNotIn("backup.removable-verify", self.collector.signals)   # sin verificador ejecutado no hay estado
        self.write_status("removable-verify", time=now - 600, exit=0, verified=True, last_success=now - 600, message="ok")
        self.assertEqual(self.severity("removable_verify", "backup.removable-verify"), "INFO")
        # montaje ausente / sha256 distinto / sin espacio: CRITICAL de inmediato, aunque el ultimo exito sea de hoy
        self.write_status("removable-verify", time=now, exit=13, verified=False, last_success=now - 600, message="V1 destino NO disponible")
        self.assertEqual(self.severity("removable_verify", "backup.removable-verify"), "CRITICAL")
        self.assertIn("FAILED", self.collector.signals["backup.removable-verify"]["detail"])
        self.write_status("removable-verify", time=now, exit=0, verified=True, last_success=now, message="ok")
        self.assertEqual(self.severity("removable_verify", "backup.removable-verify"), "INFO")
        for age, expected in ((3 * 86400, "WARNING"), (5 * 86400, "CRITICAL")):   # el verificador dejo de correr
            self.write_status("removable-verify", time=now - age, exit=0, verified=True, last_success=now - age, message="ok")
            self.assertEqual(self.severity("removable_verify", "backup.removable-verify"), expected)
        self.cfg["removable_verify"] = {"enabled": False}
        self.collector.signals = {}
        self.collector.removable_verify()
        self.assertNotIn("backup.removable-verify", self.collector.signals)

    def test_removable_copy_absent_current_stale_and_invalid(self):
        now = monitor.time.time()
        self.collector.removable_copy()
        self.assertNotIn("backup.removable-copy", self.collector.signals)   # sin BACKUP_REMOVABLE_DIR no hay estado
        self.write_status("removable-copy", time=now - 60, exit=0, verified=True, last_success=now - 60, message="al dia")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "INFO")
        # disco desconectado: el ultimo intento fallo pero el ultimo exito es reciente -> no es critico todavia
        self.write_status("removable-copy", time=now, exit=1, verified=False, last_success=now - 86400, message="atrasada: destino no disponible")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "INFO")
        self.assertIn("ATRASADA", self.collector.signals["backup.removable-copy"]["detail"])
        self.write_status("removable-copy", time=now, exit=1, verified=False, last_success=now - 4 * 86400, message="atrasada: destino no disponible")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "WARNING")
        self.write_status("removable-copy", time=now, exit=1, verified=False, last_success=now - 8 * 86400, message="atrasada: destino no disponible")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "CRITICAL")
        self.write_status("removable-copy", time=now, exit=1, verified=False, last_success=None, message="atrasada")   # nunca tuvo exito: se mide desde el intento
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "INFO")
        self.write_status("removable-copy", time=now + 9999, exit=0, verified=True, last_success=now, message="x")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "CRITICAL")
        (Path(self.tmp.name) / "monitor-status" / "removable-copy.json").write_text("{no json")
        self.assertEqual(self.severity("removable_copy", "backup.removable-copy"), "CRITICAL")

# P-08: las pruebas del relevo a Telegram se ejecutan junto con las del monitor (el CI descubre solo este archivo; ver test_telegram_relay.py).
@unittest.skipUnless(os.name == "posix" and shutil.which("bash"), "instalar-agente.sh es POSIX")
class InstalarAgenteTests(unittest.TestCase):
    HERE = os.path.dirname(os.path.abspath(__file__))

    def test_activar_canales_detects_incomplete_deployment(self):
        with tempfile.TemporaryDirectory() as d:
            for f in ("activar-canales.sh", "monitor.py"):  # falta telegram_relay.py y la unidad: el caso real de la VM
                shutil.copy(os.path.join(self.HERE, f), d)
            p = subprocess.run(["bash", os.path.join(d, "activar-canales.sh"), "--dry-run"], capture_output=True, text=True,
                               stdin=subprocess.DEVNULL, timeout=30)
            self.assertEqual(p.returncode, 2)
            self.assertIn("instalar-agente.sh", p.stderr)

    def test_installer_copies_the_complete_set_keeps_bak_and_is_idempotent(self):
        with tempfile.TemporaryDirectory() as dest:
            env = {**os.environ, "BURY_INSTALL_ANY_USER": "1"}
            with open(os.path.join(dest, "monitor.py"), "w") as f:
                f.write("# version vieja\n")
            run = lambda: subprocess.run(["bash", os.path.join(self.HERE, "instalar-agente.sh"), dest], capture_output=True, text=True, env=env, timeout=60)
            first = run()
            self.assertEqual(first.returncode, 0, first.stderr)
            for f in ("monitor.py", "telegram_relay.py", "activar-canales.sh", "bury-monitor.service", "bury-telegram-relay.service"):
                self.assertTrue(os.path.isfile(os.path.join(dest, f)), f)
            self.assertEqual(open(os.path.join(dest, "monitor.py.bak")).read(), "# version vieja\n")
            self.assertTrue(os.access(os.path.join(dest, "activar-canales.sh"), os.X_OK))
            self.assertIn("Sin cambios", run().stdout)

    def test_installer_fixes_permissions_even_if_content_matches_and_rejects_symlinked_target(self):
        with tempfile.TemporaryDirectory() as dest:
            env = {**os.environ, "BURY_INSTALL_ANY_USER": "1"}
            run = lambda d: subprocess.run(["bash", os.path.join(self.HERE, "instalar-agente.sh"), d], capture_output=True, text=True, env=env, timeout=60)
            self.assertEqual(run(dest).returncode, 0)
            script = os.path.join(dest, "activar-canales.sh")
            os.chmod(script, 0o644)
            self.assertEqual(run(dest).returncode, 0)
            self.assertTrue(os.access(script, os.X_OK))
            os.remove(os.path.join(dest, "telegram_relay.py"))
            os.symlink(os.path.join(dest, "monitor.py"), os.path.join(dest, "telegram_relay.py"))
            p = run(dest)
            self.assertEqual(p.returncode, 2)
            self.assertEqual(len([n for n in os.listdir(dest) if n.startswith(".instalacion")]), 0)


from test_telegram_relay import ConcurrencyAndHardeningTests, RelayTests, ServerAndCliTests  # noqa: E402,F401


@unittest.skipUnless(os.name == "posix" and shutil.which("bash") and shutil.which("openssl"), "activar-canales.sh es POSIX")
class ActivarCanalesTests(unittest.TestCase):
    """Activacion guiada: validaciones, permisos 0600 y ausencia de secretos en la salida (valores armados en runtime)."""
    SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "activar-canales.sh")

    def run_script(self, root, extra_env, *args):
        env = {**os.environ, "BURY_ACT_ROOT": root, "BURY_ACT_NO_SYSTEMD": "1"}
        for k in [k for k in env if k.startswith("BURY_ACT_") and k not in ("BURY_ACT_ROOT", "BURY_ACT_NO_SYSTEMD")]:
            del env[k]
        env.update(extra_env)
        return subprocess.run(["bash", self.SCRIPT, *args], capture_output=True, text=True, env=env, stdin=subprocess.DEVNULL, timeout=60)

    def values(self):
        return {"BURY_ACT_TELEGRAM_TOKEN": "123456789:" + "Ab1_" * 9, "BURY_ACT_TELEGRAM_CHAT_ID": "-1001234567890",
                "BURY_ACT_HEARTBEAT_URL": "https://observer.invalid/ping/" + "hb" * 8}

    def test_dry_run_validates_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as root:
            p = self.run_script(root, self.values(), "--dry-run")
            self.assertEqual(p.returncode, 0, p.stderr)
            self.assertFalse(os.path.exists(os.path.join(root, "etc")))
            for secret in self.values().values():
                self.assertNotIn(secret, p.stdout + p.stderr)

    def test_no_values_exits_3_and_invalid_values_exit_2(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertEqual(self.run_script(root, {}).returncode, 3)
            bad = {**self.values(), "BURY_ACT_TELEGRAM_TOKEN": "no-es-un-token"}
            self.assertEqual(self.run_script(root, bad).returncode, 2)
            bad = {**self.values(), "BURY_ACT_TELEGRAM_CHAT_ID": "abc"}
            self.assertEqual(self.run_script(root, bad).returncode, 2)
            bad = {**self.values(), "BURY_ACT_HEARTBEAT_URL": "http://insegura.invalid/x"}
            self.assertEqual(self.run_script(root, bad).returncode, 2)
            self.assertFalse(os.path.exists(os.path.join(root, "etc")))

    def test_url_with_shell_metacharacters_is_rejected_and_ampersand_is_literal(self):
        with tempfile.TemporaryDirectory() as root:
            for bad in ("https://observer.invalid/$(id)", "https://observer.invalid/x;id", "https://observer.invalid/`id`", 'https://observer.invalid/x"y'):
                p = self.run_script(root, {"BURY_ACT_HEARTBEAT_URL": bad})
                self.assertEqual(p.returncode, 2, bad)
            self.assertFalse(os.path.exists(os.path.join(root, "etc")))
            ok = "https://observer.invalid/ping/abc?a=1&b=2"
            p = self.run_script(root, {"BURY_ACT_HEARTBEAT_URL": ok})
            self.assertEqual(p.returncode, 0, p.stderr)
            self.assertIn("BURY_MONITOR_HEARTBEAT=" + ok, open(os.path.join(root, "etc", "bury-monitor", "monitor.env")).read())

    def test_reactivation_keeps_the_internal_bearer_so_the_running_relay_still_authenticates(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertEqual(self.run_script(root, self.values()).returncode, 0)
            tge = os.path.join(root, "etc", "bury-monitor", "telegram-relay.env")
            first = [l for l in open(tge).read().splitlines() if l.startswith("BURY_RELAY_AUTH_TOKEN=")]
            self.assertEqual(self.run_script(root, self.values()).returncode, 0)
            second = [l for l in open(tge).read().splitlines() if l.startswith("BURY_RELAY_AUTH_TOKEN=")]
            self.assertEqual(first, second)

    def test_writes_0600_files_keeps_previous_values_and_never_prints_secrets(self):
        with tempfile.TemporaryDirectory() as root:
            etc = os.path.join(root, "etc", "bury-monitor")
            os.makedirs(etc)
            with open(os.path.join(etc, "monitor.env"), "w") as f:
                f.write("BURY_OTRA=conservar\nBURY_MONITOR_WEBHOOK=http://viejo.invalid\n")
            v = self.values()
            p = self.run_script(root, v)
            self.assertEqual(p.returncode, 0, p.stderr)
            for secret in v.values():
                self.assertNotIn(secret, p.stdout + p.stderr)
            mon = open(os.path.join(etc, "monitor.env")).read()
            tge = open(os.path.join(etc, "telegram-relay.env")).read()
            self.assertIn("BURY_OTRA=conservar", mon)
            self.assertEqual(mon.count("BURY_MONITOR_WEBHOOK="), 1)
            self.assertIn("BURY_MONITOR_WEBHOOK=http://127.0.0.1:9098/alert", mon)
            self.assertIn("BURY_MONITOR_HEARTBEAT_STYLE=healthchecks", mon)
            auth_mon = [l.split("=", 1)[1] for l in mon.splitlines() if l.startswith("BURY_MONITOR_TOKEN=")][0]
            auth_tge = [l.split("=", 1)[1] for l in tge.splitlines() if l.startswith("BURY_RELAY_AUTH_TOKEN=")][0]
            self.assertEqual(auth_mon, auth_tge)
            self.assertGreaterEqual(len(auth_mon), 32)
            for name in ("monitor.env", "telegram-relay.env", "monitor.env.previo"):
                self.assertEqual(oct(os.stat(os.path.join(etc, name)).st_mode & 0o777), "0o600", name)


if __name__ == "__main__":
    unittest.main()

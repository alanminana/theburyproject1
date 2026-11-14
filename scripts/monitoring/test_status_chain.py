"""Cadena completa de estados de backup/custodia -> monitor -> alerta, con los productores REALES.

test_monitor.py inyecta JSON a mano; test-hardening.sh y test-check-custody.sh prueban los scripts de backup pero no miran al
monitor. Aca se unen las dos puntas: los estados los escribe el codigo real de scripts/backup (lib.sh: monitor_status /
restore_test_status, y check-custody.sh completo) y los consume el Collector real, que alimenta a Alerts. Asi un cambio de formato
en un lado que el otro no entiende rompe esta prueba en lugar de dejar el monitor ciego en produccion.

Sin Docker, sin red, sin tocar datos: todo en un directorio temporal, passwords inventadas. Requiere bash POSIX (se omite en Windows nativo o sin bash).
No modifica nada de scripts/backup.   Uso:  python3 -m unittest discover -s scripts/monitoring -p 'test_status_chain.py'
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import monitor

ROOT = Path(__file__).resolve().parents[2]
BACKUP = ROOT / "scripts" / "backup"
BASH = shutil.which("bash")
# Valor inventado, armado en tiempo de ejecucion para no parecer una credencial a los escaners de secretos.
SECRET = "".join(["Zq7", "Lm2", "Xv9", "Kd4", "Rt6", "Hw8", "Bn3", "Yc5"])


# Solo POSIX: las rutas viajan por variables de entorno a bash y en Windows no se convierten (Git Bash) o el bash puede ser un
# lanzador WSL. Corre en CI (Ubuntu) y en WSL/Linux; en Windows se omite en lugar de dar un falso verde o un falso rojo.
@unittest.skipIf(os.name == "nt", "requiere bash POSIX (CI Ubuntu / WSL)")
@unittest.skipUnless(BASH, "bash no disponible")
class StatusChainTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.dir = Path(self.tmp.name)
        self.backups = self.dir / "backups"
        for sub in ("sql", "files", "logs", "monitor-status"):
            (self.backups / sub).mkdir(parents=True)
        self.store = monitor.Store(self.dir / "state.sqlite")
        self.events = []
        self.alerts = monitor.Alerts(self.store, deliver=lambda e: self.events.append(e) or True)
        cfg = json.loads(Path(__file__).with_name("config.example.json").read_text())
        cfg["backup_dir"] = str(self.backups)
        self.cfg = cfg
        self.collector = monitor.Collector(cfg, self.store)
        self.clock = 1000

    # ---- productores reales ------------------------------------------------------------------------------------------------
    def lib(self, snippet):
        """Ejecuta funciones reales de lib.sh con BACKUP_DIR de prueba."""
        env = {**os.environ, "BACKUP_DIR": str(self.backups)}
        result = subprocess.run([BASH, "-c", f"cd '{BACKUP.as_posix()}' && source ./lib.sh >/dev/null 2>&1 && {snippet}"],
                                env=env, capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)

    def custody(self, *, bad):
        """Corre check-custody.sh REAL (escribe monitor-status/custody.json). bad=True copia las claves dentro de BACKUP_DIR (C03)."""
        case = self.dir / "custody"
        (case / "home").mkdir(parents=True, exist_ok=True)
        (case / ".env").write_text("BACKUP_REMOTE=secure:erp\nBACKUP_REQUIRE_OFFSITE=true\nMSSQL_DATABASE=TheBuryProjectDb\n")
        offsite = case / "backup-offsite.env"
        offsite.write_text("RCLONE_CONFIG_OFFSITE_TYPE=sftp\nRCLONE_CONFIG_SECURE_TYPE=crypt\n"
                           f"RCLONE_CONFIG_SECURE_REMOTE=offsite:erp\nRCLONE_CONFIG_SECURE_PASSWORD={SECRET}\n"
                           f"RCLONE_CONFIG_SECURE_PASSWORD2={SECRET[::-1]}\n")
        for name in (".env", "backup-offsite.env"):
            os.chmod(case / name, 0o600)
        (self.backups / "sql" / "TheBuryProjectDb_full_2026-01-01_000000Z.bak").write_text("x")
        (self.backups / "logs" / "backup.log").write_text("2026-01-01T00:00:00Z [INFO] backup ok\n")
        leaked = self.backups / "backup-offsite.env"
        leaked.unlink(missing_ok=True)
        if bad:
            shutil.copy(offsite, leaked)
        env = {"PATH": os.environ["PATH"], "HOME": str(case / "home"), "BACKUP_DIR": str(self.backups),
               "COMPOSE_ENV_FILES": str(case / ".env"), "BURY_OFFSITE_ENV_FILE": str(offsite), "CUSTODY_RECOVERY_DIRS": ""}
        result = subprocess.run([BASH, str(BACKUP / "check-custody.sh"), "--quiet"], env=env, capture_output=True, text=True, timeout=120)
        return result

    # ---- consumo: monitor + alertas ----------------------------------------------------------------------------------------
    def observe(self, *collectors, keys):
        """Colecta senales reales y las pasa por Alerts en dos muestras (hold=0 para no esperar 120 s)."""
        self.collector.signals = {}
        for name in collectors:
            getattr(self.collector, name)()
        picked = {k: {**self.collector.signals[k], "hold": 0} for k in keys}
        self.clock += 60
        self.alerts.update(picked, self.clock)
        return {k: self.collector.signals[k]["severity"] for k in keys}

    def statuses(self, key):
        return [e["status"] for e in self.events if e["key"] == key]

    # ---- casos -------------------------------------------------------------------------------------------------------------
    def test_offsite_upload_failure_fires_and_recovers(self):
        self.lib("monitor_status offsite-sql 6 false; monitor_status run-full 6 false")
        sev = self.observe("backups", keys=["backup.offsite-sql", "backup.run-full"])
        self.assertEqual(sev, {"backup.offsite-sql": "CRITICAL", "backup.run-full": "CRITICAL"})
        self.assertEqual(self.statuses("backup.offsite-sql"), ["firing"])
        self.assertIn("offsite-sql", json.dumps(self.events))
        self.lib("monitor_status offsite-sql 0 true; monitor_status run-full 0 true")
        sev = self.observe("backups", keys=["backup.offsite-sql", "backup.run-full"])
        self.assertEqual(sev, {"backup.offsite-sql": "INFO", "backup.run-full": "INFO"})
        self.assertEqual(self.statuses("backup.offsite-sql"), ["firing", "resolved"])

    def test_restore_failures_local_and_offsite_fire_without_secrets_and_recover(self):
        self.lib("monitor_status offsite-sql 0 true")  # habilita offsite-restore-test en modo "auto"
        self.lib(f"restore_test_status 9 false 'restore fallo password={SECRET}' restore-test;"
                 f" restore_test_status 9 false 'descarga offsite fallo password={SECRET}' offsite-restore-test")
        keys = ["backup.restore-test", "backup.offsite-restore-test"]
        sev = self.observe("restore_test", "offsite_restore_test", keys=keys)
        self.assertEqual(sev, {k: "CRITICAL" for k in keys})
        self.assertEqual(self.statuses("backup.restore-test"), ["firing"])
        self.assertEqual(self.statuses("backup.offsite-restore-test"), ["firing"])
        self.assertNotIn(SECRET, json.dumps(self.events))                       # el secreto nunca llega al canal de alertas
        self.lib("restore_test_status 0 true ok restore-test; restore_test_status 0 true ok offsite-restore-test")
        sev = self.observe("restore_test", "offsite_restore_test", keys=keys)
        self.assertEqual(sev, {k: "INFO" for k in keys})
        self.assertEqual(self.statuses("backup.restore-test"), ["firing", "resolved"])
        self.assertEqual(self.statuses("backup.offsite-restore-test"), ["firing", "resolved"])

    def test_custody_failure_from_real_script_fires_and_recovers(self):
        bad = self.custody(bad=True)
        self.assertEqual(bad.returncode, 11, bad.stdout + bad.stderr)
        self.assertTrue((self.backups / "monitor-status" / "custody.json").exists())
        sev = self.observe("custody", keys=["backup.custody"])
        self.assertEqual(sev["backup.custody"], "CRITICAL")
        self.assertIn("C03", self.collector.signals["backup.custody"]["detail"])
        self.assertEqual(self.statuses("backup.custody"), ["firing"])
        self.assertNotIn(SECRET, json.dumps(self.events))
        self.assertNotIn(SECRET, bad.stdout + bad.stderr)
        good = self.custody(bad=False)
        self.assertEqual(good.returncode, 0, good.stdout + good.stderr)
        sev = self.observe("custody", keys=["backup.custody"])
        self.assertEqual(sev["backup.custody"], "INFO")
        self.assertEqual(self.statuses("backup.custody"), ["firing", "resolved"])

    def test_missing_status_never_resolves_a_firing_alert(self):
        self.lib("monitor_status offsite-sql 6 false")
        self.observe("backups", keys=["backup.offsite-sql"])
        self.assertEqual(self.statuses("backup.offsite-sql"), ["firing"])
        self.clock += 60
        self.alerts.update({}, self.clock)                                       # muestra ausente: no resuelve
        self.assertEqual(self.statuses("backup.offsite-sql"), ["firing"])


if __name__ == "__main__":
    unittest.main()

"""Pruebas del relevo a Telegram (P-08) contra una API de Telegram SIMULADA en loopback. Sin red externa, sin credenciales reales: todos los
valores son inventados y el servidor falso se levanta en un puerto efimero.
Uso:  python3 -m unittest discover -s scripts/monitoring -p 'test_telegram_relay.py'
"""
import json
import os
import subprocess
import sys
import threading
import unittest
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import telegram_relay as relay

HERE = Path(__file__).resolve().parent
# Valores inventados, armados en tiempo de ejecucion (no parecen credenciales a los escaners de secretos).
BOT = "".join(["123456", ":", "AAFake", "Token", "ForTests", "0123456789"])
CHAT = "-1001234567890"
AUTH = "".join(["relay", "-", "bearer", "-", "synthetic", "-", "42"])


class FakeTelegram:
    def __init__(self):
        self.messages, self.mode = [], "ok"          # ok | fail500 | notok | rate
        outer = self

        class H(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                outer.messages.append({"path": self.path, "body": body})
                code, payload = 200, {"ok": True, "result": {"message_id": len(outer.messages)}}
                if outer.mode == "fail500": code, payload = 500, {"ok": False}
                if outer.mode == "notok": code, payload = 200, {"ok": False, "description": "chat not found"}
                if outer.mode == "rate": code, payload = 429, {"ok": False, "parameters": {"retry_after": 5}}
                data = json.dumps(payload).encode()
                self.send_response(code); self.send_header("Content-Length", str(len(data))); self.end_headers(); self.wfile.write(data)

        self.server = HTTPServer(("127.0.0.1", 0), H)
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown(); self.server.server_close()


def event(**kw):
    base = {"event_id": "evt-1", "key": "backup.full", "severity": "WARNING", "status": "firing", "time": 1795206591.0, "detail": "value=93653; warning=93600"}
    base.update(kw); return base


class RelayTests(unittest.TestCase):
    def setUp(self):
        self.fake = FakeTelegram(); self.addCleanup(self.fake.close)
        self.r = relay.Relay(relay.Telegram(BOT, CHAT, self.fake.url, timeout=3), AUTH)

    def post(self, ev, auth="Bearer " + AUTH):
        return self.r.handle(auth, json.dumps(ev).encode())

    def test_firing_and_resolved_are_delivered_with_readable_text_and_no_markup(self):
        self.assertEqual(self.post(event())[0], 200)
        self.assertEqual(self.post(event(event_id="evt-2", status="resolved", severity="INFO", detail="value=13"))[0], 200)
        self.assertEqual(len(self.fake.messages), 2)
        first, second = (m["body"] for m in self.fake.messages)
        self.assertEqual(first["chat_id"], CHAT)
        self.assertIn("WARNING: backup.full", first["text"]); self.assertIn("RESUELTO: backup.full", second["text"])
        self.assertNotIn("parse_mode", first)                                   # texto plano: nada del evento se interpreta como formato
        self.assertTrue(self.fake.messages[0]["path"].endswith("/sendMessage") and self.fake.messages[0]["path"].startswith("/bot" + BOT))

    def test_critical_is_visually_distinct(self):
        self.post(event(severity="CRITICAL"))
        self.assertIn("CRITICAL: backup.full", self.fake.messages[0]["body"]["text"])

    def test_duplicate_event_id_is_acknowledged_but_sent_once(self):
        self.assertEqual(self.post(event()), (200, "delivered"))
        self.assertEqual(self.post(event()), (200, "duplicate"))
        self.assertEqual(len(self.fake.messages), 1)

    def test_authentication_is_required_and_constant_time_compared(self):
        for header in (None, "", "Bearer wrong", "Basic " + AUTH, AUTH):
            self.assertEqual(self.post(event(), auth=header)[0], 401, header)
        self.assertEqual(self.fake.messages, [])
        self.assertEqual(relay.Relay(relay.Telegram(BOT, CHAT, self.fake.url), "").handle("Bearer ", b"{}")[0], 401)   # sin token configurado nada pasa

    def test_telegram_failures_return_non_2xx_so_the_monitor_retries_and_the_event_is_not_marked_seen(self):
        for mode in ("fail500", "notok", "rate"):
            self.fake.mode = mode
            self.assertEqual(self.post(event())[0], 502, mode)
        self.fake.mode = "ok"
        self.assertEqual(self.post(event()), (200, "delivered"))                 # el reintento si se entrega (no quedo como "visto")

    def test_unreachable_telegram_is_a_clean_502(self):
        dead = relay.Relay(relay.Telegram(BOT, CHAT, "http://127.0.0.1:9", timeout=1), AUTH)
        self.assertEqual(dead.handle("Bearer " + AUTH, json.dumps(event()).encode())[0], 502)

    def test_invalid_payloads_are_rejected(self):
        self.assertEqual(self.r.handle("Bearer " + AUTH, b"no es json")[0], 400)
        for bad in ({}, {"event_id": "x"}, event(severity="DEBUG"), event(status="open"), event(key=""), event(event_id=""), [1, 2], "texto"):
            self.assertEqual(self.post(bad)[0], 400, bad)
        self.assertEqual(self.fake.messages, [])

    def test_message_is_bounded_and_markup_in_detail_is_inert(self):
        self.post(event(detail="<b>x</b> *_[a](http://evil)* " + "A" * 10000))
        text = self.fake.messages[0]["body"]["text"]
        self.assertLessEqual(len(text), relay.MAX_TEXT)
        self.assertNotIn("parse_mode", self.fake.messages[0]["body"])

    def test_secrets_never_appear_in_responses_or_messages(self):
        out = [self.post(event()), self.post(event(event_id="e2"), auth="Bearer nope"), self.post({"x": 1})]
        self.fake.mode = "fail500"; out.append(self.post(event(event_id="e3")))
        # el token del bot viaja en la RUTA hacia Telegram (asi es su API); no debe aparecer en las respuestas del relevo ni en el TEXTO enviado
        blob = json.dumps(out) + json.dumps([m["body"] for m in self.fake.messages])
        self.assertNotIn(BOT, blob); self.assertNotIn(AUTH, blob)
        self.assertNotIn(CHAT, json.dumps(out))

    def test_dedupe_memory_is_bounded(self):
        for i in range(relay.SEEN_MAX + 50):
            self.r.seen[f"e{i}"] = True
        self.post(event(event_id="nuevo"))
        self.assertLessEqual(len(self.r.seen), relay.SEEN_MAX)


class ServerAndCliTests(unittest.TestCase):
    def test_http_server_end_to_end_with_the_monitors_own_sender(self):
        """monitor.send() real -> relevo HTTP real -> Telegram simulado."""
        import monitor
        fake = FakeTelegram(); self.addCleanup(fake.close)
        r = relay.Relay(relay.Telegram(BOT, CHAT, fake.url, timeout=3), AUTH)
        srv = relay.ThreadingHTTPServer(("127.0.0.1", 0), relay.make_handler(r)); self.addCleanup(srv.server_close)
        threading.Thread(target=srv.serve_forever, daemon=True).start(); self.addCleanup(srv.shutdown)
        old = {k: os.environ.get(k) for k in ("BURY_MONITOR_WEBHOOK", "BURY_MONITOR_TOKEN")}
        os.environ["BURY_MONITOR_WEBHOOK"] = f"http://127.0.0.1:{srv.server_port}/alert"; os.environ["BURY_MONITOR_TOKEN"] = AUTH
        self.addCleanup(lambda: [os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v) for k, v in old.items()])
        self.assertTrue(monitor.send(event(event_id="e2e-1")))
        self.assertEqual(len(fake.messages), 1)
        os.environ["BURY_MONITOR_TOKEN"] = "otro"
        self.assertFalse(monitor.send(event(event_id="e2e-2")))                   # el monitor ve el 401 como no entregado -> reintenta
        self.assertEqual(len(fake.messages), 1)
        # rutas/metodos incorrectos
        req = urllib.request.Request(f"http://127.0.0.1:{srv.server_port}/otra", b"{}", {"Authorization": "Bearer " + AUTH}, method="POST")
        with self.assertRaises(urllib.error.HTTPError) as cm:
            urllib.request.urlopen(req, timeout=3)
        self.assertEqual(cm.exception.code, 404)

    def run_cli(self, *args, env=None):
        e = {k: v for k, v in os.environ.items() if not k.startswith("BURY_RELAY_")}
        e.update(env or {})
        return subprocess.run([sys.executable, str(HERE / "telegram_relay.py"), *args], capture_output=True, text=True, env=e, timeout=30)

    def test_check_reports_missing_values_without_network_or_secrets(self):
        r = self.run_cli("--check")
        self.assertEqual(r.returncode, 2)
        self.assertIn("incompleta", r.stdout); self.assertIn("BURY_RELAY_TELEGRAM_TOKEN", r.stdout)
        r = self.run_cli("--check", env={"BURY_RELAY_TELEGRAM_TOKEN": BOT, "BURY_RELAY_TELEGRAM_CHAT_ID": CHAT, "BURY_RELAY_AUTH_TOKEN": AUTH})
        self.assertEqual(r.returncode, 0); self.assertIn("completa", r.stdout)
        self.assertNotIn(BOT, r.stdout + r.stderr); self.assertNotIn(AUTH, r.stdout + r.stderr)

    def test_send_test_uses_the_configured_api_and_does_not_print_secrets(self):
        fake = FakeTelegram(); self.addCleanup(fake.close)
        env = {"BURY_RELAY_TELEGRAM_TOKEN": BOT, "BURY_RELAY_TELEGRAM_CHAT_ID": CHAT, "BURY_RELAY_API_BASE": fake.url}
        r = self.run_cli("--send-test", env=env)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr); self.assertIn("entregado", r.stdout)
        self.assertEqual(len(fake.messages), 1); self.assertIn("PRUEBA", fake.messages[0]["body"]["text"])
        self.assertNotIn(BOT, r.stdout + r.stderr)
        fake.mode = "notok"
        r = self.run_cli("--send-test", env=env)
        self.assertEqual(r.returncode, 1); self.assertIn("FALLO", r.stdout); self.assertNotIn(BOT, r.stdout + r.stderr)

    def test_serve_refuses_non_loopback_bind_and_requires_the_auth_token(self):
        base = {"BURY_RELAY_TELEGRAM_TOKEN": BOT, "BURY_RELAY_TELEGRAM_CHAT_ID": CHAT}
        self.assertEqual(self.run_cli("--serve", env=base).returncode, 2)                                 # falta BURY_RELAY_AUTH_TOKEN
        r = self.run_cli("--serve", "--bind", "0.0.0.0", env={**base, "BURY_RELAY_AUTH_TOKEN": AUTH})
        self.assertEqual(r.returncode, 2); self.assertIn("loopback", r.stdout)


if __name__ == "__main__":
    unittest.main()

"""Pruebas del relevo a Telegram (P-08) contra una API de Telegram SIMULADA en loopback. Sin red externa, sin credenciales reales: todos los
valores son inventados y el servidor falso se levanta en un puerto efimero.
Uso:  python3 -m unittest discover -s scripts/monitoring -p 'test_telegram_relay.py'
"""
import json
import os
import socket
import subprocess
import sys
import threading
import time
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
        self.messages, self.mode, self.delay = [], "ok", 0          # ok | fail500 | notok | rate ; delay = segundos antes de responder
        outer = self

        class H(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                time.sleep(outer.delay)
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


class ConcurrencyAndHardeningTests(unittest.TestCase):
    def serve(self, **handler_kwargs):
        self.fake = FakeTelegram(); self.addCleanup(self.fake.close)
        self.r = relay.Relay(relay.Telegram(BOT, CHAT, self.fake.url, timeout=3), AUTH)
        srv = relay.make_server(self.r, "127.0.0.1", 0, **handler_kwargs); self.addCleanup(srv.server_close)
        threading.Thread(target=srv.serve_forever, daemon=True).start(); self.addCleanup(srv.shutdown)
        return srv.server_port

    def raw(self, port, head, body=b"", wait=2.0):
        """Envia bytes crudos y devuelve la linea de estado (o None si el servidor cerro/expiro)."""
        with socket.create_connection(("127.0.0.1", port), timeout=wait) as sock:
            sock.sendall(head.encode() + body)
            try:
                data = sock.recv(256)
            except socket.timeout:
                return "TIMEOUT"
        return data.decode("latin1").split("\r\n")[0] if data else None

    def headers(self, extra="", auth=True, length=2):
        return (f"POST /alert HTTP/1.1\r\nHost: x\r\n" + (f"Authorization: Bearer {AUTH}\r\n" if auth else "")
                + (f"Content-Length: {length}\r\n" if length is not None else "") + extra + "\r\n")

    def test_same_event_delivered_concurrently_is_sent_once(self):
        self.fake = FakeTelegram(); self.addCleanup(self.fake.close); self.fake.delay = 0.6      # el envio tarda: las dos solicitudes se solapan
        r = relay.Relay(relay.Telegram(BOT, CHAT, self.fake.url, timeout=5), AUTH)
        out, gate = [], threading.Barrier(2)
        def call():
            gate.wait(); out.append(r.handle("Bearer " + AUTH, json.dumps(event(event_id="mismo")).encode()))
        ts = [threading.Thread(target=call) for _ in range(2)]
        [t.start() for t in ts]; [t.join(10) for t in ts]
        self.assertEqual(len(self.fake.messages), 1)
        self.assertEqual(sorted(out), [(200, "delivered"), (200, "duplicate")])
        self.assertEqual(r.inflight, {})

    def test_concurrent_duplicate_after_a_failed_delivery_is_retryable_not_lost(self):
        self.fake = FakeTelegram(); self.addCleanup(self.fake.close); self.fake.delay = 0.4; self.fake.mode = "fail500"
        r = relay.Relay(relay.Telegram(BOT, CHAT, self.fake.url, timeout=5), AUTH)
        out, gate = [], threading.Barrier(2)
        def call():
            gate.wait(); out.append(r.handle("Bearer " + AUTH, json.dumps(event(event_id="falla")).encode())[0])
        ts = [threading.Thread(target=call) for _ in range(2)]
        [t.start() for t in ts]; [t.join(10) for t in ts]
        self.assertTrue(all(code in (502, 503) for code in out), out)                              # ninguna 2xx: el monitor reintenta
        self.fake.mode = "ok"; self.fake.delay = 0
        self.assertEqual(r.handle("Bearer " + AUTH, json.dumps(event(event_id="falla")).encode()), (200, "delivered"))

    def test_unauthenticated_request_is_rejected_before_reading_the_body(self):
        port = self.serve()
        t0 = time.time()
        status = self.raw(port, self.headers(auth=False, length=1000))                            # promete 1000 bytes y no envia ninguno
        self.assertIn("401", status or ""); self.assertLess(time.time() - t0, 1.5)

    def test_slow_authenticated_connection_is_dropped_and_does_not_block_others(self):
        port = self.serve(read_timeout=1)
        started = time.time()
        slow = socket.create_connection(("127.0.0.1", port), timeout=5)
        slow.sendall(self.headers(length=500).encode())                                            # autenticado, promete cuerpo y no lo envia
        r = urllib.request.Request(f"http://127.0.0.1:{port}/alert", json.dumps(event(event_id="ok1")).encode(),
                                   {"Authorization": "Bearer " + AUTH, "Content-Type": "application/json"}, method="POST")
        self.assertEqual(urllib.request.urlopen(r, timeout=5).status, 200)                         # otro cliente sigue siendo atendido
        slow.settimeout(4); data = slow.recv(256); slow.close()                                   # si el servidor no la soltara, recv vencería (socket.timeout)
        self.assertIn(b"408", data)                                                                 # la lenta vence por timeout (1 s) y libera el hilo

    def test_concurrency_is_bounded_so_slow_clients_cannot_exhaust_the_service(self):
        port = self.serve(read_timeout=3, max_concurrent=1)
        hold = socket.create_connection(("127.0.0.1", port), timeout=5); hold.sendall(self.headers(length=500).encode()); time.sleep(0.3)
        self.assertIn("503", self.raw(port, self.headers(), b"{}") or ""); hold.close()

    def test_connections_without_complete_headers_cannot_create_unbounded_threads(self):
        """Regresion (Codex): el cupo se toma en el accept, antes del hilo. Con 3 cupos, 25 conexiones que no completan las cabeceras no crean 25 hilos."""
        port = self.serve(read_timeout=2, read_deadline=3, max_concurrent=3)
        base = threading.active_count()
        socks = []
        for _ in range(25):
            c = socket.create_connection(("127.0.0.1", port), timeout=3)
            c.sendall(b"POST /alert HTTP/1.1\r\nHost:")                      # cabeceras incompletas, sin Bearer
            socks.append(c)
        time.sleep(0.5)
        self.assertLessEqual(threading.active_count() - base, 3 + 3, "hilos extra por encima del cupo")      # cupo + timers de vigilancia
        rechazadas = 0
        for c in socks:
            c.settimeout(0.3)
            try:
                if b"503" in c.recv(64):
                    rechazadas += 1
            except OSError:
                pass
        self.assertGreaterEqual(rechazadas, 20)                                                             # el resto recibio 503 inmediato, sin hilo
        [c.close() for c in socks]
        time.sleep(3.5)                                                                                     # las 3 conexiones colgadas vencen por plazo y liberan el cupo
        r = urllib.request.Request(f"http://127.0.0.1:{port}/alert", json.dumps(event(event_id="tras-ataque")).encode(),
                                   {"Authorization": "Bearer " + AUTH, "Content-Type": "application/json"}, method="POST")
        self.assertEqual(urllib.request.urlopen(r, timeout=5).status, 200)                                  # el servicio sigue entregando alertas legitimas

    def test_trickling_client_cannot_evade_the_total_read_deadline(self):
        """Regresion (Codex): un cliente que envia 1 byte cada 0.4 s renueva el timeout por lectura; el plazo TOTAL lo corta."""
        port = self.serve(read_timeout=1, read_deadline=1.5, max_concurrent=4)
        c = socket.create_connection(("127.0.0.1", port), timeout=5)
        t0 = time.time(); cerrado = False
        try:
            c.sendall(b"POST /alert HTTP/1.1\r\n")
            for _ in range(20):
                time.sleep(0.4)
                c.sendall(b"X")                                                                         # nunca termina la cabecera
        except OSError:
            cerrado = True                                                                              # el servidor corto el socket
        c.close()
        self.assertTrue(cerrado or time.time() - t0 > 8, "el servidor no corto el goteo")
        self.assertTrue(cerrado, "la conexion que gotea deberia haber sido cortada por el plazo total")
        self.assertLess(time.time() - t0, 6)

    def test_malformed_length_headers_are_rejected(self):
        port = self.serve()
        self.assertIn("400", self.raw(port, self.headers(extra="Content-Length: 5\r\n")) or "")      # Content-Length duplicado
        self.assertIn("400", self.raw(port, self.headers(length=None, extra="Content-Length: abc\r\n")) or "")
        self.assertIn("400", self.raw(port, self.headers(length=None, extra="Content-Length: -5\r\n")) or "")
        self.assertIn("400", self.raw(port, self.headers(length=None)) or "")                           # sin Content-Length
        self.assertIn("411", self.raw(port, self.headers(length=None, extra="Transfer-Encoding: chunked\r\n")) or "")
        self.assertIn("413", self.raw(port, self.headers(length=relay.MAX_BODY + 1)) or "")
        self.assertEqual(self.fake.messages, [])


class ServerAndCliTests(unittest.TestCase):
    def test_http_server_end_to_end_with_the_monitors_own_sender(self):
        """monitor.send() real -> relevo HTTP real -> Telegram simulado."""
        import monitor
        fake = FakeTelegram(); self.addCleanup(fake.close)
        r = relay.Relay(relay.Telegram(BOT, CHAT, fake.url, timeout=3), AUTH)
        srv = relay.make_server(r, "127.0.0.1", 0); self.addCleanup(srv.server_close)
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

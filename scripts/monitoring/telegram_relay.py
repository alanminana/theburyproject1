#!/usr/bin/env python3
"""Relevo de alertas del monitor (contrato webhook JSON) hacia Telegram. P-08.

monitor.py entrega cada alerta con `POST` JSON a BURY_MONITOR_WEBHOOK y reintenta (>= 60 s) mientras no reciba 2xx. Este relevo escucha SOLO en
loopback (127.0.0.1) en la misma maquina, valida el token Bearer, deduplica por `event_id`, da formato de texto plano y llama a la API de Telegram
(`sendMessage`). Responde 2xx unicamente cuando Telegram confirma `ok: true`; ante cualquier fallo responde 502/503 para que el monitor reintente
(entrega al-menos-una-vez sin perder avisos). No abre puertos hacia Internet; solo SALE a api.telegram.org por HTTPS (443).

  python3 telegram_relay.py --serve [--port 9098]      # servicio (systemd: bury-telegram-relay.service)
  python3 telegram_relay.py --check                    # valida la configuracion SIN red ni mensajes (exit 0 completa | 2 faltan valores)
  python3 telegram_relay.py --send-test                # envia UN mensaje de prueba a Telegram (confirma token + chat); exit 0 | 1 fallo

Configuracion (variables de entorno, desde /etc/bury-monitor/telegram-relay.env 0600; NUNCA en Git ni en logs):
  BURY_RELAY_TELEGRAM_TOKEN     token del bot (lo crea el propietario con @BotFather)
  BURY_RELAY_TELEGRAM_CHAT_ID   id numerico del chat o grupo destino
  BURY_RELAY_AUTH_TOKEN         mismo valor que BURY_MONITOR_TOKEN (obligatorio con --serve: sin el cualquiera en la maquina podria inyectar avisos)
  BURY_RELAY_API_BASE           solo pruebas (default https://api.telegram.org)
Sin dependencias externas. El token del bot, el chat id y el token Bearer jamas se escriben en logs, respuestas ni mensajes.
"""
import argparse
import hmac
import json
import os
import socket
import sys
import threading
import time
import urllib.error
import urllib.request
from collections import OrderedDict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

API_BASE = "https://api.telegram.org"
MAX_BODY = 16 * 1024
MAX_TEXT = 3500                       # Telegram admite 4096; margen para el encabezado
SEEN_MAX = 2000                       # event_id recordados (dedupe en memoria; el monitor reintenta solo ante no-2xx)
ICON = {"CRITICAL": "\U0001F534", "WARNING": "\U0001F7E0", "INFO": "\U0001F7E2"}


def env(name, default=""):
    return os.environ.get(name, default).strip()


def format_event(event, host=None):
    """Texto plano (sin parse_mode: nada del evento se interpreta como formato). Devuelve None si el evento no cumple el contrato."""
    if not isinstance(event, dict):
        return None
    key, severity, status = event.get("key"), event.get("severity"), event.get("status")
    if not (isinstance(key, str) and key and isinstance(event.get("event_id"), str) and event["event_id"]):
        return None
    if severity not in ("CRITICAL", "WARNING", "INFO") or status not in ("firing", "resolved"):
        return None
    head = ("RESUELTO" if status == "resolved" else severity)
    icon = ICON["INFO"] if status == "resolved" else ICON[severity]
    when = ""
    try:
        when = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(float(event.get("time"))))
    except (TypeError, ValueError, OverflowError, OSError):
        pass
    detail = str(event.get("detail", ""))[:1500].replace("\r", " ")
    lines = [f"{icon} {head}: {key[:120]}", detail, f"{host or socket.gethostname()} {when}".strip()]
    return "\n".join(l for l in lines if l)[:MAX_TEXT]


class Telegram:
    def __init__(self, token, chat_id, base=API_BASE, timeout=10):
        self.token, self.chat_id, self.base, self.timeout = token, chat_id, base.rstrip("/"), timeout

    def send(self, text):
        """True solo si Telegram responde ok. Nunca incluye el token en excepciones ni mensajes."""
        url = f"{self.base}/bot{self.token}/sendMessage"
        body = json.dumps({"chat_id": self.chat_id, "text": text, "disable_web_page_preview": True}).encode()
        req = urllib.request.Request(url, body, {"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as response:
                data = json.loads(response.read().decode("utf8", "replace") or "{}")
                return 200 <= response.status < 300 and data.get("ok") is True
        except Exception:      # HTTPError (4xx/5xx, 429 incluido), red, timeout, JSON invalido: todo es "no entregado" -> el monitor reintenta
            return False


class Relay:
    def __init__(self, telegram, auth_token):
        self.telegram, self.auth_token = telegram, auth_token
        self.seen = OrderedDict()
        self.inflight = {}          # event_id -> threading.Event: una sola solicitud envia; las solapadas esperan su resultado
        self.lock = threading.Lock()

    def authorized(self, header):
        if not self.auth_token:
            return False
        expected = "Bearer " + self.auth_token
        return hmac.compare_digest((header or "").encode(), expected.encode())

    def handle(self, authorization, raw):
        """-> (http_status, mensaje corto sin secretos)"""
        if not self.authorized(authorization):
            return 401, "unauthorized"
        try:
            event = json.loads(raw.decode("utf8"))
        except Exception:
            return 400, "invalid json"
        text = format_event(event)
        if text is None:
            return 400, "invalid event"
        event_id = event["event_id"]
        with self.lock:
            if event_id in self.seen:
                return 200, "duplicate"
            waiting = self.inflight.get(event_id)
            if waiting is None:
                self.inflight[event_id] = threading.Event()      # esta solicitud hace el envio
        if waiting is not None:
            # misma entrega solapada: esperar a la que esta enviando; si ella tuvo exito esto es un duplicado, si no, el monitor reintenta
            waiting.wait(timeout=self.telegram.timeout + 5)
            with self.lock:
                return (200, "duplicate") if event_id in self.seen else (503, "delivery in progress or failed; retry")
        try:
            delivered = self.telegram.send(text)
            with self.lock:
                if delivered:
                    self.seen[event_id] = True
                    while len(self.seen) > SEEN_MAX:
                        self.seen.popitem(last=False)
        finally:
            with self.lock:
                self.inflight.pop(event_id).set()
        return (200, "delivered") if delivered else (502, "telegram delivery failed")


MAX_CONCURRENT = 8                    # TasksMax=16 en la unidad systemd: nunca mas de la mitad ocupada por conexiones lentas
READ_TIMEOUT = 5                      # segundos para recibir cabeceras y cuerpo; una conexion que no avanza se descarta


def make_handler(relay, slots=None, read_timeout=READ_TIMEOUT):
    slots = slots or threading.BoundedSemaphore(MAX_CONCURRENT)

    class Handler(BaseHTTPRequestHandler):
        server_version = "bury-relay"
        sys_version = ""
        timeout = read_timeout            # socket de la conexion (slowloris): vence la lectura y se libera el hilo

        def log_message(self, format, *args):   # noqa: A002 - sin logs de acceso: la URL/cabeceras podrian arrastrar secretos
            pass

        def reply(self, status, msg):
            payload = json.dumps({"result": msg}).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(payload)
            self.close_connection = True

        def do_POST(self):
            if not slots.acquire(blocking=False):
                return self.reply(503, "busy")
            try:
                if self.path != "/alert":
                    return self.reply(404, "not found")
                # 1) autenticar ANTES de leer el cuerpo: un proceso sin el Bearer no retiene el hilo ni hace leer bytes
                if not relay.authorized(self.headers.get("Authorization")):
                    return self.reply(401, "unauthorized")
                # 2) Content-Length unico, numerico y acotado; sin Transfer-Encoding (no se soporta chunked)
                lengths = self.headers.get_all("Content-Length") or []
                if self.headers.get("Transfer-Encoding") is not None:
                    return self.reply(411, "length required")
                if len(lengths) != 1 or not lengths[0].strip().isdigit():
                    return self.reply(400, "invalid content-length")
                length = int(lengths[0])
                if length <= 0 or length > MAX_BODY:
                    return self.reply(413, "body size")
                try:
                    raw = self.rfile.read(length)
                except OSError:           # timeout de lectura / conexion cortada
                    return self.reply(408, "request timeout")
                if len(raw) != length:
                    return self.reply(400, "incomplete body")
                status, msg = relay.handle(self.headers.get("Authorization"), raw)
                self.reply(status, msg)
            except OSError:
                self.close_connection = True
            finally:
                slots.release()

        def do_GET(self):
            self.reply(405, "method not allowed")
    return Handler


def config_problems(serve):
    missing = [n for n in ("BURY_RELAY_TELEGRAM_TOKEN", "BURY_RELAY_TELEGRAM_CHAT_ID") if not env(n)]
    if serve and not env("BURY_RELAY_AUTH_TOKEN"):
        missing.append("BURY_RELAY_AUTH_TOKEN")
    return missing


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--serve", action="store_true"); ap.add_argument("--check", action="store_true"); ap.add_argument("--send-test", action="store_true")
    ap.add_argument("--port", type=int, default=9098); ap.add_argument("--bind", default="127.0.0.1")
    args = ap.parse_args(argv)
    if not (args.serve or args.check or args.send_test):
        ap.print_help(); return 2
    if args.check or args.send_test or args.serve:
        missing = config_problems(args.serve)
        if args.check:
            print(json.dumps({"configuracion": "completa" if not missing else "incompleta", "faltan": missing}), flush=True)
            return 2 if missing else 0
        if missing:
            print(json.dumps({"error": "faltan variables", "faltan": missing}), flush=True); return 2
    telegram = Telegram(env("BURY_RELAY_TELEGRAM_TOKEN"), env("BURY_RELAY_TELEGRAM_CHAT_ID"), env("BURY_RELAY_API_BASE") or API_BASE)
    if args.send_test:
        ok = telegram.send(f"\U0001F7E2 PRUEBA de canal de alertas de TheBuryProject ({time.strftime('%Y-%m-%d %H:%M:%S')}): no es un incidente.")
        print(json.dumps({"telegram": "entregado" if ok else "FALLO"}), flush=True)
        return 0 if ok else 1
    if args.bind not in ("127.0.0.1", "::1", "localhost"):
        print(json.dumps({"error": "el relevo solo escucha en loopback"}), flush=True); return 2
    relay = Relay(telegram, env("BURY_RELAY_AUTH_TOKEN"))
    server = ThreadingHTTPServer((args.bind, args.port), make_handler(relay))
    print(json.dumps({"relay": "escuchando", "bind": args.bind, "port": args.port}), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())

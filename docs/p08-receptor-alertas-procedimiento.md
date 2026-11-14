# P-08 — Conectar el receptor externo de alertas (procedimiento)

Estado 2026-11-14: **preparado, no conectado**. Este documento no contiene credenciales ni URLs reales; los valores van solo en
`/etc/bury-monitor/monitor.env` (0600, fuera de Git). Diseño y umbrales: `monitoreo-alertas.md`; activación del host: `offsite-monitoring-activacion.md` §4.

## Qué hay que decidir antes (propietario)

| Decisión | Opciones | Efecto |
|---|---|---|
| Canal de aviso | correo, Telegram, WhatsApp (vía proveedor), webhook propio | Lo entrega el receptor; el agente solo hace `POST` JSON |
| Receptor | (a) Uptime Kuma con Push + canal nativo, (b) servicio HTTP propio que acepte el contrato JSON | (a) cubre también "cayó todo el servidor"; (b) solo recibe lo que el agente envía |
| Observador externo | otro servidor, otro sitio, otra red eléctrica | Sin él, la caída total del host/energía/router **no avisa a nadie** |

## Contrato que debe cumplir el receptor del webhook

`POST` con `Content-Type: application/json` y, si se configura `BURY_MONITOR_TOKEN`, `Authorization: Bearer <token>`. Cuerpo:
`{"event_id","key","severity":"WARNING|CRITICAL|INFO","status":"firing|resolved","time","detail"}`. Responder **2xx** para confirmar
(timeout del agente: 8 s). Entrega *al menos una vez*: deduplicar por `event_id`. Cualquier otro código se reintenta (≥ 60 s, en orden).

## Pasos (en el host definitivo, sin tocar el ERP)

1. Crear el receptor y obtener la URL HTTPS de entrada (y el token si lo exige). Para el heartbeat: monitor **Push** de Kuma en *otro host*, 180 s, 1 reintento.
2. Escribir los valores en `/etc/bury-monitor/monitor.env` (ver `scripts/monitoring/monitor.env.example`): `BURY_MONITOR_WEBHOOK`, `BURY_MONITOR_TOKEN` (opcional), `BURY_MONITOR_HEARTBEAT`.
3. **Probar el canal sin generar incidentes** (envía `monitor.test` firing + resolved, y un ping `up` al heartbeat; no imprime URLs ni tokens):
   ```bash
   sudo bash -c 'set -a; . /etc/bury-monitor/monitor.env; set +a; python3 /opt/theburyproject/scripts/monitoring/monitor.py --test-channel'
   echo $?   # 0 = webhook y heartbeat entregados · 1 = alguna entrega falló · 2 = no hay webhook configurado
   ```
   Confirmar en el receptor que llegaron **dos** eventos (`firing` y `resolved`) con `event_id` distintos y que el aviso humano se recibió en el canal elegido.
4. `sudo systemctl restart bury-monitor` y verificar en `journalctl -u bury-monitor` que `collector.channel` dejó de ser `WARNING` (webhook configurado = True).
5. Prueba de fallo real y controlada (ventana de mantenimiento): `docker compose stop caddy` ≈ 3 min → debe llegar `http.live`/`docker.caddy` CRITICAL; `docker compose start caddy` → `resolved`. Repetir con `app` y `db`.
6. Observador externo: crear los monitores de `monitoreo-alertas.md` ("Uptime Kuma: observador externo") y **apagar el agente** (`systemctl stop bury-monitor`) 4 min para ver que el Push vencido avisa desde fuera; volver a iniciarlo.
7. Registrar fecha, canal, quién recibió el aviso y el resultado en `monitoreo-validacion.md`.

## Qué queda cubierto sin receptor (ya probado en CI o en entorno descartable)

| Caso | Prueba |
|---|---|
| App, SQL y Caddy: caída y recuperación con `resolved` y sin tormenta de repetidos | `scripts/monitoring/isolated_smoke.py` (Compose aislado; Windows 11 y Ubuntu) |
| Offsite fallido, restore-test local/offsite fallido, custodia fallida → CRITICAL, secreto ausente del evento, `resolved` al recuperar | `scripts/monitoring/test_status_chain.py` (CI): productores reales de `scripts/backup` + monitor + alertas |
| Umbrales, ventanas, cola con receptor caído, canal de prueba | `scripts/monitoring/test_monitor.py` (CI) |
| `Check-ErpHealth.ps1` (Windows/VM): 27 + 17 escenarios simulados | `Test-ErpHealth.ps1` (job `windows-scripts`, obligatorio) |

## Cierre de P-08 en el servidor definitivo — qué falta

1. Decisión del propietario sobre canal y receptor (hoy: "ninguna alerta por ahora").
2. Pasos 1-7 de arriba con credenciales reales, y evidencia de que el aviso humano llegó.
3. Observador externo operativo con heartbeat (cobertura de caída total).
4. `Check-ErpHealth.ps1` ejecutado contra el servidor vivo (ruta `wsl.exe`/SSH → `monitor-status`, `curl.exe --resolve`) y programado.
5. Umbrales de CPU/RAM/disco ajustados con carga real y varias semanas de histórico.
6. Offsite real (proveedor) para que `offsite-sql`, `offsite-restore-test` y custodia se prueben contra el remoto de verdad.

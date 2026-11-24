# P-08: activación de los canales reales (guía del propietario) y estado de cierre (2026-11-21)

> Nota (2026-11-24): la VM `tbp` quedó fuera del plan; donde esta guía dice «servidor» se refiere al servidor de pruebas (notebook Ubuntu) o al servidor definitivo, aún por definir. Los resultados fechados de abajo son los emitidos en su momento.

Base: `main` f7c51e07. Complementa `p08-cierre-operativo-20261120.md` y `p08-receptor-alertas-procedimiento.md`. Sin credenciales en este documento.
**No se crearon bots, cuentas ni servicios externos, no se generaron credenciales, no se tocó Tailscale ni la Raspberry, no se apagó Windows/VMware.**

> **Estado P-08: FINALIZADO CON EXCEPCIÓN DOCUMENTADA (2026-11-21, aceptada por el propietario; ver `docs/monitoreo-validacion.md`).** Histórico previo: EN PROGRESO. Todo lo que no requiere recursos del propietario está implementado y probado; faltan dos evidencias que solo existen con
> una cuenta suya: (1) un aviso **recibido por una persona** y (2) la detección de **caída total del host desde un observador externo real**.

## 1. Qué se agregó en esta tanda

- `scripts/monitoring/activar-canales.sh`: activación guiada y segura (un solo comando, ver §2). Lee los secretos sin eco, valida formato, escribe `/etc/bury-monitor/*.env` con modo 0600 (root) y copia previa, instala la unidad del relevo, envía **un** mensaje real de prueba a Telegram **antes** de habilitar el servicio, prueba el canal completo (`monitor.py --test-channel`) y reinicia el monitor. Sin valores no activa nada (exit 3). Pruebas en CI (`test_monitor.py::ActivarCanalesTests`): validación, `--dry-run` sin escrituras, permisos 0600, preservación de otras claves, ningún secreto en la salida.
- `BURY_MONITOR_HEARTBEAT_STYLE=healthchecks` en `monitor.py`: el heartbeat saliente funciona con **Healthchecks.io y servicios compatibles** (ping por URL; un fallo del recolector pega a `/fail`; la ausencia de ping la detecta el propio servicio). El estilo por defecto sigue siendo Uptime Kuma Push. Pruebas en `test_monitor.py`.
- Reutilizado sin cambios: monitor de 60 s, alertas CRITICAL/`resolved`, relevo `telegram_relay.py` (deduplicación por `event_id`, reintentos con 502, 127.0.0.1:9098, sin secretos en logs), unidades systemd (`systemd-analyze verify` limpio), `isolated_smoke.py`, `test_status_chain.py`.

## 2. Qué debe hacer el propietario (mínimo)

### A. Telegram (5 minutos)
1. En Telegram buscar `@BotFather` → `/newbot` → elegir nombre → **copiar el token** (formato `123456789:AAH…`). No compartirlo por chat ni correo.
2. Crear un grupo (o usar su chat personal), **agregar el bot** y escribir cualquier mensaje al bot/grupo.
3. Abrir en un navegador `https://api.telegram.org/bot<TOKEN>/getUpdates` (reemplazando `<TOKEN>`): en la respuesta, `"chat":{"id": …}` es el **chat_id** (los grupos son negativos, p. ej. `-100…`).
4. En el servidor de pruebas (notebook Ubuntu) o, más adelante, en el servidor definitivo (por definir): `sudo bash /opt/theburyproject/scripts/monitoring/activar-canales.sh --telegram` y pegar token y chat_id cuando los pida. Debe llegar un mensaje de prueba a Telegram; confirmarlo.

### B. Observador externo (heartbeat saliente, nada expuesto a Internet)
Recomendado: **Healthchecks.io** (plan gratuito; recibe un ping HTTPS *saliente* cada minuto desde el servidor y avisa por Telegram/correo cuando **dejan de llegar** o vuelven; funciona aunque el servidor y su host estén apagados porque corre en la nube). Alternativas equivalentes: Better Stack / Cronitor / Uptime Kuma en otro sitio (estilo `kuma`).
1. Crear cuenta en healthchecks.io → *Add Check* → nombre «ERP tbp», **Period 2 minutes, Grace 3 minutes** (el monitor pega cada 60 s).
2. En *Integrations* del check agregar **Telegram** (vincula un chat; puede ser el mismo del punto A) y, opcional, correo.
3. Copiar la **ping URL** del check (`https://hc-ping.com/<uuid>`). Tratarla como secreto.
4. En el servidor (de pruebas o definitivo): `sudo bash /opt/theburyproject/scripts/monitoring/activar-canales.sh --heartbeat` y pegar la URL (estilo `healthchecks`).
(Los pasos exactos de la interfaz de un servicio de terceros pueden variar: seguir su documentación; lo que importa es *period* mayor que 60 s y *grace* de unos minutos.)

### C. Prueba con persona real (30 minutos, con su autorización)
1. `docker compose stop caddy` (o `app`) en el servidor durante ~4 minutos → debe llegar a Telegram «CRITICAL http…»; `docker compose start` → «RESUELTO». Registrar fecha, canal y quién lo vio en `monitoreo-validacion.md`.
2. Para la caída total: **solo con su autorización**, apagar el servidor (o desconectarlo de la red) ~6 minutos → Healthchecks debe avisar «DOWN» por Telegram; al encenderla, «UP». Esto es lo que prueba la independencia del observador.

## 3. Lo comprobado hoy (evidencia real, sin canal humano)

Prueba controlada en la preproducción sintética de la VM (`tbp`, monitor real, receptor de ensayo local `alert-sink`; **no se modificó ninguna base de datos**; la app se detuvo y se volvió a iniciar):

| Hecho | Resultado |
|---|---|
| `docker stop` de la app a las 12:59:25 | `http.erp` **CRITICAL** a los **230 s** (≈ 4 ciclos: ventana de sostenimiento de la señal + ciclo de 60 s) |
| `docker start` a las 13:03:16 | `resolved` de `http.erp` a los **60 s**; app `healthy` |
| Eventos de la prueba | 8, con 8 `event_id` distintos (`docker.app`, `http.erp`, `http.live`, `http.ready`: cada uno firing + resolved) → **sin duplicados** |
| Estado final | app, caddy y db sanos; `/health/ready` = 200 |

Otras fallas ya probadas y que **corren en CI**: SQL Server caído y recuperado, Caddy, disco/volúmenes, backup fallido y backup diario sin lock (`backup.run-*` ≤ 60 s), offsite/restore-test/custodia, cola con receptor caído, `resolved` (`isolated_smoke.py`, `test_status_chain.py`, `test_monitor.py`: 72/72 en Linux). Backup real de las 02:30 ART del 21/11 (05:30Z): **OK en 28 s**. Desde la VM las únicas alertas activas siguen siendo las **legítimas** `backup.offsite-sql` y `backup.offsite-files` (no hay copia externa, P-06): se dejan encendidas a propósito.

**Alertas que llegaron a un destinatario real: ninguna** (no existe todavía bot ni observador). Las pruebas con receptor/observador de ensayo (Uptime Kuma, 2026-11-19) demostraron el mecanismo pero no cuentan como recepción humana.

## 4. Pendiente (solo el propietario)

1. Token y chat_id de Telegram (§2.A) → ejecutar `activar-canales.sh --telegram`.
2. Cuenta y check en el observador externo (§2.B) → `activar-canales.sh --heartbeat`.
3. Autorizar la prueba con persona real y la caída total simulada (§2.C).
4. Offsite real de respaldos (P-06) para que se apaguen las dos alertas legítimas.
5. Ajustar umbrales de CPU/RAM/disco con semanas de carga real.

Cuando 1–3 estén hechos y registrados en `monitoreo-validacion.md`, P-08 pasa a **Finalizado**.

## 5. Resultado final (2026-11-21)

Telegram y Healthchecks fueron configurados por el propietario. Caída/recuperación del ERP: probada con recepción humana. Heartbeat: enviado cada 60 s e interrupción real comprobada; **el DOWN/UP externo de Healthchecks no pudo confirmarse** (el historial de pings rotó y no hay clave de API autorizada para `flips`). Excepción aceptada y registrada; pendiente de seguridad: rotar la URL de heartbeat expuesta.

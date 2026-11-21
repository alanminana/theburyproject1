# P-08: estado operativo, detección del backup diario, canal Telegram y observador externo (2026-11-20)

Base: `main` 58592023 + #338 desplegado en la VM `tbp`. Complementa `monitoreo-alertas.md`, `monitoreo-validacion.md`, `p08-receptor-alertas-procedimiento.md` y `p08-check-erphealth.md`.
Sin credenciales reales en este documento. **No se crearon bots, cuentas ni servicios externos, no se generaron credenciales, no se publicó el ERP en Internet y no se apagó ni reinició nada.**

> **Veredicto: P-08 NO está finalizado.** Faltan dos evidencias que solo existen con recursos del propietario: (1) **un aviso recibido por una persona** en un canal real y (2) **detección de la caída
> completa del PC anfitrión desde un observador independiente**. Todo lo que se puede dejar listo y probado sin esos recursos está hecho (§5–§6).

## 1. Qué controles funcionan hoy de verdad (VM, 2026-11-20)

`bury-monitor` activo desde el 19/11 17:43, ciclo de 60 s (3,0–4,4 s por ciclo, 59–89 señales), `delivery.failed=false`. Señales vivas (lectura del estado, sin cambiar nada):

| Área | Señales | Estado |
|---|---|---|
| Aplicación y Caddy | `http.live`, `http.ready` (vía Caddy/TLS), contenedores `app`/`caddy`/`db` (estado, health, reinicios, OOM latcheado, despliegue) | INFO |
| SQL Server | conexión, edición Express (% de 10 GB), crecimiento de datos/log, tamaño de log | INFO |
| Almacenamiento | `/`, `DockerRootDir`, `BACKUP_DIR` y 7 volúmenes: porcentaje, GiB libres, crecimiento diario (una señal por dispositivo) | INFO |
| Recursos y TLS | CPU/RAM por servicio y del host, vida restante de la hoja de Caddy (fracción de vida: hojas de 12 h) | INFO |
| Respaldos | `full`, `files`, `log` (antigüedad de la última verificación OK), `run-<modo>` (código de la última corrida), `restore-test`, custodia, copia extraíble, offsite | ver abajo |

Estado de alertas: 112 señales en INFO y **2 CRITICAL activas, legítimas: `backup.offsite-sql` y `backup.offsite-files`** («verified success missing»): no hay copia externa (P-06). Se repiten cada hora
por el *cooldown* (3600 s); **se dejan encendidas a propósito** (silenciarlas ocultaría una brecha real). El historial del 20/11 muestra además el ciclo completo de la alerta del backup diario (§2).

## 2. El backup diario omitido (#338): ¿lo detectó el monitor?

**Sí, pero tarde.** Historial real del monitor: `backup.full` y `backup.files` pasaron a WARNING a las **17:29 ART** (`value=93653 s` ≈ 26,0 h; ventana de aviso 93 600 s) y volvieron a INFO (`resolved`) a las 18:01 tras el `all` manual.
El cron fallido fue a las 02:30: **~15 h entre el fallo y el aviso**, porque un `all` que no obtenía el lock salía con código 4 sin publicar nada (solo la antigüedad del último full lo delataba).

Corrección (esta tanda): si un `backup.sh all/full/files` no obtiene el lock dentro de `BACKUP_LOCK_WAIT_SEC`, ahora además de `ERROR` en stderr **publica `monitor-status/run-<modo>.json` con exit 4**; el monitor lo ve en el
siguiente ciclo (≤ 60 s) como `backup.run-<modo>` **CRITICAL** y emite `resolved` cuando el siguiente `all` exitoso lo sobrescribe. El modo `log` no publica (se omite por diseño y el siguiente corre en 15 min).
Pruebas: `test_status_chain.py::test_daily_backup_that_could_not_take_the_lock_fires_within_one_cycle_and_recovers` (productor real `backup.sh` + monitor real: firing → resolved) y comprobación manual con un lock retenido.

**Cron real de las 02:30 después de #338:** al momento de esta tanda (21–22 h ART del 20/11) la corrida de las 02:30 del 21/11 **todavía no ocurrió**, por lo que **no se puede afirmar** nada sobre ella. Sí se ejecutó en la VM el
escenario exacto que la rompía: `backup.sh log` y `backup.sh all` lanzados en el mismo segundo → el `all` registró «otra ejecución en curso: espero hasta 180 s», esperó 2 s y terminó **OK en 21 s**
(nuevo set `files`+`full`, `full.json`/`files.json`/`run-all.json` con `exit 0`). **Comprobación pendiente (≈ mañana):** `grep -a "backup 'all'" /srv/bury-backups/logs/backup.log` debe mostrar `inicio` y `OK` a las 05:30 UTC (02:30 ART).

## 3. Tiempos de detección, severidades y deduplicación (por diseño y medidos)

Ciclo 60 s; una señal exige `hold` segundos sostenidos antes de emitir (`Alerts.update`), y un incidente no se resuelve por muestras ausentes.

| Falla | Detección | Severidad | Evidencia |
|---|---|---|---|
| Caddy / app / SQL caídos (`http.*`, `docker.*`) | ≈ 2–3 min (`hold` 120 s + ciclo) | CRITICAL | `isolated_smoke.py` (CI) y `kill -9` en la VM (matriz P-04..P-08) |
| Observador externo Kuma (HTTP a `/health/live` y `/health/ready`) | ≈ 76 s medidos | DOWN | matriz P-04..P-08 |
| Agente muerto / PC caído (heartbeat *push*) | ≈ 4 min (180 s + 1 reintento) | DOWN en el observador | prueba con Kuma (matriz) |
| Backup diario que no corrió (lock): `backup.run-all` | **≤ 60 s** (nuevo) | CRITICAL | §2 |
| Backup diario que corrió pero falló | ≤ 60 s (`run-*` exit ≠ 0) | CRITICAL | `test_status_chain.py` |
| Backup `full`/`files` no verificado | 26 h WARNING, 30 h CRITICAL (red de seguridad) | WARNING→CRITICAL | §2 (17:29 ART) |
| Backup `log` | 30 min WARNING, 45 min CRITICAL | | `config.json` |
| Disco / volumen / CPU / RAM | 0 s–5 min según señal (`hold` 0/180/300 s) | WARNING→CRITICAL | `monitor.py` |

Deduplicación: cada transición `firing`/`resolved` lleva un `event_id` (UUID); mientras la severidad no cambia, el monitor **repite** como máximo una vez por *cooldown* (1 h) y solo para WARNING/CRITICAL; el receptor deduplica por `event_id`
(el relevo de Telegram lo hace en memoria, §5). Con el receptor caído los avisos quedan en cola (máx. 100 por señal, conservando el primero y los últimos) y se reintentan en orden (≥ 60 s).

## 4. Qué NO está probado (y por qué)

- Aviso a una **persona** (Telegram/correo): no existe canal ni bot (recurso del propietario).
- Caída **completa del host** detectada por un observador **independiente** fuera del PC: no existe observador externo. El Uptime Kuma de ensayo corrió en el mismo PC anfitrión (otra VM/contenedor): sirve para el
  mecanismo, **no** para la independencia (si el PC se apaga, también se apaga el observador).
- Umbrales de CPU/RAM/disco con semanas de carga real (producción).
- Corrida real del cron de las 02:30 posterior a #338 (§2: aún no ocurrió).

## 5. Canal de notificaciones: relevo a Telegram (preparado y probado, **sin credenciales**)

`monitor.py` entrega por webhook JSON (contrato en `p08-receptor-alertas-procedimiento.md`). Telegram no acepta ese contrato, así que se agregó un relevo mínimo **en la misma VM**:
`scripts/monitoring/telegram_relay.py` (sin dependencias; unidad `bury-telegram-relay.service`; ejemplo `telegram-relay.env.example`).

- Escucha **solo en 127.0.0.1:9098** (se niega a otra dirección) y **sale** a `api.telegram.org:443`; no abre nada hacia Internet.
- Exige `Authorization: Bearer` (el mismo valor que `BURY_MONITOR_TOKEN`; comparación en tiempo constante); sin token configurado nada pasa.
- Deduplica por `event_id`; texto plano (sin `parse_mode`: nada del evento se interpreta como formato), acotado a 3.500 caracteres; ícono por severidad y «RESUELTO» al recuperarse.
- Responde 2xx **solo si Telegram confirma `ok:true`**; ante cualquier fallo (red, 5xx, 429, `ok:false`) responde 502 → el monitor reintenta y el evento **no** queda marcado como entregado.
- El token del bot, el chat id y el Bearer **nunca** se imprimen (ni en respuestas, ni en logs, ni en mensajes); no hay *access log*. Unidad con `DynamicUser`, `ProtectSystem=strict`, sin red salvo IPv4/IPv6.
- Endurecido tras la revisión de Codex: autentica **antes** de leer el cuerpo, timeout de lectura (5 s) y **plazo total** de lectura (10 s; un cliente que gotea bytes no lo evade), máximo 8 conexiones simultáneas con el cupo tomado **en el `accept`, antes de crear el hilo** (503 inmediato si se agotan; 25 conexiones sin cabeceras completas no crean 25 hilos), `Content-Length` único/numérico/acotado (16 KiB) y sin `chunked`, y deduplicación también para entregas **solapadas** del mismo `event_id` (un solo mensaje; si falla el envío, ninguna recibe 2xx).
- Pruebas (`test_telegram_relay.py`, 22, dentro de `test_monitor.py`: **corren en CI**) contra una API de Telegram simulada en loopback, incluida la cadena `monitor.send()` real → relevo HTTP real → Telegram simulado.

**Lo que debe hacer el propietario (nada de esto lo hice yo):**
1. En Telegram, con `@BotFather`, crear un bot (token) y obtener el `chat_id` del chat o grupo destino (escribirle al bot / agregarlo al grupo y consultar `getUpdates`).
2. En la VM: copiar `telegram-relay.env.example` a `/etc/bury-monitor/telegram-relay.env` (root, 0600) con token, chat id y un `BURY_RELAY_AUTH_TOKEN` aleatorio; en `/etc/bury-monitor/monitor.env`: `BURY_MONITOR_WEBHOOK=http://127.0.0.1:9098/alert` y `BURY_MONITOR_TOKEN=<mismo valor>`.
3. `python3 telegram_relay.py --check` (solo valida variables) → `--send-test` (un mensaje real de prueba) → `systemctl enable --now bury-telegram-relay` → `monitor.py --test-channel` (firing + resolved) → `systemctl restart bury-monitor`.
4. Provocar un incidente real controlado (`docker compose stop caddy` ≈ 3 min) y **confirmar que la persona lo recibió**; registrar fecha, canal y quién en `monitoreo-validacion.md`.

**Propuesta para el CI (requiere tu aprobación, no se aplicó):** el workflow ejecuta solo `test_monitor.py`/`test_status_chain.py`; las pruebas del relevo ya corren porque `test_monitor.py` las importa. No hace falta tocar `ci.yml`.

## 6. Observador externo de caída total del host (alternativas; ninguna contratada)

Principio: el observador **no necesita ver el ERP**; basta un *dead-man's switch*: el agente hace un `GET` saliente periódico (`BURY_MONITOR_HEARTBEAT`) y el observador avisa si **deja de llegar**. No se publica nada en Internet y
no se abre ningún puerto de entrada. (El monitor envía `?status=up|down&msg=…`: Kuma lo entiende; Healthchecks.io y similares lo tratan como un *ping* y solo detectan la **ausencia**, no el estado `down` del recolector.)

| Opción | Costo / cuenta | Independencia | Observaciones |
|---|---|---|---|
| **Uptime Kuma 2.5.5 (push)** en un equipo de **otro sitio** (p. ej. la Raspberry Pi, si está en otra ubicación y red eléctrica) | sin costo; usa un equipo existente | Alta si está en otro sitio; **nula** si está en el mismo local y misma red eléctrica | Ya probado (mecanismo): `scripts/qa/p08-observador/kuma_observador.py`; el Telegram nativo de Kuma sirve de canal. Requiere que ese equipo alcance Internet/Tailscale y su propia alimentación |
| **Healthchecks.io** (SaaS, plan gratuito) | cuenta gratuita (propietario) | Total (otro proveedor) | Ping saliente cada 60 s con *grace*; integra Telegram/correo. Solo ausencia de ping, no `down` explícito |
| **UptimeRobot / Better Stack** (heartbeat) | cuenta; plan gratuito limitado | Total | Equivalente; verificar que el plan gratuito admita *heartbeat* (a veces solo HTTP, que exigiría exponer el ERP: **no**) |
| VPS barato propio con Kuma | costo mensual | Alta | Más mantenimiento; no recomendado como primera opción |

Recomendación: **Healthchecks.io (o Kuma en la Raspberry si está en otro sitio) con Telegram como canal**; es lo único que cubre apagón, caída del router y del PC a la vez. Con el heartbeat configurado, la prueba de cierre es:
apagar el agente ≈ 4 min (o cortar el PC en una ventana autorizada) y registrar **la hora y quién recibió el aviso**.

## 7. Estado de los criterios de cierre de P-08

| Criterio | Estado |
|---|---|
| Monitoreo de app, SQL, Caddy, almacenamiento y respaldos funcionando | **Hecho y probado** (VM real + CI) |
| Alertas ante fallo y `resolved` al recuperar, sin tormenta | **Hecho y probado** (matriz P-04..P-08; `isolated_smoke.py`; `test_status_chain.py`) |
| Detección del backup diario omitido (#338) | **Mejorada y probada** (de ~15 h a ≤ 60 s para el caso de lock) |
| Cron real de las 02:30 posterior a #338 | **Pendiente de observar** (ocurre en la madrugada del 21/11) |
| Relevo a Telegram | **Preparado y probado con API simulada**; sin bot/credenciales |
| Aviso recibido por una persona | **PENDIENTE (propietario)** |
| Observador independiente detecta la caída completa del host | **PENDIENTE (propietario)** |
| Umbrales con carga real | **PENDIENTE (producción)** |

## 8. Estado propuesto para Notion

> **P-08 — NO finalizado (2026-11-20).** Monitoreo operativo en la VM (60 s; app, SQL, Caddy, discos, respaldos), alertas y recuperación probadas. El monitor detectó el backup diario omitido a las 26 h; se corrigió para que un
> `all` que no obtiene el lock se publique y alerte en ≤ 60 s (CRITICAL `backup.run-all`, con `resolved`); el cron real de las 02:30 se confirma en la madrugada del 21/11. Preparado y probado (API simulada) el relevo
> a Telegram (127.0.0.1:9098, solo salida a api.telegram.org). **Faltan (propietario):** crear el bot y el chat de Telegram, un observador externo con *heartbeat* (Healthchecks.io o Kuma en otro sitio) y la prueba con aviso
> recibido por una persona + caída completa del host.

# Monitoreo y alertas del ERP

## Decisión y alcance

Un agente Python 3.10+ del **host Linux Docker Engine**, sin paquetes Python externos,
y Uptime Kuma 2.5.5 fijado por digest, preferentemente en **otro servidor**.
El agente no modifica cargas, permisos SQL, migraciones ni datos; no hace limpieza automática.
Solo añade señales atómicas a los scripts existentes después de verificar sus resultados.

| Alternativa | Ventaja | Limitación / decisión |
|---|---|---|
| Solo Kuma | HTTP/TLS, histórico y notificaciones fáciles | No resuelve capacidad SQL, backups verificados, recursos ni OOM persistente |
| Kuma + agente del host | Dos piezas pequeñas; sin socket Docker en el dashboard; histórico local y externo | Elegida; requiere mantener el agente y configurar el receptor |
| Prometheus + exporters + Grafana + Alertmanager (+ Loki) | Métricas centralizadas y consultas avanzadas | Excesivo ahora; considerar al operar varios hosts o necesitar series más ricas |

Kuma en el mismo servidor **no puede avisar que murió todo el servidor**. El observador externo
y un canal independiente son parte de la puesta en producción, no una mejora opcional.
Hasta configurarlos, la cobertura de pérdida del host/energía/red queda pendiente.

## Auditoría anterior a los cambios

Camino canónico: `docker-compose.yml`, `Dockerfile`, `Caddyfile`, `Program.cs`,
`scripts/backup/` y `docs/backup-restore.md`. Estaban modificados o sin seguimiento
antes de este trabajo; se preservan. La versión efectiva es .NET 10, pese al texto
histórico .NET 8 de AGENTS.md. No se tocó código ERP.

Señales existentes: live independiente de SQL; ready abre conexión SQL y devuelve 503
ante fallo; healthcheck SQL cada 10 s; readiness Docker cada 30 s (3 fallos, inicio 90 s);
restart `unless-stopped` para permanentes; `no` para migraciones/inicialización;
salida de migraciones bloquea el arranque si no es cero; límites CPU/RAM;
logs Docker 10 MB × 3; named volumes para SQL, keys, uploads, App_Data y Caddy.
`unhealthy` informa un estado: la restart policy no lo reinicia por sí mismo.

Backups: full diario, log cada 15 min, archivos diarios, retención local y offsite;
`CHECKSUM`, `VERIFYONLY`, SHA256, comprobación de tar y rclone check (download cuando
no hay hash común). Restore de prueba semanal ya implementado. Los logs estaban
rotados, pero no había estado estructurado persistente para alertas.

Faltaban: notificación, deduplicación/resolved, histórico de recursos, observador externo,
captura persistente de OOM, vigilancia de filesystem/volúmenes/SQL/TLS y caducidad de backups.
La rotación Docker no cubre errorlog/dumps SQL ni `/var/log/bury-backup-errors.log`.
Su crecimiento queda incluido en filesystem/volumen; revisar su retención operativa.

## Señales, frecuencia y severidad

`INFO`: normalidad, recuperación o reconocimiento. `WARNING`: margen reducido o reinicio
aislado. `CRITICAL`: indisponibilidad, integridad/cobertura desconocida, OOM o capacidad urgente.
Una muestra ausente nunca resuelve una alerta. Fallar un colector genera `collector.*` crítico.
Live/ready requieren HTTP 200, sin redirección y cuerpo `Healthy`; una página de login
con 200 no oculta una ruta de health mal configurada. Al cruzar 85% de disco se genera
una señal de acción independiente, sin esperar el cooldown de la advertencia del 70%.

| Componente | Señal | Warning | Critical |
|---|---|---|---|
| ERP / Caddy público | `/`, live, ready cada 60 s, validación TLS normal | — | HTTP distinto de 200/error durante 120 s |
| Dependencia SQL | live funciona, ready falla | — | Ready down 120 s; revisar SQL, no asumir causa única |
| app/db/caddy | missing/stopped/paused/dead/restarting/unhealthy/starting | — | Estado anormal durante 120 s |
| Reinicios | aumento de RestartCount por ID | Un incremento; recuerda 10 min | ≥3 incrementos en 10 min |
| OOM | eventos Docker continuos + OOMKilled de inspect | — | Inmediato, persistente hasta reconocimiento |
| migrate/db-init | one-shot termina | — | Exit distinto de cero; exit 0 no alerta |
| Filesystem | porcentaje y GiB libres cada 60 s | ≥70%, acción ≥85%, o <20 GiB | ≥90% o <10 GiB |
| Volúmenes / backups | `du -sx`, cada 15 min | ≥20 GiB por defecto | ≥40 GiB por defecto |
| Crecimiento | comparación con base de hasta 24 h; mínimo 1 h | ≥2 GiB/día | ≥4 GiB/día |
| SQL Express 2022 | suma archivos ROWS asignados / 10 GiB | ≥70% (7 GiB) | ≥85% (8,5 GiB) |
| SQL log | archivos LOG asignados, separado de datos | ≥2 GiB | ≥5 GiB |
| Certificado | cadena/hostname válidos y días restantes, 15 min | ≤21 días | ≤7 días; inválido/error de TLS también crítico |
| Backups | resultado verificado, no mera presencia | Ver ventanas siguientes | Falta, inválido, fallo o supera máximo |
| Canal / agente | webhook, errores de recolección y heartbeat | Canal no configurado | Push externo vencido o colector fallando |

Medición de disco: `/`, DockerRootDir obtenido de `docker info`, BACKUP_DIR y todos los
mountpoints de volúmenes del proyecto (incluye SQL, keys, uploads, App_Data y certificados).
Los porcentajes describen el filesystem que alberga cada volumen, no una cuota inexistente.
Configurar `disk_paths` para cualquier montaje adicional. Ausencia o permisos insuficientes
producen alerta; nunca se presentan como 0% usado. En Docker Desktop los mountpoints del
daemon están en otra VM: ejecutar el agente en el host Linux definitivo, no Windows/WSL cliente.

20/10 GiB son pisos iniciales, **ajustarlos** para reservar además espacio para dos fulls,
el mayor restore temporal, imágenes y crecimiento diario. 20/40 GiB por volumen son presupuestos
iniciales configurables globalmente: revisarlos según tamaños reales. No hay borrado automático.
El crecimiento es estimación desde la base diaria; saltos de backups/preasignación SQL requieren interpretación.

| Servicio | Límite de referencia | CPU (del límite) | RAM (del límite) |
|---|---|---|---|
| app | 2 CPU / 2 GiB | ≥80% warning, ≥95% critical, 5 min | ≥80% / ≥95%, 3 min |
| db | 4 CPU / 3 GiB | ≥80% / ≥95%, 5 min | ≥85% / ≥95%, 3 min |
| migrate mientras corre | 4 CPU / 3 GiB | ≥80% / ≥95%, 5 min | ≥80% / ≥95%, 3 min |
| caddy | 1 CPU / 256 MiB | ≥80% / ≥95%, 5 min | ≥80% / ≥95%, 3 min |
| host | CPU total / MemAvailable | ≥80% / ≥95%, 5 min | ≥80% / ≥95%, 3 min |

Se leen los límites efectivos de Docker, no se asumen los defaults. 180% de `docker stats`
en app limitada a 2 CPU es 90% de su presupuesto. Docker stats descuenta caché inactiva en
Linux: no es garantía contra OOM por picos entre muestras. Los umbrales requieren muestras
continuas; un hueco >180 s reinicia la ventana. Revaluar con carga representativa.

SQL: consulta pequeña de `sys.database_files` cada 15 min, timeouts 5/10 s; conexión de
disponibilidad cada minuto indirectamente por readiness y healthcheck SQL. Datos son tamaño
**asignado**, conservador para capacidad; el log no cuenta en los 10 GiB de Express 2022.
No se usan scans de tablas, DBCC periódicos ni nuevos grants. La consulta ejecuta sqlcmd dentro
del contenedor db con su identidad administrativa existente; la password no sale del contenedor
ni aparece en argumentos del host. Esto requiere confiar en el agente como administrador Docker.

## Backups y restore

| Evidencia | Warning | Critical / máximo |
|---|---|---|
| Full verificado | 26 h | 30 h |
| Log verificado | 30 min | 45 min |
| Archivos verificados | 26 h | 30 h |
| Offsite SQL verificado | 30 min | 45 min |
| Offsite archivos verificado | 26 h | 30 h |

`BACKUP_DIR/monitor-status/*.json`: escritura atómica, modo restringido; se marca éxito
solo después de los checks existentes (VERIFYONLY+SHA256 para SQL; validación tar+SHA256 para
archivos; rclone copy+check para offsite). `run-<modo>` registra el exit final, incluidos errores
de prerrequisitos. Exit 4 ocupado no reemplaza la evidencia anterior; la edad sigue avanzando.
Un backup log omitido por recovery SIMPLE **no** renueva la fecha de éxito del log.
Un full bueno no silencia un fallo offsite. Un éxito `all` posterior tampoco borra un fallo de
una ejecución manual `full`: repetir ese modo y comprobarlo. Un archivo existente sin evidencia
verificada no se considera válido. Los backups anteriores al cambio requieren una ejecución nueva.
No se rehashean bases grandes cada minuto; una modificación posterior del archivo se detecta
durante las verificaciones de restauración existentes, no por esta lectura de estado.

Conservar VERIFYONLY en **cada backup**, restore real **semanal** con `restore-test.sh`
(base temporal y DBCC CHECKDB, ya programado), disaster drill completo **trimestral**
en servidor aislado desde offsite incluyendo claves y archivos. Una base pequeña justifica
el restore semanal; VERIFYONLY no demuestra recuperación funcional. Registrar RPO/RTO y
resultado del drill. Este bloque no ejecuta DR ni cambia el scheduler/retención de backups.

## Instalación en el host definitivo

Requisitos: Linux Docker Engine, Docker CLI, Python ≥3.10, bash, GNU du, systemd,
acceso del servicio al daemon y lectura de los discos. Sin librerías pip.
Las rutas de ejemplo asumen checkout en `/opt/theburyproject`.

```bash
sudo install -d -m 0700 /etc/bury-monitor /var/lib/bury-monitor
sudo install -m 0600 scripts/monitoring/config.example.json /etc/bury-monitor/config.json
sudo install -m 0600 scripts/monitoring/monitor.env.example /etc/bury-monitor/monitor.env
# Editar config.json: project EXACTO de Compose, database, base_url real, backup_dir y umbrales.
# Editar monitor.env: URL webhook, bearer opcional y URL Push externa, sin llevarlos a Git.
sudo install -m 0644 scripts/monitoring/bury-monitor.service /etc/systemd/system/
sudo systemd-analyze verify /etc/systemd/system/bury-monitor.service
sudo systemctl daemon-reload
sudo systemctl enable --now bury-monitor
sudo journalctl -u bury-monitor -n 30 --no-pager
```

Servicio root por lectura de volúmenes y Docker (equivalente a administrador); no agrega puertos.
Filesystem del servicio protegido, solo escribe `/var/lib/bury-monitor`, memoria 192 MiB,
CPU 25% de un core, I/O idle. No se instala automáticamente en esta estación Windows.
`ProtectHome=true` implica que el checkout/backups no deben estar bajo `/home`.
El lock de proceso evita dos agentes simultáneos. Excepciones de collectors se aíslan;
una falla fatal del proceso la reinicia systemd y debe detectarla el heartbeat externo.

## Uptime Kuma: observador externo

```bash
docker compose -f docker-compose.monitoring.yml up -d
# Desde el equipo del operador: túnel al observador, no al ERP si son distintos hosts.
ssh -L 3001:127.0.0.1:3001 operador@observador
```

Abrir `http://127.0.0.1:3001`, crear administrador y mantener autenticación habilitada.
No habilitar página de estado pública. No montar docker.sock. El único puerto agregado
es **127.0.0.1:3001**, TCP. No es alcanzable públicamente.

Crear estos monitores, usando el dominio real cuando exista:

| Nombre | Tipo / destino | Ajustes |
|---|---|---|
| ERP público | HTTPS `https://dominio/` | 60 s, timeout 10 s, 2 retries, HTTP 200 |
| LIVE público | HTTPS `https://dominio/health/live` | Igual, no ignorar errores TLS |
| READY público | HTTPS `https://dominio/health/ready` | Igual; comparar con LIVE |
| TLS | Reutilizar ERP público | Activar notificación de vencimiento; 21, 14 y 7 días |
| Agente del host | Push | 180 s, 1 retry; URL en BURY_MONITOR_HEARTBEAT |

El agente debe poder alcanzar el endpoint Push del observador por **red privada existente**
o endpoint HTTPS autenticado del observador. El compose loopback por sí solo no proporciona
esa ruta: prepararla en la infraestructura del observador sin abrir un puerto administrativo
del ERP. No configurar firewall/SSH ni dominio real como parte de este bloque.
Si no hay conectividad privada preparada, mantener Push pendiente, no fingir cobertura externa.

Configurar un canal soportado por Kuma (email/Telegram/webhook), probar desde su UI, activar
recuperación y recordatorio cada 60 intervalos (~1 h); asociarlo a todos los monitores.
No pegar directamente una URL Slack/Discord al agente: el agente envía JSON genérico, requiere
un receptor que acepte ese contrato. Kuma sí adapta sus proveedores nativos.

## Alertas, persistencia y límites

Agente: POST JSON `{event_id,key,severity,status,time,detail}`, Bearer opcional, timeout 8 s;
HTTP 2xx confirma. El receptor debe deduplicar `event_id` (entrega al menos una vez).
Cambio de estado y resolved producen eventos; un incidente igual se recuerda cada **3600 s**.
Cola persistida antes del envío; reintento ≥60 s, máximo 10 envíos por ciclo; también reintenta
señales lentas aunque ese ciclo no vuelva a medirlas. Conserva firing y resolved si se corta el canal.
Cola acotada a 100 transiciones por señal: bajo una interrupción muy prolongada se conservan
primera y últimas; el histórico local retiene todas las transiciones dentro de su ventana.
La ausencia de canal no bloquea la recolección; figura como warning. Fallos de entrega/recolección
marcan Push down; URL/token no se imprimen. Si Push tampoco llega, su vencimiento avisa desde fuera.
Cada regla tiene su clave: pueden llegar alertas correlacionadas HTTP/db/app; no hay agrupación causal.

OOM: streaming continuo de eventos y registro SQLite antes de la siguiente muestra. Sobrevive a
restart/recreate del contenedor y reinicio del agente. Reconocimiento manual tras investigación:

```bash
sudo python3 /opt/theburyproject/scripts/monitoring/monitor.py \
  --config /etc/bury-monitor/config.json --ack-oom app
```

El siguiente ciclo emite resolved. Docker solo guarda **256 eventos recientes** para replay;
una caída prolongada del daemon/agente puede perder eventos anteriores, no reconstruibles desde
`OOMKilled=false`. La captura continua reduce esta brecha, pero no la elimina durante un apagón.
El observador externo debe alertar ese período sin cobertura. Exit 137 solo no prueba OOM.

SQLite en `/var/lib/bury-monitor`: estado, incidentes, outbox, OOM y muestras, 30 días.
La purga reutiliza páginas SQLite; no reduce automáticamente el máximo físico ya alcanzado.
Volumen Kuma `monitor-data`: configuración, cuentas, canales e histórico. **Útil**, no dato
crítico del ERP; el histórico es regenerable, la configuración evita rehacer alertas.
Respaldarlo semanalmente y tras cambios: detener SOLO Kuma y copiar su volumen con tar,
o usar snapshot consistente; cifrar porque contiene canales/tokens. Respaldar configuración
del agente en gestor seguro; para SQLite usar su API backup, no copiar solo el .sqlite vivo
ignorando WAL. No se modifica el backup ERP para incluirlos en este bloque.

## Runbook

Ejecutar Compose desde el checkout con el proyecto/entorno correctos. No publicar `compose config`
ni inspect completo: pueden mostrar secretos. Escalar inmediatamente cualquier pérdida de datos,
OOM repetido, disco crítico o ausencia de backups válidos.

| Alerta / significado | Verificar y comando | Cuándo escalar |
|---|---|---|
| READY DOWN + LIVE UP: proceso vivo, dependencia no disponible | `docker compose ps -a db app`; `docker compose logs --tail 100 db app` (revisar localmente) | >5 min o errores SQL de I/O/integridad |
| LIVE/ERP DOWN: ruta/proceso/TLS | `curl --fail --max-time 10 https://DOMINIO/health/live`; `docker compose ps -a app caddy` | >2 min después de descartar mantenimiento |
| Caddy/HTTPS/TLS | `docker compose logs --tail 100 caddy`; `openssl s_client -connect DOMINIO:443 -servername DOMINIO </dev/null 2>/dev/null \| openssl x509 -noout -dates` | Cert inválido ahora o <7 días; revisar DNS/ACME y disco |
| Restart loop/unhealthy | `docker compose ps -a`; `docker compose logs --tail 100 app` | ≥3 reinicios/10 min; no reiniciar indefinidamente |
| OOM | `docker stats --no-stream`; `journalctl -k --since '-1 hour'` | Antes de reconocer si causa desconocida/repetida; revisar cuota y concurrencia |
| CPU/RAM sostenida | `docker stats --no-stream`; `free -h`; `uptime` | Critical sostenido; capturar carga antes de ajustar límites |
| Disco / volumen / crecimiento | `df -h`; `df -i`; `docker system df`; `sudo du -xsh /srv/bury-backups /var/lib/docker/volumes` | 85% plan inmediato, 90% o <10 GiB urgente; nunca prune/borrar uploads automáticamente |
| SQL datos Express | Revisar `sql.data` en histórico; `docker compose exec db df -h /var/opt/mssql` | 7 GiB plan capacidad/licencia; 8,5 GiB urgente, no esperar 10 |
| SQL log / crecimiento | `docker compose ps db`; `tail -n 100 /srv/bury-backups/logs/backup.log` | Revisar log backups/transacciones; no shrink rutinario ni recovery SIMPLE improvisado |
| Full/log/archivos vencido/fallido | `cat /srv/bury-backups/monitor-status/*.json`; `systemctl status cron`; `sudo cat /etc/cron.d/bury-backup` | Log >45 min/full >30 h; reparar causa y ejecutar `bash scripts/backup/backup.sh log` o `full` |
| Offsite | Revisar backup.log localmente, conectividad/cuota y canal secreto del proveedor | No hay copia verificada dentro de ventana; repetir modo correspondiente después de reparar |
| migrate fallido | `docker compose ps -a migrate`; `docker compose logs --tail 100 migrate` | Siempre antes de servir tráfico; no marcar exit 0 ni saltar dependencia |
| collector/canal/Push ausente | `systemctl status bury-monitor`; `journalctl -u bury-monitor -n 50`; revisar `last_sample`/outbox | >3–6 min sin cobertura; comprobar observador y entrega real |

Para inspeccionar histórico sin dependencias adicionales:

```bash
sudo python3 - <<'PY'
import sqlite3
with sqlite3.connect('/var/lib/bury-monitor/monitor.sqlite') as db:
    for row in db.execute("SELECT datetime(ts,'unixepoch'),data FROM history WHERE kind='alert' ORDER BY ts DESC LIMIT 20"):
        print(*row)
PY
```

Los detalles no contienen passwords, pero sí nombres/rutas operativas; mantenerlos privados.
No hay silenciamiento global automático durante backup: la gracia de 120 s absorbe la parada
habitual de 10–15 s y una parada más larga sigue siendo indisponibilidad real.

## Validación reproducible y evidencia

```powershell
python -m unittest discover -s scripts/monitoring -p test_monitor.py -v
python scripts/monitoring/isolated_smoke.py
docker compose -f docker-compose.monitoring.yml config --quiet
```

El smoke requiere imagen local `theburyproject/erp:production-qa`, puertos QA 18891 libre y
subred 172.29.94.0/24 libre. Usa secretos aleatorios propios en `artifacts/monitoring/` ignorado,
un proyecto único y seis volúmenes propios; elimina solo sus contenedores/red, conserva los
volúmenes. Nunca usa volúmenes reales. Acorta el reloj de evaluación de alertas para las pruebas;
los holds y huecos se prueban aparte. Evidencia por ejecución: `report.json` privado en artifacts.

Ver `docs/monitoreo-validacion.md` para resultados, consumo, límites y archivos de este bloque.

## Fuentes verificadas

- [Uptime Kuma: características](https://github.com/louislam/uptime-kuma) y
  [riesgo de acceso Docker desde Kuma](https://github.com/louislam/uptime-kuma/wiki/How-to-Monitor-Docker-Containers).
- [Docker stats y caché](https://docs.docker.com/reference/cli/docker/container/stats/),
  [eventos y replay de 256 entradas](https://docs.docker.com/reference/cli/docker/system/events/).
- [Límites Express 2022](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022?view=sql-server-ver17).

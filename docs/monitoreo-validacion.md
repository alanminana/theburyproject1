# Evidencia del bloque de monitoreo — 2026-09-22

Implementación revisable; activación en servidor definitivo y entrega externa pendientes.
Runbook, configuración, tablas y límites: [monitoreo-alertas.md](monitoreo-alertas.md).

## Pruebas realizadas

| Prueba | Evidencia / resultado |
|---|---|
| SQL detenido realmente | `live` 200, `ready` falla; alertas `docker.db` y `http.ready` |
| SQL recuperado | Readiness vuelve; receptor recibe resolved |
| app detenida realmente | `docker.app` + live/ready down; recuperación posterior |
| Caddy detenido realmente | `docker.caddy` + live/ready down; recuperación posterior |
| Repetición estable | 16 notificaciones totales del escenario; repetición no agrega ninguna |
| Backups vencidos/fallidos/futuros | Inyección segura: CRITICAL; no se llena ni modifica un disco real |
| Poco espacio | Mock con porcentaje bajo pero 5 GiB libres: CRITICAL |
| OOM/restart loop | Inspect simulado y evento OOM simulado: latch persiste; replay no deshace reconocimiento; 3 reinicios críticos |
| app unhealthy | Estado running + unhealthy produce CRITICAL |
| migrate !=0 | Estado simulado produce deploy.migrate CRITICAL; exit 0 no alerta |
| CPU/RAM | Normalización a cuota efectiva, pico descartado, huecos reinician hold, uso sostenido alerta |
| SQL Express | Consulta real: 0,070 GiB datos / 0,008 GiB log; inyección 7,5/8,6 GiB warning/critical |
| TLS | Cadena local de Caddy confiada explícitamente; HTTPS real correcto; certificado local de ~12 h provoca critical por <7 días |
| Canal | POST a receptor HTTP local, firing + resolved; outage y reintento conservan eventos |
| Full/log reales | Exit 0, VERIFYONLY+SHA256 OK; registros full/log verified=true y run exit=0 |
| Archivos reales | Exit 0, tar verificado, files verified=true; app detenida por el script volvió a healthy |
| Fallo de prerrequisito backup | Intento WSL sin integración Docker: run-full exit=8 persistido; éxito posterior reemplaza el fallo |
| Agente Linux completo | 83 señales; Docker, stats, filesystem, du, SQL, HTTP, TLS, eventos y lectura de estados sin error de colector |
| Kuma | Imagen fijada; HTTP 200 por loopback; volumen propio, sin docker.sock |

13 tests focalizados pasan en Linux Python 3.12 y Windows Python 3.14.
`bash -n` pasa para los dos scripts de backup tocados. Compose de monitoreo valida.
`systemd-analyze verify` en WSL no reportó errores del unit; avisó permisos del montaje
Windows y un unit ajeno de netplan. El servicio **no fue instalado ni arrancado con systemd**.
No se ejecutó suite ERP completa ni DR. No se cambió el esquema SQL.
`git diff --check` pasó (avisos LF/CRLF del trabajo previo); revisión de whitespace de
los 11 archivos de alcance, incluidos los nuevos sin seguimiento, también pasó.

El smoke integrado `bury-monitor-qa-58e3a7` pasó y generó
`artifacts/monitoring/bury-monitor-qa-58e3a7/report.json` (ignorado). Hubo un primer fallo
por acceso al campo Health ausente en contenedores sin healthcheck; se corrigió con lectura
opcional. También se corrigió el cierre explícito de conexiones SQLite detectado en Windows.
Un intento posterior encontró subred de QA ocupada por los contenedores detenidos de la
primera ejecución; el harness ahora elimina **sus** contenedores/red y conserva volúmenes.

El ensayo nativo Linux se ejecutó en un contenedor temporal con Python estándar, CLI Docker,
socket administrativo, mountpoints del daemon en solo lectura y red del Caddy **de QA**.
No constituye validación de capacidad ni de permisos del futuro servidor Linux. Un intento
previo desde WSL sin integración Docker emitió correctamente collector.* críticos y no se
usó como prueba exitosa de Docker/SQL. Toda mutación de carga ocurrió en proyectos propios.

## Consumo medido

| Componente | CPU | RAM | Disco |
|---|---|---|---|
| Agente, ciclo completo Linux | 1,05 s CPU del intérprete; 12,02 s de pared incluyendo muestreo/CLI/du | pico RSS 33.740 KiB (~33 MiB) | SQLite inicial 57.344 bytes |
| Kuma sin monitores configurados | 0,00% en dos muestras puntuales | 103–108 MiB | /app/data 16 KiB iniciales |

Estas son medidas iniciales, no promedio bajo carga ni histórico de semanas. El CPU del
intérprete no incluye el consumido por procesos Docker CLI/du; la cuota systemd sí limita
todo su cgroup a 25% de un core/192 MiB. Kuma tiene 0,5 CPU/256 MiB. Presupuestar inicialmente
1 GiB para 30 días del agente y revisar crecimiento real; Kuma debe medirse otra vez con
los cinco monitores y retención definitiva. El histórico del agente purga filas pero SQLite
reutiliza páginas (el archivo no encoge solo). No se infiere impacto cero de dos muestras.

## Archivos de este bloque

| Archivo | Propósito |
|---|---|
| `docker-compose.monitoring.yml` | Observador independiente, pin, límites, loopback y volumen |
| `scripts/monitoring/monitor.py` | Recolección, estado, histórico, eventos OOM y envío |
| `scripts/monitoring/config.example.json` | Umbrales y rutas sin secretos |
| `scripts/monitoring/monitor.env.example` | Contrato de webhook/heartbeat privado |
| `scripts/monitoring/bury-monitor.service` | Arranque persistente y límites del agente Linux |
| `scripts/monitoring/test_monitor.py` | 13 regresiones focalizadas y receptor local |
| `scripts/monitoring/isolated_smoke.py` | Fallos reales reproducibles en Compose aislado |
| `scripts/backup/lib.sh` | Escritura atómica de evidencia sin alterar exit codes |
| `scripts/backup/backup.sh` | Hooks después de verificaciones y al finalizar |
| `docs/monitoreo-alertas.md` | Diseño, tablas, instalación y runbook |
| `docs/monitoreo-validacion.md` | Evidencia, alcance y pendientes |

Los dos scripts de backup ya estaban sin seguimiento antes del bloque. Su staging incluye
ese trabajo previo; revisar los archivos completos. Se conservaron los cambios previos y
los cambios concurrentes de otros módulos. No hubo commit, push, staging ni prune.
Al cerrar se retiraron los contenedores/redes de los proyectos propios de QA y se conservaron
sus volúmenes/evidencia. No se detuvieron contenedores ajenos.

```powershell
git add -- docker-compose.monitoring.yml scripts/monitoring/monitor.py scripts/monitoring/config.example.json scripts/monitoring/monitor.env.example scripts/monitoring/bury-monitor.service scripts/monitoring/test_monitor.py scripts/monitoring/isolated_smoke.py scripts/backup/lib.sh scripts/backup/backup.sh docs/monitoreo-alertas.md docs/monitoreo-validacion.md
```

## Pendientes exclusivos de monitoreo

- Instalar el agente en el Linux definitivo; comprobar paths, permisos y capacidad/umbrales con datos reales.
- Configurar Kuma en un observador externo, sus cinco monitores y una ruta privada/HTTPS para Push.
- Proveer webhook/canal real y comprobar recepción y resolved: **credencial real probada: no**.
- Verificar HTTPS/TLS con dominio y ACME reales cuando existan; solo TLS local verificado aquí.
- Probar la evidencia offsite con el proveedor real; no hubo upload externo en esta ejecución.
- Medir consumo con monitores configurados/histórico y respaldar configuración/estado del monitoreo.

No se avanzó a firewall, hardening SSH/RDP, CI/CD, dominio real, auditoría web ni Mercado Libre.

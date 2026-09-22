# Docker productivo: imágenes, recursos y mantenimiento

Ensayo del 21/22 de septiembre de 2026. **Listo para commit del bloque**, con límites iniciales
validados sobre datos sintéticos pequeños; no constituye dimensionamiento de carga real.
No se modificaron reglas de negocio, usuarios SQL, migraciones, secretos, Caddyfile ni scripts de backup.
El árbol ya tenía cambios ajenos y archivos Docker sin seguimiento antes de esta intervención.

## Imágenes

Los digests completos están en Dockerfile y Compose. Se fijó el contenido local probado, sin actualizar
silenciosamente a otra versión. Los prefijos siguientes identifican esos digests, no son referencias ejecutables.

| Servicio | Imagen anterior | Imagen final | Estrategia |
|---|---|---|---|
| app / migrate | `theburyproject/erp:latest` | `${ERP_IMAGE:-theburyproject/erp:local}`; ensayo `:production-qa` | Tag único por release/commit en producción; conservar anterior |
| Runtime base | `dotnet/aspnet:10.0` | mismo tag + digest `2d584d8147fa…` (10.0.12) | Digest |
| SDK build | `dotnet/sdk:10.0` | mismo tag + digest `2fa828c68761…` (10.0.401) | Digest, compatible con global.json |
| db / db-init | `mssql/server:2022-latest` | `mssql/server@sha256:4402d880dd4c…` (16.0.4295.3) | Digest |
| caddy | `caddy:2-alpine` | mismo tag + digest `de23def33b17…` (2.11.4) | Digest |
| backup-files / restore-files | `alpine:3` | mismo tag + digest `294b683cb724…` | Digest; sin cambiar procedimientos |
| backup-offsite / restore-offsite | `rclone/rclone:1` | mismo tag + digest `45401ad7410d…` (1.75.1) | Digest; sin cambiar procedimientos |

`:local` es comodidad de desarrollo, **no una etiqueta de release**. En producción definir `ERP_IMAGE`
con una etiqueta nueva antes del build y conservar su ID/digest. Un reinicio no descarga ni reconstruye.
Los digests fijan las bases, pero APT y restore siguen consultando repositorios: reconstruir no garantiza
igualdad bit a bit. La unidad de despliegue/rollback es la imagen final ya construida y conservada.

## Tamaños

Valores de `docker image ls` (almacenamiento mostrado por Docker Desktop/containerd; no tamaño comprimido
de descarga ni suma de bytes exclusivos). En este entorno `image inspect .Size` devuelve valores distintos.

| Imagen | Tamaño |
|---|---:|
| ERP anterior / final | 631 MB / 631 MB |
| SDK | 1,3 GB |
| SQL Server | 2,34 GB |
| Caddy | 88,8 MB |
| Alpine | 13 MB |
| rclone | 130 MB |

Multi-stage conservado: restore después de copiar csproj/global.json, luego fuente y publish Release.
SDK ausente en la imagen final (`dotnet --list-sdks` vacío). QuestPDF conserva fontconfig y Liberation;
PDF y Excel generados realmente. Búsqueda en imagen limpia: sin `.git`, `.env`, `*.env`, backups,
`*.bak`, secrets.json ni appsettings.Development.json; keys/uploads/App_Data vacíos.
`.dockerignore` ya excluía secretos, estado real, tests y artefactos; no fue necesario modificarlo.
La exclusión no puede detectar una credencial incrustada arbitrariamente en código fuente.

## Usuarios / hardening

| Servicio | UID/User | root | no-new-privileges | read-only | Motivo |
|---|---|---|---|---|---|
| app | 1654 | No | Sí | Sí | Solo volúmenes de estado y /tmp escribibles |
| migrate | 1654 | No | Sí | Sí | Migraciones SQL y temporales; no inicia servidor HTTP |
| db | 10001/mssql | No | Sí | No | Usuario oficial; SQL necesita escribir datos y estado interno |
| db-init | 10001/mssql | No | Sí | Sí | Scripts /init montados ro; solo cliente SQL |
| caddy | 0/root | Sí | Sí | No | Usuario oficial; datos/config y trust store local en ensayo TLS |
| backup-files / restore-files | 0/root | Sí | No | No | Leer claves y preservar propietarios al restaurar |
| backup-offsite / restore-offsite | 0/root | Sí | No | No | Acceso a archivos de backup restringidos; comportamiento oficial conservado |

No hay privileged, cap_add, network/pid host ni docker.sock en el stack. Los helpers de archivos no tienen red.
No se forzó cambio de usuario/capabilities de imágenes oficiales. `tmpfs /tmp` en app/migrate/db-init:
rw, nosuid, nodev, modo 1777. Consume memoria dentro del límite del contenedor; no es almacenamiento persistente.
No se usó noexec, que puede interferir con bibliotecas nativas. Las herramientas de backup solo cambiaron
referencias de imagen y rotación de stdout/stderr; no se endurecieron sus operaciones de restore sin probarlas.

## Recursos medidos

Docker Engine 29.6.2, Compose 5.3.1, Docker Desktop Linux/amd64: 18 CPU y 15,36 GiB disponibles.
`docker stats --no-stream` repetido aproximadamente cada 2–3 segundos, incluyendo arranque, login,
navegación, escritura/lectura, 20 PDF + 20 Excel por pasada, ocho exportaciones simultáneas,
migraciones completas y reinicios. CPU 100% equivale aproximadamente a un núcleo.
Picos muestreados, no máximos instantáneos. Idle es mediana de las muestras etiquetadas idle.

| Servicio | CPU idle | CPU pico | RAM idle | RAM pico |
|---|---:|---:|---:|---:|
| app | ~0,52% | 104,16% | ~252 MiB | 293,6 MiB |
| db | ~1,26% | 115,91% | ~776 MiB | 865 MiB |
| caddy | ~0% | 1,65% | ~13 MiB | 19,8 MiB |
| migrate | No aplica | 143,47% | No aplica | 875,7 MiB |
| db-init | No aplica | No capturado | No aplica | No capturado |

db-init terminó demasiado rápido para obtener una muestra válida; los ceros de stats después de salir
no se interpretaron como consumo cero. No se inventó un límite para ese servicio.
Los reportes tienen pocos datos: no prueban un PDF grande, un Excel masivo ni muchas sesiones SignalR.
Los servicios en segundo plano estuvieron activos. SQL reportó cuatro schedulers; su DMV de memoria
reportó 4096 MiB mientras Docker medía ~865 MiB: se conserva esa discrepancia y se dimensiona el
contenedor con stats/cgroup y la prueba real, no tratando el buffer pool como toda la memoria de SQL.

## Límites finales

| Servicio | CPU limit | RAM limit | Variable | Motivo |
|---|---:|---:|---|---|
| app | 2 | 2 GiB | APP_CPUS / APP_MEMORY | Margen sobre ~294 MiB; exportaciones nativas/concurrentes |
| db | 4 | 3 GiB | DB_CPUS / DB_MEMORY | Express usa hasta 4 cores; margen para proceso y cachés |
| migrate | 4 | 3 GiB | MIGRATE_CPUS / MIGRATE_MEMORY | Mayor presupuesto que app; pico ~876 MiB |
| caddy | 1 | 256 MiB | CADDY_CPUS / CADDY_MEMORY | Amplio margen sobre ~20 MiB; TLS/h2 probado |
| db-init | Sin límite específico | Sin límite específico | — | One-shot breve, sin muestra fiable |
| herramientas | Sin límite específico | Sin límite específico | — | Solo profile tools; no estrangular compresión/copias |

Se utilizan `cpus` y `mem_limit`, no se asume comportamiento Swarm. `inspect` confirmó NanoCpus
2000000000/4000000000/1000000000 y Memory 2147483648/3221225472/268435456 según servicio.
Estos presupuestos máximos no reservan RAM; considerar host, backups, cache y solapamiento con migrate.
No son apropiados automáticamente para un host de 4 GiB. No se cambió la configuración de swap del daemon.
Express 2022 limita buffer pool a 1410 MB y datos por base a 10 GB; **no limita todo el proceso a 1410 MB**.
Compose ahora usa Express por defecto; una variable MSSQL_PID explícita conserva prioridad.

## OOM y recuperación

En inspección final previa a detener el ensayo: OOMKilled=false, ExitCode=0 y RestartCount=0 en los cinco
servicios. db-init/migrate terminaron 0; app/db healthy. Caddy no tiene healthcheck propio: se verificó por HTTP/TLS.
Reinicios manuales no incrementan necesariamente RestartCount. No se provocó OOM.
SQL detenido: live=200, ready=503; recuperado: ambos 200. La respuesta ready durante caída puede tardar
más que los cinco segundos del timeout Docker; no se afirma que el timeout interno de cuatro segundos sea
un límite estricto observado. Docker corta su sondeo a cinco segundos.

## Logging

| Servicio | Driver | max-size | max-file |
|---|---|---|---|
| db, app, caddy | json-file | 10m | 3 |
| db-init, migrate | json-file | 10m | 3 |
| cuatro herramientas de backup/restore | json-file | 10m | 3 |

Presupuesto aproximado de 30 MB por contenedor; cada ejecución one-shot retenida tiene su propio presupuesto.
Validado en HostConfig.LogConfig; no se inundó deliberadamente la app para forzar una rotación.
Recrear contenedores aplica cambios de logging; `restart` solo no actualiza su configuración.
Production conserva Default=Warning, ASP.NET=Warning y EF=Error. Se habilitó únicamente
Microsoft.Hosting.Lifetime=Information para startup/shutdown; sin Debug ni SQL informativo continuo.
No se agregó filtro que oculte errores. Readiness pasó de 15 a 30 segundos (tres fallos, ~90 segundos
más tiempos de ejecución); liveness/proceso y readiness/SQL conservan su significado.
La caída SQL produce stacks de HealthChecks y también de EF/background workers: el intervalo reduce
sondeos, pero no elimina los errores reales de workers. El presupuesto Docker limita su almacenamiento.
Revisión focalizada de configuración y sitios de logging: no habilita sensitive-data logging; no sustituye
una auditoría completa de todos los mensajes del ERP.

Caddyfile no habilita access logs; no se habilitaron. Logs operativos/certificados siguen en stdout/stderr.
No se agregaron cabeceras Authorization, cookies ni cuerpos a los logs.

**Tres logs distintos en SQL:** stdout/stderr rota por Docker; `/var/opt/mssql/log/errorlog*` pertenece
a SQL (ciclado por arranque o `EXEC sys.sp_cycle_errorlog`, retención propia de SQL que debe revisarse);
`.ldf` es parte indispensable de la base. No borrar/truncar estos archivos con limpieza Docker.
FULL requiere mantener los backups de log ya existentes; un full no sustituye los backups de log.
No ejecutar shrink periódico ni cambiar recovery model como limpieza. Dumps/XEvents también pueden crecer
dentro del volumen SQL y requieren diagnóstico/retención propios.

## Disco

| Categoría | Antes | Después del ensayo, aproximado |
|---|---:|---:|
| Imágenes Docker | 9,763 GB | 10,39 GB |
| Contenedores, capa escribible | 1,761 GB | 1,761 GB |
| Volúmenes | 0 | 331,3 MB |
| Build cache | 15,28 GB | 16,02 GB |
| Logs json-file | Sin medición inicial separada | ~66,4 MiB global; ~66,35 MiB son del contenedor Kasm preexistente |
| Backups del ensayo | 0 | 0: no se repitió disaster recovery ni se generaron backups |

El ensayo creó tres bases pequeñas para medir migraciones completas. No había volúmenes/ERP activo al inicio.
El filesystem Linux de Docker informó 27,3 GiB usados de ~1007 GiB: **no sumar las categorías anteriores**,
que comparten capas. Tampoco representa el espacio libre de la unidad Windows que aloja el VHDX.
Logs medidos por tamaños, sin editar archivos internos del daemon. SQL data ~235 MiB en medición intermedia;
SQL error logs ~808 KiB. No se inspeccionaron backups productivos ni datos reales.

## Volúmenes

Prefijo del ensayo `bury-production-qa_`; producción usa el nombre de proyecto configurado.

| Volumen / bind | Servicio | Contenido | Crítico | Tiene backup |
|---|---|---|---|---|
| bury-sqldata | db | MDF/LDF, system DB, logs SQL | Sí | Backup SQL full/log existente |
| bury-keys | app; helpers archivos | Data Protection | Sí | tar existente |
| bury-uploads | app; helpers archivos | Archivos subidos | Sí | tar existente |
| bury-appdata | app; helpers archivos | Contratos persistidos | Sí | tar existente |
| caddy-data | caddy; helpers archivos | Certificados/cuenta ACME | Operativo | tar según BACKUP_INCLUDE_CADDY |
| caddy-config | caddy | Config autosave | Regenerable | No |
| BACKUP_DIR/sql | db | Backups SQL | Sí | Copia externa existente, pendiente configuración real según runbook |
| BACKUP_DIR | helpers | Backups archivos/SQL y logs | Sí | Copia externa existente |
| docker/db-init | db-init, ro | Scripts versionados | Regenerable | Git |
| Caddyfile | caddy, ro | Config versionada | Regenerable | Git |
| scripts/backup/container | helpers archivos, ro | Scripts versionados | Regenerable | Git |

Exactamente seis volúmenes nombrados en el daemon tras el ensayo; ningún volumen anónimo.
La columna backup describe el mecanismo existente, no una nueva prueba de restauración ni confirmación del remoto.

## Permisos filesystem

| Ruta | Resultado efectivo |
|---|---|
| /app | root:root 755, montaje raíz ro; escritura rechazada |
| /keys, /app/App_Data, /app/wwwroot/uploads | 1654:1654 755; app escribe y conserva marcadores tras restart/recreate |
| /tmp | tmpfs 1777; temporal escribible |
| /var/opt/mssql | root:10001 770; mssql escribe y conserva datos |
| /backups y /backups/sql del ensayo | root:root 777 por bind Windows; **no valida permisos Linux productivos** |
| /data, /config Caddy | root:root 755; estado persistente, TLS local generado |
| Montajes /src de backup-files | ro, propietarios conservados; lectura del marcador probada |

No se introdujeron chmod recursivos sobre datos existentes. Para Linux productivo usar el procedimiento
de `backup-restore.md`: raíz backups 700, sql 10001/750 y archivos restringidos. Verificar con `stat`
como usuario efectivo antes del despliegue. El UID 1654 debe seguir siendo dueño de los volúmenes app;
no cambiarlo sin preparar su propiedad. App/Caddy no montan los backups; migrate/db-init no montan estado app.

## Limpieza rutinaria (manual y acotada)

Comandos para shell Linux en el host, revisar inventario antes de borrar:

```bash
docker system df -v
docker image ls --digests
docker ps -a --filter label=com.docker.compose.project=theburyproject
docker volume ls
docker builder prune --filter 'until=168h' --keep-storage 5GB
docker image prune --filter 'until=168h'
docker container prune --filter label=com.docker.compose.project=theburyproject --filter 'until=168h'
docker network prune --filter label=com.docker.compose.project=theburyproject --filter 'until=168h'
```

Prune de imágenes sin `-a` elimina dangling; conservar las dos releases **etiquetadas** primero.
Prune de contenedores elimina evidencia de one-shots: guardar lo necesario antes. El filtro de proyecto
evita otros stacks; cambiarlo si el proyecto tiene otro nombre. Limpiar redes solo con el stack activo y
tras revisar las candidatas: una red de un stack completamente detenido también puede estar sin uso.
Cache puede regenerarse, pero su limpieza encarece builds. No se ejecutó ningún prune en este trabajo.
Para una release antigua identificada y excluida del par actual/anterior: `docker image rm REPO:TAG_EXACTO`
después de comprobar que ningún contenedor la necesita. No borrar automáticamente por antigüedad las releases.

## Comandos peligrosos

No automatizar `docker system prune --volumes`, `docker volume prune`, `docker compose down -v`
ni `docker image prune -a`: pueden eliminar estado persistente o la imagen anterior sin contenedor asociado.
Un volumen no usado puede ser el único ejemplar de datos valiosos. Nunca borrar a mano MDF/LDF,
`/var/lib/docker`, logs del daemon ni contenido de volúmenes como forma de liberar espacio.

## Actualización deliberada y rollback

1. Revisar release notes y avisos de seguridad de .NET, SQL, Caddy y herramientas. Resolver el digest de
   la versión elegida con `docker buildx imagetools inspect IMAGEN:TAG`; revisar plataforma y compatibilidad.
2. Editar explícitamente los pins. SDK debe respetar global.json. Descargar referencias seleccionadas;
   `docker compose --profile tools pull db db-init caddy backup-files restore-files backup-offsite restore-offsite`.
3. Crear etiqueta ERP única, construir y registrar ID. No usar :local/:latest en producción ni reutilizar una release.
4. Probar en stack aislado con red, credenciales, puertos, BACKUP_DIR y volúmenes propios. El ensayo de este
   bloque no instaló staging ni CI/CD. Promover **la misma imagen probada**, no reconstruir al desplegar.
5. Desplegar durante ventana prevista y comprobar migraciones/health/operaciones. Conservar actual y anterior.

Ejemplo Linux (RELEASE y PREVIOUS deben ser valores reales elegidos; no cambiar credenciales):

```bash
export ERP_IMAGE="theburyproject/erp:$RELEASE"
docker compose build app
docker image inspect "$ERP_IMAGE" --format '{{.Id}}'
# Tras validar esa imagen en el ensayo aislado:
docker compose up -d --no-build --pull never --wait
```

Guardar también fuera del daemon si no hay registry: `docker image save -o /ruta/erp-anterior.tar
"theburyproject/erp:$PREVIOUS"`; inventariar su tamaño, no dejar tar ilimitados en el repo.
Rollback de **app**, solo si el esquema es compatible con la versión anterior:

```bash
export ERP_IMAGE="theburyproject/erp:$PREVIOUS"
docker image inspect "$ERP_IMAGE" --format '{{.Id}}'
docker compose up -d --no-deps --no-build --pull never app
docker compose ps
```

No ejecuta migrate ni revierte la base. Si el esquema no es compatible, detenerse y seguir el runbook
de recuperación aprobado; este bloque no cambia migraciones. Para traer imagen archivada usar `docker image load`.
Conservar también los pins/config anteriores. No degradar una imagen SQL sobre datos actualizados sin verificar
compatibilidad: rollback de app no implica downgrade de SQL.

## Crecimiento y umbrales propuestos (sin alertas instaladas)

Advertencia disco 70%, intervención 85%, urgente 90%; además espacio libre suficiente para el próximo
full, copia temporal, nueva imagen y crecimiento SQL. El porcentaje solo no garantiza margen.
Revisar diariamente tendencia y días restantes; crecimiento que deje menos de siete días requiere acción.
SQL Express: revisar datos al 70% de 10 GB y actuar al 85%; LDF se mide aparte. Uploads/App_Data requieren
capacidad/retención de negocio, nunca borrado automático. Backups tienen retención existente, pero pueden
acumularse si fallan pasos previos; mantener espacio para al menos dos fulls adicionales y los logs del intervalo.
Vigilar ambos discos si BACKUP_DIR es externo. Build cache >10 GB y más de dos releases ERP son señales
de revisar inventario, no autorización de borrado. Logs de cron del host, errorlog/dumps SQL y archivos
producidos fuera de stdout no están cubiertos por la rotación Docker.

Disco lleno puede impedir commits SQL, generar fallos de uploads/contratos, bloquear backups, builds,
emisión de certificados o escritura de logs. No se llenó el disco para comprobarlo.

## Archivos modificados por este bloque

| Archivo | Motivo |
|---|---|
| Dockerfile | Pins SDK/runtime; intervalo de healthcheck 30 s |
| docker-compose.yml | Pins, ERP_IMAGE, Express por defecto, límites, hardening y logs |
| docs/docker-produccion.md | Evidencia y runbook de este bloque |

Dockerfile ya estaba modificado y Compose sin seguimiento. No atribuir sus cambios previos a este bloque.
Artefactos locales ignorados en `artifacts/docker-production/`: builds, smoke, stats.jsonl, resumen,
inspección, reportes generados y sesión/credenciales **solo de QA**. No agregarlos a Git.

## Pruebas

| Prueba | Resultado |
|---|---|
| Build multi-stage Release | OK; imagen 631 MB, restore cacheado |
| Compose productivo y overlay dev, config --quiet | OK, sin imprimir secretos |
| Stack aislado desde cero / SQL Express | OK, seis volúmenes propios |
| Tres bases nuevas, migraciones; última con límites finales | 106 aplicadas / 0 pendientes; exit 0 |
| Login Playwright + términos del usuario sintético | OK |
| Dashboard, catálogo, clientes, reportes | HTTP 200 |
| Marca CreateAjax y GetJson | Escritura/lectura real, valores coincidentes |
| PDF QuestPDF / Excel ClosedXML | MIME y firma PDF/ZIP correctos; ~26 KB / ~6,8 KB |
| 20 PDF + 20 Excel, ocho exportaciones concurrentes | OK con límites, sin OOM |
| Reinicios app, SQL y Caddy | Recuperados; HTTP health 200 |
| Persistencia | Marca, sesión y marcadores keys/uploads/App_Data conservados |
| SQL detenido | live 200 / ready 503; recuperación healthy |
| TLS local Caddy con límites/NNP | HTTPS 200, navegador negoció h2 |
| HTTP/3 | Listener habilitado y UDP configurado; negociación h3 no probada |
| Runtime-only y exclusión de estado/secretos conocidos | OK mediante inspección de imagen sin volúmenes |
| Filesystem ro y montajes escribibles | Verificados con escritura real |
| Logging / límites / NNP | Confirmados en inspect |
| Backup helper | Lectura ro de volúmenes y propietarios comprobada; no se repitió restore |

El primer login requería completar términos; el primer muestreador no produjo datos y fue sustituido.
Hubo un intento de smoke antes de readiness durante recreación; se repitió tras healthy y pasó.
Las cifras publicadas provienen de las capturas válidas posteriores. No se ejecutó suite completa.
`git diff --check` pasó sin errores (solo avisos de conversión LF/CRLF); se revisaron diff/stat/status.
El stack `bury-production-qa` quedó detenido al terminar; se conservaron sus seis volúmenes y las imágenes.
No hubo commit, staging Git, prune ni cambios a otros contenedores. Working tree conserva todos los cambios previos.

Comando para agregar únicamente los archivos tocados en este bloque (Dockerfile/Compose incluyen trabajo previo):

```powershell
git add -- Dockerfile docker-compose.yml docs/docker-produccion.md
```

## Pendientes del bloque para el host definitivo

- Validar límites con documentos grandes y datos/concurrencia representativos; especialmente Excel/PDF y migraciones futuras.
- Permisos efectivos de backups y capacidad real de discos en Linux definitivo; el bind Windows no los reproduce.
- TLS ACME público/renovación y negociación HTTP/3 no comprobados con un dominio real (fuera de este ensayo).
- db-init: captura más fina si se decide limitarlo; actualmente se conserva sin límite específico.
- Retención efectiva de errorlog/dumps SQL y log de cron del host: inventariar en el servidor, fuera de la rotación Docker.

Fuentes: [Compose services](https://docs.docker.com/reference/compose-file/services/),
[rotación json-file](https://docs.docker.com/engine/logging/drivers/json-file/),
[límites Express 2022](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022?view=sql-server-ver17).

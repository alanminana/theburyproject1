# Simulacros de copia externa por SFTP (P-06)

Complemento de `docs/backup-restore.md` §6 (copia externa), §9-10 (restore) y §14 (pendientes). Este documento describe el arnes
`scripts/backup/test-offsite-sftp.sh`, que ejercita el pipeline REAL de backup/restore contra un servidor SFTP real **descartable**,
y registra honestamente que queda demostrado y que NO.

## 1. Que es y como correrlo

Un servidor SFTP OpenSSH (imagen `atmoz/sftp` fijada por digest) en una red Docker propia, con host keys, usuario, password,
clave privada y passwords de `rclone crypt` **sinteticos generados al vuelo**; un stack Compose aislado
(`COMPOSE_PROJECT_NAME=bury-p06sftp-<rand>`, subred libre elegida al azar, SQL Server real con la imagen fijada del `docker-compose.yml`)
con una base sintetica de 5000 filas (recovery FULL) y archivos aleatorios en los 4 volumenes con estado. Sobre eso corren, sin
modificarlos, `backup.sh`, `fetch-offsite.sh`, `restore-sql.sh`, `restore-files.sh` y `restore-test.sh` con
`BACKUP_REMOTE=secure:erp` (crypt sobre SFTP, host key fijada via `known_hosts`, puerto no estandar 2222).

```bash
bash scripts/backup/test-offsite-sftp.sh                 # todos los escenarios (exit 0 = todo PASS)
bash scripts/backup/test-offsite-sftp.sh --only a,b,c    # subconjunto
bash scripts/backup/test-offsite-sftp.sh --keep          # no limpia (depuracion); imprime el comando de limpieza manual
```

Requisitos: Linux o WSL2 con Docker (`docker compose` v2, `ssh-keygen`). No corre en Git Bash/PowerShell de Windows (usa `flock`,
`stat -c`, bind mounts de ext4). En WSL, correr desde una copia del repo cuyo `.git` funcione o directamente desde `/mnt/<unidad>/...`.
Memoria: la base usa `DB_MEMORY=2g`. Duracion: ver §3.

Aislamiento y limpieza:

* Proyecto, red, volumenes, contenedor SFTP y directorio temporal llevan el nombre unico `bury-p06sftp-<rand>`; la limpieza (trap `EXIT`)
  solo elimina recursos con la etiqueta de ESE proyecto. Nunca `docker system prune`.
* No toca `TheBuryProjectDb`, LocalDB, backups reales ni el stack real: `BACKUP_DIR`, volumenes y credenciales son del simulacro.
* Escribe `./backup-offsite.env` (sintetico, con una marca en la primera linea) en la raiz del repo mientras corre y lo borra al salir.
  Si ya existe uno que NO lleva esa marca (configuracion real), el arnes ABORTA sin tocarlo.
* Al final verifica que no quedan contenedores ni volumenes y que ninguna password, `obscure` ni clave aparece en las salidas ni en `backup.log`.

## 2. Que prueba (escenarios)

| Esc. | Que demuestra | Aserciones automaticas |
|---|---|---|
| setup | SFTP + crypt operativos con host key fijada y puerto no estandar | `rclone lsf` del centinela via crypt |
| a | `backup.sh all` sube cifrado y verifica byte a byte (SFTP no expone hash comun) | exit 0; "subida y VERIFICADA" sql/files; el servidor tiene exactamente 6 objetos; TODOS los nombres son base32 opacos; ni el nombre de la base ni texto de metadatos en claro en el servidor; via crypt se ven los nombres reales; manifiesto del par subido |
| b | Retencion remota con `deletefile` sobre crypt+SFTP respeta el minimo de copias | fulls y tars viejos borrados (principal y `.sha256`), el de 40 dias conservado por el minimo de 3, sin `.sha256` huerfanos, conteo en el log |
| c | Borrado denegado en el servidor (directorios sticky de root, archivos de root) | exit 6, "retencion remota INTERRUMPIDA ... permission denied", 0 borrados, todo lo previo sigue en el remoto, la corrida si subio lo nuevo, **no** corrio la retencion local, el log no dice OK |
| d | Fallo de conexion: servidor detenido (con y sin centinela), puerto cerrado, host key cambiada, credenciales invalidas | por caso: exit 6, causa legible en el mensaje (sin secretos), sin retencion local (canario local intacto), el log no dice OK. Luego recuperacion: la corrida siguiente repone los fulls que no pudo subir, no sube lo viejo (`--max-age`), ahora si poda local y remoto |
| g | Password de `crypt` incorrecta | exit 6 con "no se pudo comprobar el centinela ... Causa:", **nada** escrito en el servidor; `fetch-offsite` falla con exit 6 y causa |
| h | Autenticacion por clave privada montada desde `BACKUP_OFFSITE_SSH_DIR` (sin password) | exit 0, subida verificada |
| e | Corrupcion: (e1) un byte alterado en el servidor sobre crypt; (e1b) sobre SFTP plano sin crypt; (e2) full local alterado | e1: `fetch-offsite` falla (exit 6/3) y el archivo alterado NO queda aceptado; se repara y vuelve a leerse. e1b: exit 3 por sha256 de origen, `restore-files` lo rechaza (exit 9) sin tocar los volumenes. e2: `restore-sql` exit 3 por sha256; sin `.sha256`, `RESTORE VERIFYONLY ... CHECKSUM` tambien lo rechaza (exit 3); reparado, restaura |
| f | Perdida total: se borran `BACKUP_DIR` y todos los volumenes; recuperacion solo desde el SFTP | `fetch-offsite` (exit 0, sha256 de origen); `restore-sql --with-logs` (>=1 log) deja la base IDENTICA (filas:`CHECKSUM_AGG`); `restore-files` deja el arbol IDENTICO (sha256 de todo el arbol); `restore-test.sh` (restore + `DBCC CHECKDB`) OK |

## 3. Resultados reales

Corrida completa del 2026-11-12 (WSL2 Ubuntu + Docker Desktop 29.6.2, rclone `1@sha256:45401ad7...`, SQL Server de la imagen fijada del
compose, `atmoz/sftp@sha256:c5fb9e4b...`): **86 PASS, 0 FAIL en 452 s**, sin residuos (0 contenedores, 0 volumenes, tmp eliminado).
Una primera corrida dio 83 PASS y 1 FAIL por un error del propio arnes (el canario del escenario c recalculaba su nombre con otra
hora); corregido y repetida completa.

Evidencia destacada (salida real):

* a: el servidor tiene 6 objetos, todos con nombres base32 opacos; `grep` de la base/metadatos en claro sobre el arbol del servidor: 0.
* b: `deletefile` sobre crypt+SFTP funciona: borrados 3 fulls y 2 tars viejos (+ sus `.sha256`), conservado el de 40 dias por el minimo de 3.
* c: `retencion remota INTERRUMPIDA tras borrar 0 archivo(s)` + permission denied, exit 6, nada borrado, la retencion local no corrio.
* d: los 5 modos de fallo dan exit 6 con la causa (`no such host`, `connection refused`, `knownhosts: key mismatch`, `unable to authenticate`);
  la corrida siguiente repuso los 5 fulls no subidos; la retencion local y la remota volvieron a funcionar.
* g: con password crypt incorrecta y centinela configurado: exit 6 sin escribir nada. **Sin centinela una password incorrecta NO falla**:
  rclone crypt cifra con otra clave y la subida "funciona" (arbol paralelo ilegible). Por eso con crypt conviene
  `BACKUP_OFFSITE_MARKER` aunque el destino sea SFTP/S3/B2 (el `.env.example` dice que "no hace falta": con crypt SI).
* e: sobre crypt, un byte alterado en el servidor hace fallar `fetch-offsite` (autenticacion del descifrado); sin crypt lo detecta el
  sha256 de origen (exit 3) y `restore-files` rechaza el tar (exit 9) sin tocar los volumenes; un full local alterado lo rechaza
  `restore-sql` (sha256) y, sin `.sha256`, `RESTORE VERIFYONLY ... CHECKSUM`.
* f: con `BACKUP_DIR` y volumenes borrados, solo desde el SFTP: base restaurada identica (5500 filas, mismo `CHECKSUM_AGG`) tras full + log,
  arbol de archivos identico (sha256 de todo el arbol) y `restore-test.sh` (restore + `DBCC CHECKDB`) OK.
* Sin `known_hosts` rclone acepta un servidor con OTRA clave (verificado: exit 0); ahora `backup.sh` lo advierte en el log.

Tests rapidos (sin Docker): `bash scripts/backup/test-hardening.sh` -> 65 OK, 0 fallos (incluye `test-retention-remote.sh`, 98 casos);
antes de los cambios: 61 OK.

## 4. Hallazgos corregidos durante la auditoria

La auditoria de `backup.sh`, `lib.sh`, `fetch-offsite.sh`, `restore-*.sh` y de sus tests no encontro errores de consistencia en el
respaldo SQL (CHECKSUM + VERIFYONLY + sha256), la retencion ni el restore (todo se confirmo ademas con datos reales en el simulacro).
Si encontro tres defectos de diagnostico/seguridad operativa, corregidos con test:

1. **Centinela: causa descartada.** `backup.sh` ejecutaba `rclone lsf` del centinela con `2>/dev/null`; ante cualquier fallo (servidor caido,
   host key, credenciales, password crypt incorrecta) el mensaje decia solo "falta el centinela ... ¿disco desconectado?". Ahora incluye
   `Causa: <stderr de rclone sanitizado>`. Test: `test-hardening.sh` ("centinela: remoto inaccesible") y escenarios d/g.
2. **`fetch-offsite.sh`: "descarga fallo" sin causa.** El stderr de `rclone copy` no se capturaba. Ahora el mensaje de salida (exit 6) lleva la
   causa sanitizada. Test: escenario g.
3. **SFTP sin host key fijada, en silencio.** Sin `KNOWN_HOSTS_FILE` rclone acepta cualquier servidor y el log no decia nada. Ahora
   `backup.sh` registra un WARN (`warn_hostkey_unpinned` en `lib.sh`). Test: `test-hardening.sh` y escenario d. Ademas `known_hosts` y la
   clave privada no llegaban al contenedor rclone: se agrego el overlay `docker-compose.offsite-sftp.yml` (§5).

Observaciones sin cambio de codigo (documentar/decidir): (a) con crypt y sin centinela, una password incorrecta no se detecta al subir (ver g);
(b) rclone exige en `known_hosts` la clave del TIPO que negocia el servidor (si el servidor ofrece ed25519 y rsa y el archivo solo tiene
ed25519, falla con `key mismatch`): usar `ssh-keyscan` (una linea por tipo); con puerto no estandar la linea lleva `[host]:puerto`.

## 5. Configuracion del SFTP real futuro (solo `backup-offsite.env` + `.env`)

Se confirma que NO hace falta tocar scripts ni `docker-compose.yml`:

| Dato | Donde |
|---|---|
| host, puerto, usuario | `backup-offsite.env`: `RCLONE_CONFIG_OFFSITE_HOST/PORT/USER` |
| password | `backup-offsite.env`: `RCLONE_CONFIG_OFFSITE_PASS` (salida de `rclone obscure`) |
| clave privada | `backup-offsite.env`: `RCLONE_CONFIG_OFFSITE_KEY_FILE=/config/id_ed25519`; el archivo vive en `BACKUP_OFFSITE_SSH_DIR` (host) |
| known_hosts (host key estricta) | `RCLONE_CONFIG_OFFSITE_KNOWN_HOSTS_FILE=/config/known_hosts`; archivo en `BACKUP_OFFSITE_SSH_DIR` |
| ruta remota | `RCLONE_CONFIG_SECURE_REMOTE=offsite:<ruta relativa al home del usuario SFTP>` y `BACKUP_REMOTE=secure:erp` en `.env` |
| cifrado | `RCLONE_CONFIG_SECURE_*` (crypt) en `backup-offsite.env` |

Lo unico nuevo fue el overlay `docker-compose.offsite-sftp.yml` (monta `BACKUP_OFFSITE_SSH_DIR` en `/config`, solo lectura, como ya
hace `docker-compose.offsite-disk.yml` para el disco externo); sin el, `known_hosts` y la clave privada no llegaban al contenedor rclone.
Activacion: `COMPOSE_PATH_SEPARATOR=,` y `COMPOSE_FILE=docker-compose.yml,docker-compose.offsite-sftp.yml` en `.env` (asi tambien lo
heredan cron y el Task Scheduler). Ejemplo completo en `backup-offsite.env.example`.

## 6. Limitaciones: lo que sigue SIN probarse con un SFTP real de proveedor

* El servidor del simulacro es OpenSSH en Docker con POSIX permissions. Un proveedor real (Hetzner Storage Box, rsync.net, un NAS,
  un SFTP gestionado) puede diferir en: restricciones de comandos (jaulas sin shell, `ForceCommand`), limites de conexiones/tasa,
  cuotas, soporte de `rename`/`setstat` (rclone sube y luego fija mtime), politicas "append-only"/inmutabilidad (el escenario c simula
  "sin permiso de borrado" con permisos POSIX, no con la politica real del proveedor), tiempos de espera y latencia/ancho de banda.
* Volumen y duracion reales: la base y los archivos son pequenos; no se midio el tiempo de subida/descarga de un backup real ni el
  comportamiento ante cortes largos a mitad de una subida de GB.
* La identidad del servidor depende de que `known_hosts` se obtenga por un canal independiente; el arnes solo prueba que una clave
  cambiada se rechaza.
* El procedimiento humano (custodia de las passwords `crypt`, de `BACKUP_OFFSITE_SSH_DIR` y de la clave privada FUERA del servidor
  de produccion) no es automatizable aqui; ver `docs/backup-restore.md` §6 y los documentos de custodia.
* No se probo `app` corriendo (el stack del simulacro solo levanta `db`): la parada/arranque de `app` durante el backup tiene sus
  propias pruebas en `test-retention-remote.sh`/`test-hardening.sh`.

## 7. Pendiente del proveedor / propietario

* Elegir y contratar el proveedor (o disponer el servidor SFTP propio) y entregar host, puerto, usuario y metodo de autenticacion.
* Obtener las huellas del servidor por un canal independiente y armar `known_hosts`.
* Repetir en el servidor definitivo, con credenciales reales y un backup real, la prueba minima: `backup.sh all` con
  `BACKUP_REQUIRE_OFFSITE=true`, `fetch-offsite.sh` a un directorio vacio y `restore-test.sh`; y, si el proveedor ofrece
  inmutabilidad/append-only, un ensayo de la retencion remota contra esa politica.

# Custodia de claves de respaldo (P-06 / P-07 / P-08)

Complementa `docs/backup-restore.md` (§6.3 cifrado `crypt`, §14 pendientes). Aquí: **qué clave existe, dónde vive, dónde no puede
vivir, cómo se verifica automáticamente, cómo se rota y qué hacer si se compromete**. No contiene valores reales ni elige proveedor.

## 1. Regla central

> Las claves que descifran los respaldos y el material de recuperación **no pueden almacenarse junto a los respaldos cifrados**:
> ni dentro de `BACKUP_DIR`, ni en el remoto/offsite, ni en el mismo directorio o volumen que el paquete `.enc`, ni en Git, ni en logs.

Un respaldo cifrado cuya clave está al lado es, a efectos prácticos, un respaldo sin cifrar; y una clave que solo existe en el servidor
muere con el servidor (se pierde la única copia legible del remoto). Hacen falta **dos** cosas a la vez: separación (confidencialidad) y
una copia de la clave fuera del servidor (recuperabilidad).

## 2. Inventario de secretos

| Secreto | Para qué | Dónde vive operativamente (necesario) | Dónde NO puede ir | Copia de recuperación |
|---|---|---|---|---|
| `RCLONE_CONFIG_SECURE_PASSWORD` y `_PASSWORD2` (salidas de `rclone obscure`) | Cifran contenido y nombres en el remoto `crypt` | `backup-offsite.env` en el host de la app (modo 600, fuera de Git, fuera de `BACKUP_DIR`); lo lee solo el contenedor `backup-offsite`/`restore-offsite` | `BACKUP_DIR`, directorio offsite local/NAS, el remoto, Git, logs, imagen Docker | Las **passwords en claro** (P1/P2, no la forma obscure) en custodia externa independiente (§3) |
| Credencial de transporte (`RCLONE_CONFIG_OFFSITE_PASS`/`_KEY`/clave SSH privada) | Acceso al SFTP/S3/B2 | Mismo `backup-offsite.env`; la clave SSH privada en `$StateDir`/archivo con ACL restringida | Los mismos que arriba | Re-emitible desde el proveedor; no hace falta custodiarla si se puede regenerar |
| Passphrase del paquete de recuperación (`.enc`) | Descifra el paquete que contiene lo necesario para reconstruir (P1/P2, `.env`, etc.) | **En ningún servidor.** Solo se usa en el momento de recuperar | Mismo directorio/volumen/host que el `.enc`; `BACKUP_DIR`; remoto; consola/logs; historial del shell | Custodia A (ver §3) |
| Paquete de recuperación `.enc` | Contiene los secretos para volver a levantar | Cifrado; puede vivir en medio extraíble/cuenta del custodio | Mismo directorio que su passphrase | Custodia B (distinta de A) |
| `MSSQL_SA_PASSWORD`, `.env` | Operación de la app/SQL | `.env` del host (modo 600) | `BACKUP_DIR`, Git, logs | Dentro del paquete `.enc` |

Nota: `rclone obscure` **no es cifrado**: es una ofuscación reversible. Quien lee `backup-offsite.env` obtiene las passwords crypt.
Por eso el archivo es 600, vive fuera de `BACKUP_DIR` y la compromisión del host implica la compromisión del remoto (§10).

## 3. Modelo de custodia recomendado (sin elegir proveedor)

Dos custodias **independientes** (no la misma cuenta, no el mismo lugar físico, no el mismo dispositivo):

- **Custodia A: la passphrase del paquete** y las passwords crypt en claro. Gestor de contraseñas/secretos con acceso de al menos dos personas
  de confianza, o papel en sobre sellado en caja fuerte/ubicación distinta al servidor. Nunca en el servidor de producción ni en su nube de respaldo.
- **Custodia B: el paquete `.enc`** (y su `.sha256`, guardado por separado para detectar corrupción: AES-CBC no autentica). USB cifrado/disco
  desconectado guardado en otro sitio, o repositorio privado de la organización distinto del que aloja el código. Debe poder leerse sin el servidor.

Condiciones: (1) quien tenga solo A o solo B no puede descifrar nada; (2) ninguna de las dos depende de que el servidor esté vivo;
(3) hay al menos **dos** personas que saben dónde está cada una (factor de bus); (4) la recuperación se ensaya (§7).

Dónde sí está permitido que coexistan: en la mente/mano del custodio durante un ensayo o una recuperación, en memoria o tmpfs, y se borra al terminar.

## 4. Auditoría realizada (estado del repositorio, 2026-11-12)

| # | Hallazgo | Severidad | Estado |
|---|---|---|---|
| 1 | `backup-offsite.env` (con passwords crypt obscure) debe estar en el host. `.gitignore` (`backup-offsite.env`, `*.env`) y `.dockerignore` lo excluyen; el compose lo monta solo en los servicios de herramientas | Aceptado (inherente a un backup desatendido) | Mitigación: modo 600; verificador C02 |
| 2 | La documentación histórica (`docs/offsite-monitoring-activacion.md` §6, `docs/propuesta-infraestructura-produccion.md`, `docs/p07-preparacion-windows-docker-tailscale.md` riesgo 3) registra que `recovery-passphrase.txt` quedó junto al `.enc` en el mismo host, y el riesgo se "aceptó" | **Alto** (anula el valor del cifrado offsite) | Ahora detectable: verificador C09 (raíces `~/secure-preprod`, `~/secure-prod` y `CUSTODY_RECOVERY_DIRS`). La corrección real (mover A/B fuera) es del propietario |
| 3 | `docs/offsite-monitoring-activacion.md` (sección de sanitización, previa a §6) reconoce que un `RCLONE_CONFIG_SECURE_PASSWORD2` sintético se imprimió en consola en una prueba, y en la copia histórica una passphrase se imprimió y otra quedó con BOM por el pipe de PowerShell 5.1 | Medio | `sanitize_text` redacta `*PASSWORD2` en logs (cubierto por `test-hardening.sh`); el verificador C06 busca el valor en claro en logs/estado. Recomendación: generar claves con stdin (§6) y nunca con pipe de PS 5.1 |
| 4 | `backup-offsite.env.example` indica `docker run --rm rclone/rclone obscure "$P1"`: la password en claro queda en `argv` (visible en `ps`/`docker inspect`) y en el historial del shell | Medio | Procedimiento nuevo en §6 (`obscure -` por stdin, `read -s`, `HISTFILE=/dev/null`). No se modificó el ejemplo (archivo fuera del alcance de este lote) |
| 5 | `install-windows-server.ps1` crea la clave SSH del servidor con passphrase vacía (`-N ""`), necesaria para el SFTP desatendido, con ACL restringida a Administradores/SYSTEM por SID | Aceptado | La clave de transporte es re-emitible; no es la clave de descifrado. Sigue siendo una credencial en el host: rotar si el host se compromete |
| 6 | Nada en `scripts/` escribe las passwords crypt en `BACKUP_DIR`, `backup.log` ni `monitor-status/*.json` (estos solo guardan tiempo, exit y mensajes sanitizados) | OK | Verificador C05/C06 lo comprueba en cada corrida |
| 7 | El tar de `files` (keys de Data Protection, uploads, caddy-data) viaja al remoto: su confidencialidad depende por completo de las passwords crypt | Informativo | Refuerza la necesidad de custodia A |
| 8 | No existía verificación automática ni alerta por custodia insegura ni prueba de restauración desde el remoto | Alto | Implementado en este lote (§5, §8) |

## 5. Verificador automático: `scripts/backup/check-custody.sh`

Solo lectura. **Nunca imprime valores**: solo regla, ruta y motivo. Sale con `0` (ok) o `11` (hay al menos un FAIL); `1` si la configuración es
inválida. Escribe `$BACKUP_DIR/monitor-status/custody.json` (omitible con `--no-status`), que lee `monitor.py`.

```bash
scripts/backup/check-custody.sh                 # verificación local + estado para el monitor
scripts/backup/check-custody.sh --check-remote  # además lista nombres del remoto (rclone lsf -R) y busca claves
scripts/backup/check-custody.sh --quiet         # solo FAIL/WARN y resumen (modo cron)
```

| Regla | Falla si | 
|---|---|
| C01 | `.env`/`COMPOSE_ENV_FILES` con permisos para grupo/otros. En NTFS/DrvFs/9p/Git Bash (donde `chmod` no existe) no se puede verificar: emite **WARN** visible (también con `--quiet`), no un OK silencioso |
| C02 | `backup-offsite.env` con modo != 600, o `backup-offsite.env`/`.env` dentro de `BACKUP_DIR` o del directorio offsite local |
| C03 | `BACKUP_DIR` contiene nombres tipo `*passphrase*`, `*password*`, `*recovery*`, `*.key`, `*.pem`, `id_*`, `rclone.conf`, `.env`, `backup-offsite.env`, y sus equivalentes en español (`*clave*`, `*contraseña*`/`*contrasena*`, `*llave*`, `*secreto*`/`*secret*`, `*credencial*`, `*recuperaci*`). Se ignoran `*.example` y los paquetes cifrados (los cubre C09) |
| C04 | Lo mismo en `BACKUP_OFFSITE_DISK_DIR` (disco/NAS montado) |
| C05 | Un archivo de texto de `BACKUP_DIR` contiene `RCLONE_CONFIG_*PASS/KEY/SECRET=<valor>`, `MSSQL_SA_PASSWORD=<valor>` o una clave privada PEM (un `[REDACTED]` no cuenta) |
| C06 | El valor de la password crypt aparece tal cual en `logs/`, `monitor-status/`, `~/bury-backup-errors.log`, `/var/log/bury-backup-errors.log` o `CUSTODY_EXTRA_LOGS` |
| C07 | `_PASSWORD`/`_PASSWORD2` del remoto activo faltan, son iguales, no tienen forma de salida de `obscure` (≥ 22 caracteres base64url), son placeholders (`changeme`, `<obscure P1>`…) o caracteres repetidos. **Valida forma, no fortaleza**: una password débil pasa si tiene forma válida (ver §10) |
| C08 | Hay `BACKUP_REMOTE` y: falta `BACKUP_REQUIRE_OFFSITE=true`; o el remoto no es `crypt`/no se puede comprobar; o `FILENAME_ENCRYPTION=off`. También: `REQUIRE_OFFSITE=true` sin remoto. Sin remoto ni REQUIRE: solo WARN |
| C09 | Una raíz (`BACKUP_DIR`, offsite local, `~/secure-preprod`, `~/secure-prod`, `CUSTODY_RECOVERY_DIRS`) contiene un paquete (`*.enc`, `*.gpg`, `*.age`, `*.7z`, `*.zip`) **y** passphrase/clave (`*passphrase*`, `*password*`, `*recovery-key*`, `*.key`, `*.pem`, `rclone.conf`, `*clave*`, `*contraseña*`, `*llave*`, `*secret*`, `*credencial*`). Un paquete dentro de `BACKUP_DIR` genera WARN |
| C10 | Git versiona `.env`, `backup-offsite.env`, `rclone.conf`, `*passphrase*`, `*.enc`, `*.pem`, `*.key` (excepto `*.example`). Si el directorio no es un repositorio Git (o falta `git`) emite **WARN** explícito: no pudo verificarse |
| C11 | (`--check-remote`) el remoto contiene nombres con aspecto de clave |

Los symlinks se resuelven (`readlink -f`) antes de comparar rutas y de escanear: un `BACKUP_DIR`, un directorio offsite o un `backup-offsite.env`/`.env` que sea un enlace hacia dentro de un directorio de respaldos se detecta (C02-C04).

Se ejecuta **solo en bash** (un único script): en Windows las tareas corren dentro de WSL (`install-windows.ps1` → `wsl.exe … bash`) y los
secretos viven en el sistema de archivos de WSL/Linux, donde el chequeo de modos es real; un `Check-Custody.ps1` duplicaría 11 reglas para
revisar ACL NTFS de archivos que, en esta arquitectura, no residen en NTFS. En Windows nativo/Git Bash C01/C02 informan "omitido".

Programación: `install-schedule.sh` agrega un cron semanal (una hora después del restore-test) y `install-windows.ps1` la tarea
`TheBury Check Custody` (domingo 05:30; se verifica con `-DryRun`, sin registrar tareas). No hay instalación automática en equipos reales.

Tests: `scripts/backup/test-check-custody.sh` (casos sintéticos positivos/negativos: 47 en Git Bash, 53 en Linux; los de permisos y symlinks solo corren en Linux/WSL/Docker).

## 6. Generar las passwords crypt sin dejar rastro

Hacerlo en una terminal del custodio (idealmente en una máquina que no sea el servidor), no en PowerShell 5.1 (el pipe agrega BOM):

```bash
set +o history 2>/dev/null; HISTFILE=/dev/null
P1=$(openssl rand -base64 24); P2=$(openssl rand -base64 24)       # NO imprimir ($P1/$P2 no se muestran)
O1=$(printf '%s' "$P1" | docker run --rm -i rclone/rclone obscure -)   # stdin: la password no aparece en argv
O2=$(printf '%s' "$P2" | docker run --rm -i rclone/rclone obscure -)
# Guardar P1 y P2 en la Custodia A (gestor/papel). Copiar O1/O2 SOLO a backup-offsite.env del servidor (chmod 600).
unset P1 P2 O1 O2
```

Después: `scripts/backup/check-custody.sh` debe dar `custodia OK`, y `scripts/backup/restore-test.sh --offsite` (§8) confirma que lo subido se descifra.
Si el `.enc` se crea con openssl: `openssl enc -aes-256-cbc -pbkdf2 -iter 600000 -salt -in paquete.tar -out paquete.tar.enc -pass stdin`
(passphrase por stdin con `read -s`), y registrar `sha256sum paquete.tar.enc` aparte.

## 7. Verificación periódica

| Qué | Frecuencia | Cómo | Quién alerta |
|---|---|---|---|
| Custodia (reglas C01-C10) | semanal (cron/tarea) | `check-custody.sh --quiet` | `backup.custody` (monitor): CRITICAL si falla; warning 8 d / critical 15 d sin corrida |
| Restore desde el remoto | mensual (a mano o cron propio, ver abajo) | `restore-test.sh --offsite` | `backup.offsite-restore-test`: CRITICAL si falla; warning 35 d / critical 45 d |
| Restore local | semanal | `restore-test.sh` | `backup.restore-test` (ya existente) |
| Copia externa verificada | cada corrida | `backup.sh` | `backup.offsite-sql` / `backup.offsite-files` (ya existentes) |
| **Ensayo humano de recuperación** | semestral y al cambiar de custodio | con solo A + B (sin acceso al servidor) descifrar el paquete y una muestra del remoto | manual |
| Custodia B sigue legible | semestral | abrir el medio, comprobar el `sha256` | manual |

Cron mensual sugerido (no se instala solo: es pesado y baja un full completo):
`30 6 1 * *  <usuario>  cd <repo> && /bin/bash scripts/backup/restore-test.sh --offsite >/dev/null 2>>/var/log/bury-backup-errors.log`.

### Cómo cubre las alertas el monitor (`scripts/monitoring/monitor.py`)

| Necesidad | Señal | Umbrales (config.example.json) |
|---|---|---|
| (a) ausencia de éxito verificado de la copia externa | `backup.offsite-sql`, `backup.offsite-files` (ya existían: CRITICAL si falta el estado, `exit != 0`, `verified != true` o fuera de ventana) | `backup_windows` |
| (b) fallo de verificación de custodia | `backup.custody` (nueva) | `custody`: `enabled`, `warning` 691200 s (8 d), `critical` 1296000 s (15 d) |
| (c) restore-test desde el remoto | `backup.offsite-restore-test` (nueva) | `offsite_restore_test`: `enabled` (`"auto"` = solo si ya existe `offsite-sql.json`), `warning` 3024000 s (35 d), `critical` 3888000 s (45 d) |
| (d) edad de la última verificación de custodia | `backup.custody` (misma señal: la edad del último éxito) | ídem |

Las tres señales de "prueba periódica" comparten una única implementación (`Collector.restore_test` parametrizada) y un formato de estado
(`restore_test_status` en `lib.sh`). Un estado inexistente tiene gracia hasta el umbral desde la primera observación, luego alerta. Los mensajes
de estado ya van sanitizados y solo contienen identificadores de regla y causas, nunca valores. Las alertas no ejecutan acciones: solo notifican.

`restore-test.sh --offsite` avisa si el último full remoto es más viejo que `BACKUP_OFFSITE_RESTORE_MAX_AGE_DAYS` (default 3; según el nombre, UTC): por defecto WARN
(log y mensaje del estado), o fallo (exit 6) con `BACKUP_OFFSITE_RESTORE_STALE=fail`. En `config.example.json`, `enabled` acepta `true/false` y también los strings `"false"`, `"0"`, `"off"`, `"no"` (desactivan) y `"auto"`.

`restore-test.sh --offsite`: descarga el último full de `BACKUP_REMOTE` (pasa por `crypt`, o sea prueba las passwords vigentes) con prefijo
`offsitert_`, verifica su `.sha256` de origen, lo restaura en la base aislada `<db>_OffsiteRestoreTest`, valida (`DBCC CHECKDB`, migraciones), y **borra** la
copia descargada y la base. No toca la base viva, ni `<db>_RestoreTest`, ni `restore-test.json`. Requiere espacio libre igual al tamaño del full en `BACKUP_DIR/sql`.
Probado con un `docker` falso (`scripts/backup/test-offsite-restore-test.sh`, 24 casos); **no se ejecutó contra un SQL Server ni un remoto reales** en este lote.

### Notas de despliegue (importante)

- **`custody` (8/15 d) y `offsite-restore-test` (35/45 d) alertan si no se programan.** `backup.custody` está habilitada por defecto: sin `check-custody.sh` programado,
  tras la gracia inicial pasa a WARNING a los 8 días y CRITICAL a los 15. `backup.offsite-restore-test` se activa sola (`"auto"`) en cuanto existe `offsite-sql.json`, y
  **no tiene cron automático**: sin programarla a mano alerta a los 35/45 días. Si no se va a usar alguna, desactivarla explícitamente en la config (`"enabled": false`).
- `install-schedule.sh` y `install-windows.ps1` programan `check-custody.sh` (semanal); el restore desde el remoto hay que agregarlo (cron mensual sugerido arriba).

## 8. Rotación

Rotar cuando: cambia el custodio, hay baja de personal con acceso, sospecha de exposición, o anualmente.

**Passwords crypt (P1/P2).** `crypt` no re-cifra en sitio; se migra a un remoto nuevo:

1. Generar P1'/P2' (§6) y guardarlas en la Custodia A **antes** de usarlas (las viejas se conservan hasta el paso 7).
2. En `backup-offsite.env` agregar un remoto adicional (`RCLONE_CONFIG_SECURE2_*` con las nuevas) sin quitar el actual.
3. Migrar descifrando con las viejas y cifrando con las nuevas:
   `docker compose run --rm backup-offsite copy secure:erp secure2:erp` (puede tardar y consumir cuota del proveedor).
4. Verificar: `docker compose run --rm backup-offsite check secure:erp secure2:erp --download --one-way` (compara contenido descifrado).
5. Cambiar `BACKUP_REMOTE=secure2:erp` en `.env`, ejecutar `backup.sh all` y `restore-test.sh --offsite`. `check-custody.sh` debe pasar.
6. Esperar un ciclo completo de retención sin alertas (≥ 7 días).
7. **Con aprobación del propietario**, retirar los datos del remoto viejo y las passwords viejas de la Custodia A. Registrar fecha.

(El procedimiento no se ejecutó aquí contra un remoto real; ensayarlo primero con el remoto `local` de `docs/backup-restore.md` §6.1.)

**Passphrase del paquete `.enc`.** En un equipo seguro: descifrar con la vieja, recifrar con una nueva (§6), calcular el nuevo `sha256`,
guardar la nueva en A y el nuevo `.enc` en B, destruir copias antiguas de ambos.

**Credencial de transporte (SFTP/S3/B2).** Emitir una nueva en el proveedor, actualizar `backup-offsite.env`, probar con `backup.sh`, revocar la vieja.

## 9. Si se compromete una passphrase o una clave

1. **Contener**: identificar qué se expuso (¿passphrase? ¿también el `.enc`? ¿`backup-offsite.env`? ¿acceso al remoto?). Anotar hora y alcance; no borrar evidencia.
2. **Passphrase sola (el atacante no tiene el `.enc`)**: rotarla (§8) y destruir la antigua. Riesgo bajo, pero la passphrase ya no sirve como secreto.
3. **Passphrase + `.enc`**: el contenido del paquete está comprometido. Rotar **todo lo que contiene**: P1/P2 (§8, migración completa), `MSSQL_SA_PASSWORD`
   y secretos del `.env`, credenciales de transporte, claves de Data Protection si estaban incluidas (invalida sesiones/cookies).
4. **Passwords crypt + acceso al remoto**: los respaldos del remoto (SQL con datos de clientes y el tar de `files`) pueden haberse leído. Tratar como incidente
   de datos: evaluar obligaciones de notificación según corresponda y rotar P1/P2 + migrar; los datos ya descargados no se "des-exponen".
5. **Host comprometido**: asumir expuestos `backup-offsite.env`, `.env` y la clave SSH de transporte (§2). Rotar todo lo anterior desde una máquina limpia.
6. Tras rotar: `check-custody.sh`, `backup.sh all`, `restore-test.sh --offsite`; registrar el incidente y la causa (¿por qué coexistían?).

## 10. Limitaciones

- El verificador mira **nombres** y contenidos de texto pequeños dentro de los directorios que conoce. No detecta una clave guardada con un nombre
  inocuo ni dentro de un archivo comprimido, ni lo que haya en otra máquina, en el gestor de contraseñas o en una nube de usuario. No sustituye a la disciplina de custodia.
- C07 valida **forma, no fortaleza**: una password débil o reutilizada con forma válida pasa. La fortaleza depende de generarla con `openssl rand` (§6).
- Las passwords `obscure` se validan por **forma** (longitud/alfabeto/placeholder/igualdad textual). No puede saber si la password real es débil: `obscure` usa un IV
  aleatorio (dos obscure de la misma password son textos distintos) y revelarla exigiría invocar `rclone reveal` con la password en `argv`. Generar con `openssl rand` (§6).
- C06 busca el valor obscure tal como figura en el env; no puede encontrar la password en claro (nunca debería estar en el servidor) ni valores ya rotados.
- No revisa logs de Docker (`docker logs`), journald ni volcados de memoria. Los contenedores de herramientas tienen `json-file` con rotación.
- C01/C02 no corren en NTFS/Git Bash (los modos no existen); en Windows con WSL el check corre dentro de la distro.
- C09 inspecciona raíces conocidas (`~/secure-preprod`, `~/secure-prod`) y `CUSTODY_RECOVERY_DIRS`; no puede ver un paquete guardado en otra ubicación.
- La compromisión del host del ERP compromete el remoto (el host necesita las passwords para cifrar). El modelo protege contra: pérdida/robo del remoto,
  del proveedor y de la copia en disco/NAS; **no** contra un administrador del host malicioso.
- La prueba `--offsite` valida el **último full** y su integridad/restaurabilidad; no recorre toda la cadena de logs ni todas las copias históricas.

## 11. Pendientes del propietario

1. Decidir y armar las custodias **A** y **B** (§3) y mover fuera del servidor `recovery-passphrase` y el `.enc` (hoy coexisten según los documentos históricos). Ejecutar luego `check-custody.sh` en el servidor real y corregir lo que reporte.
2. Generar las passwords crypt **definitivas** (§6; no reutilizar las de pruebas) y guardarlas en A.
3. Elegir/contratar el destino offsite (SFTP/S3/B2) y completar `backup-offsite.env` + `BACKUP_REQUIRE_OFFSITE=true`.
4. Decidir si programar `restore-test.sh --offsite` (mensual) y activar el canal de alertas (`BURY_MONITOR_WEBHOOK`).
5. Ensayo humano de recuperación (solo A + B) y registrar la fecha. Designar al menos dos personas que sepan dónde está cada custodia.
6. Aprobar la actualización de `docs/offsite-monitoring-activacion.md`, `docs/p07-preparacion-windows-docker-tailscale.md` (riesgo 3 "aceptado") y `backup-offsite.env.example` (§4 hallazgos 2 y 4).

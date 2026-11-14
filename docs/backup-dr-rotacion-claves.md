# P-06: ensayo de rotación de claves, recuperación integral y custodia de la CA de Caddy

> Estado: **ensayado en entorno aislado y sintético. P-06 NO está cerrado**: faltan proveedor externo real, custodia real y ejecución productiva
> (ver §6). Todo lo de este documento se probó con datos inventados, servidores SFTP descartables y Docker; ningún respaldo, contraseña ni base real
> (`TheBuryProjectDb`) fue tocado.

## 1. Qué ya estaba cubierto y qué agregan estos ensayos

| Ya existía | Cobertura | Qué faltaba (lo agregado aquí) |
|---|---|---|
| `test-offsite-sftp.sh` (a-h) | subida cifrada, retención remota, fallos de conexión, password crypt incorrecta, corrupción, pérdida total de **SQL y archivos** (base sintética de 1 tabla) | el ERP real no se levantaba tras la recuperación; no se medía el RTO; no se probaba la CA |
| `test-check-custody.sh`, `docs/backup-custodia.md` §8 | custodia de claves; rotación **solo documentada** (comandos manuales) | rotación ejecutada, interrumpida, con password incorrecta y con rollback |
| ensayo manual 2026-10-29 (`backup-restore.md` §10.1) | RTO/RPO con el ERP real, a mano y desde un disco | no repetible ni automatizado; no cubría la CA ni la rotación |
| `scripts/install/test-caddy-tls.sh` | extracción de la CA pública y confianza TLS | recuperación y rotación de la CA, custodia de `root.key` |

Nuevo: `scripts/backup/rotate-crypt.sh`, `scripts/backup/test-key-rotation.sh`, `scripts/backup/test-dr-integral.sh`, regla `C12` en `check-custody.sh`,
workflow `.github/workflows/backup-dr-rotacion.yml`.

## 2. Rotación de las passwords crypt (P1/P2)

`rclone crypt` no re-cifra en sitio: rotar es **copiar descifrando con la clave vieja y cifrando con la nueva** a un remoto nuevo, verificar y cambiar
`BACKUP_REMOTE`. `rotate-crypt.sh` automatiza ese paso con garantías que se prueban:

```bash
# backup-offsite.env: agregar RCLONE_CONFIG_SECURE2_* (TYPE=crypt, REMOTE=<otra ruta>, PASSWORD/PASSWORD2 nuevas, ya guardadas en la custodia A)
scripts/backup/rotate-crypt.sh --from secure:erp --to secure2:erp [--verify-only]
```

Garantías (con prueba en `test-key-rotation.sh`; ver el recuento de la última corrida en §5):

- **Nunca destruye ni modifica el origen.** El script solo admite los subcomandos `lsf`, `copy` y `check` de rclone (lista blanca) y la copia usa `--immutable`
  (no pisa nada del destino). Tras rotar, el árbol cifrado del servidor viejo es idéntico byte a byte. Retirar el remoto viejo es una decisión humana
  posterior (`backup-custodia.md` §8, pasos 6-7).
- **Origen con password incorrecta**: aborta antes de copiar (exit 6) y el destino no recibe nada.
- **Password nueva incorrecta**: si el destino ya contiene objetos que no se descifran con las passwords nuevas, **se niega a copiar** (evita escribir un
  segundo juego de objetos indescifrable junto al bueno); `fetch-offsite.sh` falla con la causa y no escribe nada en el `BACKUP_DIR` local.
- **Interrupción** (se mata el contenedor rclone a mitad de la copia, con la copia limitada a 4 MB/s): termina con error, el origen queda intacto, el
  destino queda parcial, `--verify-only` **no** da por buena la copia parcial, el lock se libera y volver a ejecutar **reanuda y completa**.
- **Idempotente**: repetir la rotación ya completada no cambia nada.
- **Recuperación de respaldos viejos y nuevos** con la clave nueva (`BACKUP_REMOTE=secure2:erp`), incluido un respaldo posterior a la rotación; el respaldo
  posterior no es legible con la clave vieja.
- **Rollback**: volver a `BACKUP_REMOTE=secure:erp` sigue recuperando todo, idéntico.
- Rechaza `--from` igual a `--to`, destinos que no son `crypt` (viajarían sin cifrar) y rutas con `..`. Ningún password ni valor `obscure` aparece en las
  salidas ni en `rotate-crypt.log` (12 valores buscados). Toma el lock de backups (no corre a la vez que `backup.sh`/`restore-*.sh`).

- **Passwords "nuevas" iguales a las viejas**: se detecta (los nombres cifrados crudos coinciden en ambos remotos) y falla con exit 6 (`secure3` en el ensayo).
- **Lock**: con un backup/restore en curso la rotación se niega (exit 4); el caso está probado.

Qué **no** cubre / precauciones operativas (hallazgos de la revisión independiente):

- **Ventana de mantenimiento.** La rotación toma el lock de backups durante toda la copia y la verificación `--download`. En un remoto real de varios GB puede
  durar horas, y mientras tanto `backup.sh` (log cada 15 min, full nocturno) sale con código 4 (ocupado) **sin dejar estado de monitoreo**: el RPO se degrada sin
  alerta inmediata. Programarla en una ventana acordada, o pausar el cron y avisar, y correr `backup.sh all` al terminar.
- **Hueco entre el fin de la rotación y el cambio de `BACKUP_REMOTE`.** Hasta editar `.env`, `backup.sh` sigue escribiendo en el remoto viejo. Justo **antes** de
  cambiar `BACKUP_REMOTE` repetir `rotate-crypt.sh` (es idempotente: copia lo nuevo) y hacer `backup.sh all` inmediatamente después. El rollback tardío tampoco es
  completo: lo escrito solo en el remoto nuevo no existe en el viejo (volver a copiar en sentido inverso exige otra rotación manual).
- La retención remota (`backup.sh`) solo actúa sobre `BACKUP_REMOTE`; mientras el remoto viejo no sea `BACKUP_REMOTE` nada lo poda, y su retiro sigue siendo manual.
- `--immutable` falla si un archivo ya existe en el destino con distinto tamaño/fecha; en proveedores sin `mtime` fiable una reanudación podría requerir limpiar
  el archivo parcial a mano. Solo se probó sobre SFTP. La cuota/costo y la velocidad del proveedor real no se midieron.
- Los objetos del origen que no se descifran con las passwords del origen (p. ej. restos de una rotación anterior) no se detectan: se ignoran. Revisar el remoto viejo
  antes de retirarlo.
- `check-custody.sh` (C06/C07) vigila las passwords del remoto activo; durante una rotación no inspecciona las `SECURE2_*` ni `rotate-crypt.log`. `rotate-crypt.sh` nunca
  imprime passwords (probado: 12 valores buscados en las salidas).
- Un `SIGKILL` del script (no `SIGINT`/`SIGTERM`) podría dejar el contenedor rclone vivo y el lock liberado (no confirmado: no se ensayó). El ensayo interrumpe el contenedor
  rclone, no el script.
- `rotate-crypt.sh` lee siempre `./backup-offsite.env`, el mismo archivo que monta `docker-compose.yml` (no admite `BURY_OFFSITE_ENV_FILE`).

## 3. Recuperación integral ante pérdida total (`test-dr-integral.sh`)

Stack **real** (`db`, `db-init`, `migrate`, `app`, `caddy` con `tls internal`, imagen construida desde el `Dockerfile`), scripts reales de
`scripts/backup/*.sh` y un SFTP descartable con crypt. Sin puertos publicados en el host (el arnés lo verifica con `compose config` y aborta si no).

Secuencia: servidor original operativo (CA pública extraída, login con aceptación de términos, datos sintéticos, archivo en `uploads`) → `backup.sh all` →
fila posterior + `backup.sh log` → **pérdida total** (stack, volúmenes y `BACKUP_DIR` eliminados; el servidor original queda indisponible) →
recuperación **solo desde el SFTP cifrado** siguiendo `backup-restore.md` §10 (`prepare-dir` → `fetch-offsite` → `restore-files` → `db` → `restore-sql --with-logs`
→ `up --wait`).

Evidencia (WSL2 + Docker, base sintética del ERP real: 137 migraciones; tiempos **no** extrapolables a la base real):

| Paso | Tiempo |
|---|---|
| `fetch-offsite` (descarga + verificación byte a byte + sha256) | 8 s |
| `restore-files` / arranque de `db` / `restore-sql` (full + log) | 2 s / 6 s / 3 s |
| `up --wait` (db-init + migrate + app + caddy `healthy`) | 12 s |
| **RTO técnico total hasta `/health/ready` = 200 por TLS** | **32 s** (otra corrida: 44 s; la variación es el arranque de SQL Server) |

Verificado tras recuperar: la app inicia y responde por TLS **confiando solo en la CA original** que ya tenían los clientes; la **cookie de sesión emitida en el
servidor original sigue válida** (claves de Data Protection restauradas) y consulta el dato; login nuevo; la fila posterior al full llega por el log (RPO de
minutos) y se ve desde la app; conteos y `CHECKSUM_AGG` de la base **idénticos** al origen; árbol de archivos (`keys`, `uploads`, `appdata`) **idéntico**;
`restore-test.sh` con `DBCC CHECKDB` OK; el usuario de la app sigue sin DDL (`db-init` re-vinculó los usuarios); ningún secreto en las salidas.

Controles negativos (todos pasan): sin la password crypt `fetch-offsite` falla (exit 6) y no descarga nada útil; sin las claves de Data Protection la cookie
vieja es rechazada (302 al login).

Fallos hallados al construir el ensayo (del arnés, no del producto): el login pide un paso de aceptación de términos en el primer ingreso; `Marcas` tiene
índices filtrados, por lo que `sqlcmd` necesita `-I` (`QUOTED_IDENTIFIER`) para insertar datos de prueba.

## 4. La CA de Caddy: custodia, recuperación y rotación

### 4.1 Qué contiene `caddy-data` (verificado con el Caddy fijado del `docker-compose.yml`)

`caddy/pki/authorities/local/{root.crt, root.key, intermediate.crt, intermediate.key}`, `caddy/certificates/local/<dominio>/{.crt, .key, .json}`,
`instance.uuid`, `last_clean.json`. Todos los archivos son `0600 root`. `root.key` **sí está incluida** en el tar de archivos (`BACKUP_INCLUDE_CADDY=true`, por defecto).

### 4.2 Hallazgo C-01 (riesgo confirmado, severidad media)

La clave privada de la CA raíz comparte ubicación y custodia con los respaldos: viaja dentro de `files_*.tar.gz`, que queda **sin cifrar** en `BACKUP_DIR`
(0600, solo dueño) y cifrada por crypt en el remoto (en el servidor SFTP no hay ningún nombre legible: probado). Quien obtenga el tar descifrado (disco del
servidor, o remoto + passwords P1/P2) puede emitir certificados que **todos los clientes que instalaron la CA ya confían** (CA raíz de ~10 años, sin
restricción de nombres; vida y alcance según el diseño de Caddy, no medidos en el ensayo): MITM en la LAN/VPN, interceptando logins. Es el mismo riesgo que ya tiene `bury-keys` (claves de Data Protection), pero con alcance
sobre los equipos de los clientes, no solo sobre la app.

### 4.3 Por qué NO se corrigió excluyendo `root.key` del backup

Probado: si `root.crt` existe pero falta `root.key`, **Caddy no arranca** (`loading root key ... no such file or directory`; el ensayo `d6` lo comprueba). Un
filtro simple en `files-backup.sh` dejaría un restore con el ERP caído por TLS. Las opciones reales:

| Opción | Efecto | Costo |
|---|---|---|
| **A. Mantener `caddy-data` en el backup (hoy)** | la CA se recupera idéntica; los clientes no hacen nada | `root.key` comparte custodia con los respaldos |
| **B. `BACKUP_INCLUDE_CADDY=false`** | `root.key` nunca viaja; tras un desastre Caddy genera una CA nueva | hay que **redistribuir la CA nueva a todos los clientes** (§4.5); la app y los datos no dependen de la CA (probado) |
| **C. Excluir solo `root.key` + custodia separada** | requiere un procedimiento de reposición de la clave antes de arrancar Caddy | no implementado: mayor complejidad y riesgo de outage |

**Recomendación**: mantener A **mientras** (1) el remoto sea `crypt` con P1/P2 en custodia A/B fuera del servidor y (2) `BACKUP_DIR` sea 700 y no se copie en
claro a otros medios; y elegir B si los clientes son pocos/gestionados (redistribuir una CA es barato) o si no se puede garantizar (2). Es decisión del
propietario. `check-custody.sh` ahora emite `WARN C12` mientras `BACKUP_INCLUDE_CADDY=true` para que el riesgo sea visible (no falla: A es aceptable).

### 4.4 Recuperación de la CA tras una pérdida total

Con A, `restore-files.sh` restaura `caddy-data` completo y **la CA es la misma** (mismo sha256 de `root.crt`): los clientes siguen confiando sin tocar nada
(probado con la CA original extraída antes del desastre). Con B, o si se pierde solo `caddy-data` (control negativo `d6`): Caddy genera una CA **nueva**,
los clientes que confían en la vieja rechazan el certificado y hay que distribuir la nueva (§4.5); mientras tanto la app y los datos siguen intactos.

### 4.5 Rotación de la CA (sospecha de exposición de `root.key`, o cambio de política)

Probada en `test-dr-integral.sh` (`d7`). **No mostrar, copiar a logs ni subir ninguna clave.**

1. **Contener**: decidir si hay evidencia de exposición de `root.key`; anotar fecha y alcance. Si se expuso, todo certificado emitido por esa CA es sospechoso.
2. `docker compose stop caddy`.
3. **Retirar** la CA vieja. Con **exposición** de `root.key`: copiar `caddy/pki` y `caddy/certificates` a una custodia separada (medio cifrado fuera del servidor y del
   `BACKUP_DIR`) o destruirlos con aprobación del propietario, y **no dejarlos en el volumen** (cualquier `backup.sh files` posterior los empaquetaría). Sin exposición
   (rotación por política): moverlos dentro del volumen a un directorio **con fecha** (`caddy/retiro/<AAAAMMDDTHHMMSSZ>/pki` y `.../certificates`: nombres únicos para que una
   segunda rotación no anide ni choque) y sacarlos del volumen al terminar la transición. Durante la rotación pausar el cron de backups o esperar a que no haya uno en curso.
4. `docker compose up -d --wait caddy`: Caddy genera CA y certificado nuevos. Extraer **solo** el certificado público:
   `docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root-ca.crt` y verificar que no contiene `PRIVATE KEY`.
5. Distribuir la CA nueva a los clientes (`docs/red-privada-lan-vpn.md` §2) y **quitar la vieja** de sus almacenes de confianza. Probado: con la CA nueva
   instalada `/health/ready` = 200; con la vieja ya no confían; la sesión y los datos siguen intactos.
6. `backup.sh files` para que el respaldo tenga la CA nueva. **Trampas probadas/analizadas**: (a) el tar incluye `retiro/<fecha>/pki` mientras esté en el volumen (sacarlo
   antes del siguiente backup si la rotación fue por exposición); (b) **los tars anteriores a la rotación conservan la `root.key` vieja**: 14 días en `BACKUP_DIR` local (sin
   cifrar) y 30 o más en el remoto. Si hubo exposición hay que purgar esos tars locales y evaluar el remoto como comprometido (rotar P1/P2, `backup-custodia.md` §9); la
   retención remota no se acelera sola; (c) **restaurar un tar anterior a la rotación reinstala la CA vieja**: los clientes que ya la quitaron de su almacén fallarán. Tras
   cualquier restore de `files` posterior a una rotación, verificar la huella de `root.crt` contra la CA vigente (el ensayo no restaura después de rotar: limitación).
7. Registrar fecha, motivo y qué clientes ya confían en la nueva.

## 5. Cómo ejecutar los ensayos

```bash
bash scripts/backup/test-key-rotation.sh                      # ~4 min, sin SQL Server (última corrida: 47 PASS, 0 FAIL)
ERP_IMAGE=<imagen> bash scripts/backup/test-dr-integral.sh    # ~6 min (sin ERP_IMAGE construye el Dockerfile) (última corrida: 53 PASS, 0 FAIL)
bash scripts/backup/test-check-custody.sh                     # sin docker
```

Requieren Linux (o WSL2) con Docker Compose ≥ 2.24 (`!reset`). Son auto-limpiantes (proyecto `bury-p06rot-*` / `bury-p06dr-*`), no usan el `.env` real
(`COMPOSE_ENV_FILES` apunta a un archivo vacío) y abortan si ya existe un `backup-offsite.env` que no sea suyo. En CI: `.github/workflows/backup-dr-rotacion.yml`.

## 6. Pendientes reales de P-06 (no resueltos por estos ensayos)

1. **Proveedor externo real** y su cuenta (o el disco/NAS definitivo) y primera corrida de `backup.sh all` contra él; medir RTO/RPO y cuota con la base real.
2. **Custodia A y B reales** (`backup-custodia.md` §3 y §11): guardar P1/P2 fuera del servidor, mover `recovery-passphrase`/`.enc` que hoy coexisten, y ejecutar
   `check-custody.sh` en el servidor real.
3. **Primera rotación real** de P1/P2 con `rotate-crypt.sh` contra el remoto definitivo, esperar ≥ 1 ciclo de retención y recién entonces retirar el viejo
   (aprobación del propietario).
4. **Decidir la política de la CA** (A o B, §4.3) y, si es B, el procedimiento de redistribución a los clientes reales.
5. **Ejecución productiva programada**: `install-schedule.sh`/tarea de Windows en el host real, restore mensual `--offsite`, alertas del monitor llegando a alguien.
6. **Ensayo humano de recuperación** (con solo custodia A + B, sin acceso al servidor) y servidor físico definitivo (P-04).
7. Rotación de la credencial de transporte (SFTP/S3/B2) y de `root.key` por calendario, no solo ante incidentes.

# Custodia de la clave privada de la CA de Caddy (`root.key`) — P-06

> Estado: **mecanismo implementado y ensayado; NO activado en ningún servidor**. Activarlo exige una decisión del propietario sobre el modelo de custodia (§6).
> No se generó, rotó, movió ni borró ninguna credencial ni certificado real; todo el ensayo es sintético y descartable (`scripts/backup/test-ca-key-custody.sh`).

## 1. El riesgo

La CA interna de Caddy (`tls internal`) firma el certificado del ERP. Todos los clientes que instalaron su `root.crt` confían en ella. Quien tenga `root.key`
(o `intermediate.key`) puede emitir certificados que esos clientes aceptan: MITM en la LAN/VPN, con captura de logins. Es el hallazgo **C-01** de
`docs/backup-dr-rotacion-claves.md` §4.2.

Con `BACKUP_INCLUDE_CADDY=true` (por defecto) la clave viajaba **dentro de `files_*.tar.gz`**, sin cifrar a nivel local, junto con los datos que el propio tar
protege peor aún (claves de Data Protection). Excluirla sin más no sirve: **si falta `root.key`, Caddy no arranca aunque exista `root.crt`** (medido en
`test-dr-integral.sh` d6 y de nuevo en `test-ca-key-custody.sh` e) y el ERP queda caído por TLS.

## 2. Recorrido de la clave (auditoría)

| Etapa | Modo `include` (histórico) | Modo `seal` (nuevo) |
|---|---|---|
| Volumen vivo `caddy-data` | `0600 root` | igual (sin cambios) |
| Lectura para el backup | contenedor `backup-files`, volumen en solo lectura | tar **sin** `*.key` de la CA; la clave la lee `ca-seal` (solo lectura, sin red) |
| Archivo temporal | `files_<ts>.tar.gz.partial` con la clave en claro en `BACKUP_DIR` (`umask 077`, dir `700`) | el `.partial` no contiene la clave; el tar de claves existe solo como tubería en memoria; `/tmp` del contenedor es `tmpfs` (nunca toca disco) |
| Reposo local (`BACKUP_DIR`) | **tar con la clave en claro** (`0600`, 14 días, mín. 3) | tar sin clave + `<tar>.caseal` cifrado y autenticado |
| Transferencia a la copia externa | dentro del tar, protegido solo por crypt P1/P2 (las mismas que protegen todo lo demás) | el `.caseal` viaja cifrado **dos veces** (sellado + crypt); el tar no tiene la clave |
| `fetch-offsite` en el servidor nuevo | vuelve a quedar el tar con la clave en claro en `BACKUP_DIR` | vuelve el `.caseal` (inútil sin la clave de sellado) |
| Restauración | `restore-files` extrae el tar | `restore-files` extrae el tar y luego `ca-unseal` repone la clave (`0600`, validada, todo-o-nada) |
| Eliminación | retención local/remota borra el tar | borra el tar **y su `.caseal`** (local y remoto; probado) |
| Procesos / argv | sin secretos en argv | sin secretos en argv; la clave de sellado entra por **entorno del contenedor** (`env_file`) |
| Logs | no se imprime contenido | no se imprime contenido; `sanitize_text` redacta salidas; probado que ni la clave ni las passwords aparecen en `backup.log` ni en las salidas |

**Exposición residual que NO se elimina** (ver §7): tars históricos en modo `include` que sigan en `BACKUP_DIR`/remoto; el entorno del contenedor `ca-seal`/`ca-unseal` es visible
para `root` del host (`docker inspect`) mientras corre; `rclone obscure` es ofuscación reversible, no cifrado: la protección de la clave de sellado es el archivo `0600` + su custodia.

### Hallazgo adicional corregido

`BACKUP_INCLUDE_CADDY=false` **no tenía efecto**: `docker-compose.yml` no pasa el entorno del host a `backup-files`, así que `files-backup.sh` veía el valor por defecto `true`.
`backup.sh` ahora pasa `BACKUP_INCLUDE_CADDY` y `BACKUP_CA_KEY` de forma explícita (`-e`). Probado en `test-ca-key-custody.sh` (a2).

## 3. Diseño

```
BACKUP_CA_KEY=include   (default: comportamiento histórico, check-custody avisa C12)
BACKUP_CA_KEY=seal      la clave se SEPARA del tar y se sella aparte
BACKUP_CA_SEAL_ENV=/etc/bury/ca-seal.env   clave de sellado, 0600, FUERA de BACKUP_DIR y del offsite
```

* `files-backup.sh` excluye `caddy-data/caddy/pki/authorities/*/*.key` y, **fail-closed**, relee el tar y lo descarta si queda alguna `*.key` de una CA.
  Las claves de las **hojas** (`caddy/certificates/...`) se conservan: Caddy las regenera y no firman a nadie más.
* `ca-seal.sh` (contenedor `ca-seal`, imagen rclone ya fijada por digest, sin red) arma un tar con las `*.key` y lo escribe con **rclone crypt** (NaCl secretbox:
  cifrado autenticado, dos secretos independientes) en `files/<tar>.caseal`. Relee y compara el sha256 del flujo descifrado con el origen antes de renombrar.
* `backup.sh`: con `seal` exige la clave de sellado **antes** de tocar nada; si el sellado falla, **retira el tar** (no deja un respaldo que no cumple la política elegida) y
  sale con 5. Nunca degrada a `include` en silencio. El `.caseal` se sube a la copia externa con su tar, se verifica y lo borra la retención junto con él.
* `restore-files.sh` repone la clave con `ca-unseal.sh` si hay `.caseal`; con `--skip-ca-key` no. Si no hay `.caseal` y quedó `root.crt` sin `root.key`, avisa con las dos salidas posibles.
* `restore-ca-key.sh --archive <tar> [--dry-run] [--force]`: repone solo la clave. `--dry-run` descifra y valida **sin escribir** (prueba periódica de que la custodia sirve; es
  la forma de ensayar la recuperación sin tocar Caddy).
* `ca-unseal.sh` valida el contenido descifrado: solo archivos regulares `caddy/pki/authorities/<ca>/<nombre>.key`, sin `..`, enlaces ni otras rutas; no pisa una clave **distinta** sin `--force`.
  Rechaza (rc 5) cualquier enlace simbólico en el destino, en su temporal o en los directorios padre (un tar de archivos manipulado, que solo tiene un `.sha256` hermano como control,
  podría plantar `root.key.tmp.key -> otro` para desviar la clave descifrada fuera del área excluida del backup), crea los temporales de forma exclusiva y es **todo o nada**: cualquier error
  de escritura aborta con rc 5 y deja los temporales limpios (jamás informa `ok` tras un fallo parcial). Los temporales terminan en `.key` para que el tar siguiente los excluya y el control fail-closed los detecte.
* `ca-seal.sh` es fail-closed también hacia el origen: una CA con certificados pero **sin** sus claves (volumen dañado, restore incompleto) o un error al enumerar **no** es «nada que sellar»: rc 3, `backup.sh`
  descarta el tar y sale con 5 (así un respaldo sin clave ni `.caseal` no desplaza por retención a los que sí permiten recuperar la CA). Solo «Caddy aún no creó su CA» es `ca_seal=none`.
* `check-custody.sh` **C12** (aviso en `include`) y **C13** (FAIL en `seal` si la clave de sellado falta, está incompleta, no es `0600`, está dentro de `BACKUP_DIR`/offsite o comparte secretos con `backup-offsite.env`).

No se tocó C-02, `TheBuryProjectDb`, copias históricas ni el SFTP definitivo.

## 4. Activación (cuando el propietario elija el modelo, §6)

```bash
# 1. Generar la clave de sellado FUERA de BACKUP_DIR (no sobrescribe; no imprime los secretos)
scripts/backup/ca-seal-init.sh --out /etc/bury/ca-seal.env
# 2. COPIARLA YA a la custodia independiente elegida (§6). Sin ella los .caseal no se abren.
# 3. En .env:
#      BACKUP_CA_KEY=seal
#      BACKUP_CA_SEAL_ENV=/etc/bury/ca-seal.env
# 4. Verificar y ensayar
scripts/backup/check-custody.sh                      # C13 debe estar OK
scripts/backup/backup.sh files                       # genera tar sin clave + .caseal
scripts/backup/restore-ca-key.sh --archive files_<ts>.tar.gz --dry-run
# 5. Migración de lo existente (decisión del propietario, NO automática): los tars previos (modo include) siguen conteniendo la clave hasta que la retención los borre
#    (14 días local, BACKUP_REMOTE_RETENTION_DAYS remoto). Si hay sospecha de exposición, rotar la CA (docs/backup-dr-rotacion-claves.md §4.5).
```

## 5. Recuperación

| Situación | Resultado (probado) |
|---|---|
| Pérdida total + custodia correcta | `fetch-offsite` → `restore-files --force` → misma CA, mismas claves (sha256 idénticos), Caddy sirve y los clientes **siguen confiando** sin tocar nada |
| Se restaura **sin** `.caseal` | los datos se restauran pero `restore-files.sh` **termina con rc 9** (la CA quedó incompleta; con `--skip-ca-key` es explícito y termina 0); Caddy **no** sirve (000). Salidas: traer el `.caseal` del remoto y `restore-ca-key.sh`, o rotar la CA (§4.5 del doc DR: borrar `caddy/pki`, Caddy genera una nueva, redistribuir `root.crt`) |
| Backup en modo `seal` con CA incompleta (certificados sin claves) | rc 5, el tar se descarta |
| Enlace simbólico plantado en el destino (temporal o directorio padre) | rc 9 (`ca-unseal` rc 5): se rechaza, no se escribe nada fuera del destino autorizado |
| Clave de sellado **incorrecta** | rc 9, mensaje "clave de sellado incorrecta o archivo corrupto", **no se escribe nada**, la salida no contiene secretos |
| `.caseal` **alterado** (1 byte) | rechazado (cifrado autenticado), no se escribe nada |
| `.caseal` **malicioso** (enlace, ruta ajena, extensión no `.key`) sellado con la clave real | rechazado por la validación de contenido, volumen intacto |
| Ya hay una clave distinta | aborta sin `--force`; con la misma clave es idempotente |
| Caddy corriendo | solo `--dry-run` |
| Se pierde la clave de sellado | los `.caseal` son irrecuperables: **solo queda rotar la CA** (los datos y el ERP no dependen de ella) |

## 6. Modelos de custodia — decisión del propietario (no tomada aquí)

El mecanismo necesita un lugar para la **copia de la clave de sellado** (2 líneas, ~100 caracteres) que sea independiente del servidor, de `BACKUP_DIR` y del SFTP.

| Modelo | Cómo | Riesgo / costo |
|---|---|---|
| **A. Gestor de secretos / caja de contraseñas del propietario** | guardar el contenido de `ca-seal.env` como nota segura | depende de quién acceda al gestor; recuperarlo exige disponibilidad del gestor el día del desastre |
| **B. Medio cifrado offline (USB/papel) + custodio distinto del administrador del servidor** | copia impresa/en medio, guardada fuera del sitio | disponibilidad manual; hay que ensayar la lectura; dos custodios = más robusto |
| **C. División de secretos (2 de 3)** | repartir el archivo con un esquema de umbral | mejor contra un solo custodio comprometido; requiere herramienta y práctica; **no implementado** |
| **D. No guardar la clave: `BACKUP_INCLUDE_CADDY=false` y rotar la CA tras un desastre** | `root.key` nunca viaja ni se sella | hay que redistribuir la CA nueva a todos los clientes tras cada desastre; sin dependencia de custodia |
| **E. Dejar `include`** | sin cambios | el riesgo C-01 sigue abierto; solo aceptable si BACKUP_DIR y P1/P2 están muy bien protegidos |

Recomendación técnica: **A o B + `seal`**, con `restore-ca-key.sh --dry-run` mensual como prueba de que la custodia sirve; D si los clientes son pocos y gestionados. La decisión, el
custodio y la migración de los respaldos existentes quedan **reservados al propietario**.

## 7. Límites

* No protege contra un atacante con `root` en el servidor **en el momento del backup** (lee la clave del volumen vivo, como Caddy).
* Los tars previos en modo `include` conservan la clave hasta que se borren; borrar no es borrado seguro (journal, snapshots, copias del SFTP con versionado).
* La clave de sellado en el servidor (para poder sellar en cada backup) es tan segura como el servidor; lo que **se separa** es la clave respecto de los **respaldos**: quien robe `BACKUP_DIR`
  o el SFTP (aun con P1/P2) no obtiene la clave de la CA sin la clave de sellado. Quien comprometa el servidor ya tiene el volumen vivo.
* Las claves de las hojas siguen en el tar (acotadas al certificado del ERP; se regeneran).
* La disponibilidad de la copia de custodia fuera del servidor no se puede comprobar desde aquí (C13 lo dice explícitamente).
* Ensayado con Caddy, SQL Server y rclone reales en Docker (WSL2/Ubuntu); copia externa = disco local con crypt, no SFTP real (P-06 sigue abierto por eso).

## 8. Pruebas

* `scripts/backup/test-ca-key-custody.sh` (~5 min, SQL Server real + Caddy real; CI: job `custodia-clave-ca` de `backup-dr-rotacion.yml`): última corrida 2026-11-14: **108 PASS, 0 FAIL** (stack aislado, sin residuos).
* `scripts/backup/test-check-custody.sh` (55) y `scripts/backup/test-hardening.sh` (77) siguen en verde.
* Revisión adversarial independiente (Codex, solo lectura, worktree idéntico antes y después; 2 rondas): 6 hallazgos (4 altos, 1 medio), todos verificados contra el código y corregidos con prueba (enlace simbólico en el
  temporal, errores de escritura que terminaban en `ok`, restore con rc 0 sin clave, seal que aceptaba una CA sin claves, C13 comparando solo contra un remoto llamado `secure`; ronda 2: el control de restore miraba solo `authorities/local`, ahora recorre todas las autoridades).
  No cubierto por pruebas: fallo real de `chmod`/disco lleno durante la reposición (revisado por lectura; el código aborta con rc 5 y limpia temporales).

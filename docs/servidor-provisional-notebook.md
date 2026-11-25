# Servidor provisional (notebook Ubuntu) — instancia operativa, custodia, respaldo independiente y traslado (2026-11-25)

Decisión del propietario: hasta tener el servidor físico definitivo, la notebook Ubuntu `192.168.50.223` hace de **servidor provisional** que simula las condiciones de producción. La VM `tbp` queda descartada. **No hay datos comerciales reales** y esta instancia **no inicia la operación comercial ni la ventana CSP productiva** (`CSP_MODE=Off`).
Código instalado: tag `cierre-tecnico-software-20261125` (= `main` 41775ca5, CI verde). Matriz de requisitos: [`matriz-lista-b-20261125.md`](matriz-lista-b-20261125.md).

## 1. Dos instalaciones independientes en la misma notebook

| | Laboratorio (`theburyproject-notebook`) | **Provisional** (`theburyproject-prov`) |
|---|---|---|
| Clon | `~/theburyproject1` | `~/theburyproject-prov` (tag de cierre) |
| Dominio / puerto | `tbp-nb` : 443 | `tbp-prov` : **8443** |
| Volúmenes, red, subred | `theburyproject-notebook_*`, `172.29.88.0/24` | `theburyproject-prov_*`, `172.29.89.0/24` |
| Firewall (DOCKER-USER) | cadena `BURY-ERP` (`bury-firewall.service`) | cadena `BURY-ERP-PROV` (`bury-firewall-prov.service`, `/etc/bury/firewall-prov.conf`) |
| Respaldos | `/srv/bury-backups`, copia cifrada `bury-notebook.luks` | `/srv/bury-backups-prov`, copia cifrada **propia** `bury-prov.luks` (3 GB, tope 2 GB) → `/mnt/bury-prov-respaldo` |
| Cron | `/etc/cron.d/bury-backup-notebook` (02:30) | `/etc/cron.d/bury-backup-prov` (full 03:00, log 7/22/37/52, restore-test dom 05:00, custodia dom 06:00) y `bury-backup-localcopy-bury-prov` (03:45) |
| Cuenta inicial | `lab-admin` | `prov-admin` (SuperAdmin sintético, correo `.invalid`; contraseña solo en `.env` 0600) |
| Fiscal V2 | `2026-11-01T03:00:00Z` (laboratorio) | **sin definir** (criterio V1) hasta que el propietario fije el instante real (B9) |
| Crédito | cerrado | **cerrado** (`cerrar-credito-inicial.py`: cupos $0 por la pantalla real, decisión del 2026-11-22) |

Ninguna toca a la otra (guardia proyecto Compose ↔ `BACKUP_DIR`, #362). El laboratorio, sus respaldos y su pendrive no se modificaron (solo se **agregaron** archivos de custodia en `recovery/`).

## 2. Instalación reproducible (lo que se ejecutó, con scripts oficiales)

```bash
# Servidor nuevo (Ubuntu): una vez
sudo scripts/install/server-linux-bootstrap.sh
git clone https://github.com/alanminana/theburyproject1 ~/theburyproject-prov && cd ~/theburyproject-prov && git checkout cierre-tecnico-software-20261125
sudo install -d -m 700 -o $USER -g $USER /srv/bury-backups-prov

scripts/install/install.sh --env-only --yes --project theburyproject-prov --domain tbp-prov \
    --admin-email admin-prov@notebook.invalid --admin-user prov-admin --backup-dir /srv/bury-backups-prov
#   editar .env: ERP_HTTPS_PORT=8443, BURY_NET_SUBNET=172.29.89.0/24 (solo si comparte host con otra instalacion)
sudo scripts/install/bury-firewall.sh ...        # ver «Firewall» abajo
scripts/install/install.sh --yes --project theburyproject-prov --domain tbp-prov --admin-email ... --admin-user prov-admin --backup-dir /srv/bury-backups-prov
python3 scripts/install/cerrar-credito-inicial.py --base https://tbp-prov:8443 --ca caddy-root-ca.crt --resolve 127.0.0.1 --user prov-admin --env-file .env
sudo scripts/backup/install-schedule.sh --install --project theburyproject-prov --user $USER --full-at 03:00 --log-offset 7
sudo scripts/backup/setup-local-disk-copy.sh --apply --host-mount /mnt/bury-pendrive --mount /mnt/bury-prov-respaldo --size-gb 3 \
    --name bury-prov --owner $USER --repo $PWD --project theburyproject-prov --verify-at 03:45
#   editar .env: BACKUP_REMOVABLE_DIR=/mnt/bury-prov-respaldo, BACKUP_REMOVABLE_MAPPER=bury-prov, BACKUP_REMOVABLE_MAX_TOTAL_GB=2, BACKUP_MIN_FREE_GB=20
COMPOSE_PROJECT_NAME=theburyproject-prov scripts/backup/backup.sh all
sudo bash scripts/monitoring/instalar-agente.sh /opt/theburyproject/scripts/monitoring   # + config.json/monitor.env/unidad (docs/monitoreo-alertas.md)
scripts/install/verificar-servidor.sh --project theburyproject-prov --lan-cidr 192.168.50.0/24 --fw-chain BURY-ERP-PROV --sudo-cmd "sudo -n"
```

**Firewall de una segunda instalación** (la primera usa `BURY-ERP` y 443): `/etc/bury/firewall-prov.conf` con `ALLOW_FROM="192.168.50.0/24"` y `HTTPS_PORT=8443`, y una unidad `bury-firewall-prov.service` copia de `bury-firewall.service` con `Environment=BURY_FW_CHAIN=BURY-ERP-PROV` y `Environment=BURY_FW_CONF=/etc/bury/firewall-prov.conf`. Documentado en la cabecera de `scripts/install/bury-firewall.sh`.

`verificar-servidor.sh` es de **solo lectura** y es el mismo control que se repite en el servidor definitivo (con `--cierre` exige además `p07-verificar-cuentas.sql` con `@Cierre = 1`: ≥ 2 SuperAdmin reales y 0 cuentas sintéticas; en la provisional falla **solo** ese control, por diseño).

## 3. Evidencia de esta instalación (2026-11-25, medida en la notebook)

| Comprobación | Resultado |
|---|---|
| `verificar-servidor.sh` | **21 PASS, 0 FAIL, 2 WARN** (los 2 WARN son pendientes del propietario: Fiscal V2 sin instante real —B9— y sin copia externa independiente —B4—); la primera corrida, antes de programar respaldos y monitor, dio 18/0/6 |
| Imagen / migraciones | `theburyproject/erp:20261125-41775ca5`, 145 migraciones, `p02-verificar-instalacion.sql` y `p07-verificar-credito-cerrado.sql` exit 0 |
| HTTPS | desde otro equipo de la LAN: `curl --cacert` = 200 en `/health/ready` y login; TLS 1.3; **sin la CA el certificado se rechaza** |
| Puertos medidos desde otro equipo | abiertos: 22 (solo el equipo administrador), 443 (laboratorio), 8443 (provisional); **cerrados: 80, 1433, 2019, 8080, 8444** |
| Respaldo | `backup.sh all` OK (41 s), copia al extraíble cifrado verificada (`verify-local-copy.sh` sha256 OK) |
| Restauración | `restore-test.sh` OK (145 migraciones, 141 tablas, CHECKDB OK); `restore-test-local-copy.sh` desde la copia cifrada OK (32 s) |
| Monitoreo | `bury-monitor.service` activo (95 señales, 0 en alerta); sin canal externo (B6) |
| Custodia | `check-custody.sh` sin FAIL (2 avisos conocidos: sin copia externa; `BACKUP_INCLUDE_CADDY`) |
| **Ensayo de traslado** | ver §6: reconstruido desde cero con solo el paquete de recuperación + el pendrive + el tag: compuerta superada, **CA idéntica**, usuarios/roles/cupos/migraciones con checksum idéntico; destruido al terminar |
| Consumo medido (instancia vacía) | `db` ≈ 850 MiB, `app` ≈ 210 MiB, `caddy` ≈ 13 MiB por instalación; disco: ≈ 33 GB usados (imágenes 7 GB, caché de build 8,7 GB) |

## 4. Custodia externa de claves (B5) — procedimiento

Principio (`docs/backup-custodia.md`): la clave **no** puede vivir junto a lo que descifra, y una clave que solo existe en el servidor muere con él. Hacen falta **dos custodias independientes** (otro dispositivo/lugar, otra cuenta):

| Custodia | Qué es | Dónde está hoy (a sacar de la notebook) |
|---|---|---|
| **A — passphrase** (corta) | descifra los paquetes B | `~/.bury-custodia-prov/passphrase` (provisional) y `~/.bury-custodia/passphrase` (laboratorio) |
| **B — paquetes cifrados** | `bury-recovery_*.gpg` (`.env`) y `bury-custodia-luks_*.gpg` (clave + encabezado LUKS de la copia local) | `/mnt/bury-pendrive/recovery-prov/` y `/mnt/bury-pendrive/recovery/` |

Generados y verificados hoy (descifrado autenticado; alteración detectada; ver `scripts/backup/test-exportar-custodia.sh` 24/24 y `test-recovery-package.sh` 39/39):
```bash
# paquete de configuracion (.env) y paquete de claves LUKS (como root); passphrase en archivo 0600 FUERA de la carpeta de salida
COMPOSE_PROJECT_NAME=theburyproject-prov COMPOSE_ENV_FILES=$PWD/.env scripts/backup/recovery-package.sh create --out /mnt/bury-pendrive/recovery-prov --passphrase-file ~/.bury-custodia-prov/passphrase
sudo scripts/backup/exportar-custodia.sh create --out /mnt/bury-pendrive/recovery-prov --name bury-prov --passphrase-file ~/.bury-custodia-prov/passphrase
#   (bajo sudo lee BACKUP_DIR/BACKUP_REMOVABLE_DIR del .env del repo; con otro .env: --env-file. Se NIEGA a guardar dentro de los respaldos)
scripts/backup/recovery-package.sh verify <paquete>.gpg --passphrase-file ...        # idem exportar-custodia.sh verify
```
**Qué debe hacer el propietario (no se puede hacer desde el servidor):**
1. Copiar los `.gpg` y `.gpg.sha256` a un dispositivo **fuera de la notebook** (p. ej. `scp snowsh@192.168.50.223:/mnt/bury-pendrive/recovery-prov/*.gpg .` desde su PC y de ahí a un USB guardado aparte o a otra cuenta).
2. Guardar las dos passphrases (A) en un gestor de contraseñas o impresas en un lugar **distinto** al de los paquetes.
3. Verificar **una vez** en otro equipo: `scripts/backup/recovery-package.sh verify <paquete>.gpg` (pide la passphrase) y `exportar-custodia.sh verify`.
4. Decidir la clave privada de la CA de Caddy: hoy `BACKUP_CA_KEY=include` (viaja en el tar de archivos, protegida por la copia LUKS); alternativa `seal` (`docs/backup-ca-key-custodia.md`). Sin ella, un servidor nuevo emite otra CA y hay que reinstalarla en los clientes.
5. Rotar la contraseña de `snowsh` (quedó escrita en el chat) y reservar la IP del equipo administrador (ufw solo admite SSH desde `192.168.50.164`).

## 5. Respaldo independiente del equipo (B4) — procedimiento y estado

Hoy hay **tres copias en el mismo equipo físico** (disco interno, copia local cifrada en el pendrive, paquetes de custodia): protegen contra borrado, corrupción y robo del pendrive, **no** contra la pérdida del equipo. Para B4 hay que elegir **un destino que no sea la notebook**; todas las opciones usan scripts ya existentes (`backup.sh` las envía cifradas con `rclone crypt`; `fetch-offsite.sh` y `recover.sh` las recuperan):

| Opción | Qué necesita | Cómo se activa |
|---|---|---|
| **SFTP a otro equipo de la casa/oficina** (PC, NAS) | un equipo con SSH siempre encendido y su usuario/clave | `docker-compose.offsite-sftp.yml`, `BACKUP_REMOTE`, `backup-offsite.env`; `docs/backup-restore.md` §6; el CI lo simula en `simulacro-sftp` |
| **Segundo disco USB rotado** guardado fuera del local | un disco y una rutina de rotación | `setup-local-disk-copy.sh --name bury-prov-2` sobre ese disco, o `BACKUP_REMOVABLE_DIR` |
| **Nube (S3/B2/Drive vía rclone)** | cuenta del proveedor | `backup-offsite.env` con el remoto `crypt` |

Tras elegir: `fetch-offsite.sh` + `restore-test.sh --offsite` (prueba real de restauración **desde la copia externa**), y `BACKUP_REQUIRE_OFFSITE=true`. Si no se mitiga, el riesgo debe **aceptarse por escrito** (B4).

## 6. Traslado al servidor definitivo — procedimiento (ensayado hoy)

Ensayo ejecutado el 2026-11-25 en la notebook (proyecto descartable `theburyproject-ens`, ya destruido), **sin usar nada de la instalación origen excepto lo que viajaría**: (1) el `.env` extraído del paquete de recuperación con la passphrase, (2) el código en el tag, (3) los respaldos de la copia cifrada del pendrive.

```bash
# En el servidor definitivo (Ubuntu limpio)
sudo scripts/install/server-linux-bootstrap.sh
git clone https://github.com/alanminana/theburyproject1 ~/theburyproject && cd ~/theburyproject && git checkout cierre-tecnico-software-20261125   # o el tag vigente
scripts/backup/recovery-package.sh extract bury-recovery_<ts>.gpg --to /dev/shm/env --passphrase-file <passphrase> && cp /dev/shm/env/.env .env && chmod 600 .env && rm -rf /dev/shm/env
#   ajustar en .env solo lo propio del host: ERP_BIND_ADDRESS, BACKUP_DIR, BACKUP_REMOVABLE_DIR, (ERP_HTTPS_PORT/BURY_NET_SUBNET si comparte host)
scripts/install/install.sh --no-deploy --yes --project theburyproject ...            # construye la imagen con tag inmutable
scripts/backup/prepare-dir.sh
#   abrir el disco cifrado (clave desde exportar-custodia.sh extract) y copiar respaldos al BACKUP_DIR:  sudo cp -a <disco>/bury-backup/{sql,files} $BACKUP_DIR/
ERP_IMAGE=<imagen construida> scripts/backup/recover.sh --plan     # muestra el par full+tar
ERP_IMAGE=<imagen construida> scripts/backup/recover.sh --apply    # estricto: se detiene ante cualquier fallo; no arranca app con base vacia
scripts/install/verificar-servidor.sh --project theburyproject --sudo-cmd "sudo -n" --cierre
```
**Resultado del ensayo:** compuerta superada (ONLINE, 145 migraciones, usuarios, CHECKDB OK), stack sano en ≈ 1 min (restore de archivos + SQL + compuerta + arranque); la **CA de Caddy es idéntica** (misma huella SHA-256: los clientes que ya confían siguen confiando — requiere `BACKUP_INCLUDE_CADDY=true`); el login funciona con la misma contraseña; usuarios, roles, cupos y migraciones tienen el mismo checksum que el origen.
**Qué cambia en el definitivo:** el dominio (`ERP_DOMAIN`) debe resolverse al servidor nuevo; si cambia el nombre, los clientes deben reinstalar la CA. Los clientes (Windows/Mac/móvil) instalan `caddy-root-ca.crt` (`scripts/install/Install-ErpCa.ps1`).

## 7. Operación diaria de la provisional

* Estado: `scripts/install/verificar-servidor.sh ...` (o `docker compose -p theburyproject-prov ps`).
* Reinicio: los 3 servicios `restart: unless-stopped`; Docker, `bury-firewall*` y `bury-monitor` arrancan con el sistema (verificado en reinicio completo y corte brusco del laboratorio, 2026-11-24).
* Acceso: `https://tbp-prov:8443` (resolver `tbp-prov` a `192.168.50.223` en el hosts del cliente + instalar `caddy-root-ca.crt`). Notebook solo Wi-Fi/DHCP: reservar la IP en el router.

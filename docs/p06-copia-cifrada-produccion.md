# P-06: copia local cifrada independiente de PRODUCCION (2026-11-23)

> Histórico: la VM `tbp` quedó fuera del plan el 2026-11-24; este documento conserva la evidencia tal como se emitió.

> **Respaldo LOCAL, no offsite.** Ambos discos virtuales de la VM `tbp` (`sda` del sistema y `sdb` de 40 GB) son archivos VMDK del **mismo anfitrion**
> (mismo PC, mismo volumen `C:`). **No protege contra la perdida total del equipo** (robo, incendio, falla del disco `C:`, ransomware, borrado de la VM).
> El propietario **acepto temporalmente ese riesgo; esta aceptado, NO mitigado.** `BACKUP_REMOTE` sigue vacio y las alertas de copia externa de preprod
> no se tocaron. Este documento no declara proteccion offsite.

Complementa [`p06-respaldo-disco-local-vm.md`](p06-respaldo-disco-local-vm.md) (la copia de **preproduccion**, que no se modifico) y
[`p06-cierre-tecnico-dos-instalaciones.md`](p06-cierre-tecnico-dos-instalaciones.md) (aislamiento preprod/prod).

## Diseno

Un segundo contenedor LUKS2 **independiente** en el mismo `sdb1` (ya montado en `/mnt/tbp-pruebas`). No se formateo, particiono ni borro nada: se **agrego un archivo**.

| | Preproduccion (existente) | Produccion (nuevo) |
|---|---|---|
| Contenedor | `/mnt/tbp-pruebas/tbp-respaldo.luks` (3 GB) | `/mnt/tbp-pruebas/tbp-respaldo-prod.luks` (5 GB, disperso: es el techo duro de crecimiento del VMDK) |
| Mapper / montaje | `tbp-respaldo` -> `/mnt/tbp-respaldo` | `tbp-respaldo-prod` -> `/mnt/tbp-respaldo-prod` |
| Clave | `/etc/bury-backup/local-disk.key` | `/etc/bury-backup/local-disk-tbp-respaldo-prod.key` (64 bytes aleatorios, root 0400, **distinta**) |
| Encabezado LUKS (copia) | `/etc/bury-backup/local-disk.luks-header` | `/etc/bury-backup/local-disk-tbp-respaldo-prod.luks-header` |
| Origen | `/srv/bury-backups` | `/srv/bury-backups-prod` |
| `.env` | `BACKUP_REMOVABLE_*` en `~/theburyproject1/.env` | `BACKUP_REMOVABLE_DIR=/mnt/tbp-respaldo-prod`, `_MAPPER=tbp-respaldo-prod`, `_MAX_TOTAL_GB=3.5`, `_MIN_FREE_GB=0.5`, `_MAX_AGE_HOURS=48` en `~/theburyproject-prod-src/.env` (backup previo `.deploy-state/env.pre-p06-copia.bak`) |
| Copia | `backup.sh` full/all de las 02:30 -> `copy-removable.sh` | `backup.sh` full/all de las 03:45 (`/etc/cron.d/bury-backup-prod`, sin cambios) -> `copy-removable.sh` |
| Verificador diario | `/etc/cron.d/bury-backup-localcopy` 03:15 | `/etc/cron.d/bury-backup-localcopy-tbp-respaldo-prod` **04:15**, con `COMPOSE_PROJECT_NAME=theburyproject-prod` y `COMPOSE_ENV_FILES` |
| Log de errores | `/var/log/bury-backup-errors.log` | `/var/log/bury-backup-errors-tbp-respaldo-prod.log` |

Cambio de codigo: `setup-local-disk-copy.sh` aprendio `--name` (identidad de la copia: archivo, mapper, etiqueta, clave, cron) y `--project`
(exporta el proyecto Compose en el cron, como `install-schedule.sh --project`). Con el nombre por defecto todo es identico a antes. Se niega a reusar una clave,
encabezado, cron o contenedor existentes. Comando aplicado (en la VM, como root):

```
setup-local-disk-copy.sh --apply --name tbp-respaldo-prod --project theburyproject-prod --owner snow \
  --repo /home/snow/theburyproject-prod-src --mount /mnt/tbp-respaldo-prod --size-gb 5 --verify-at 04:15
```

Controles (los de `copy-removable.sh`/`verify-local-copy.sh`, ya existentes): punto de montaje real + marca (la marca nombra el `BACKUP_DIR` dueno: una instalacion no escribe en
la copia de otra una vez reclamada), sha256 de cada archivo copiado, cadenas completas, poda por capacidad (tope 3,5 GB, libre minimo 0,5 GB, minimo 2 cadenas, nunca la ultima),
V1..V5 diarios. **No poda por dias**: la retencion es por capacidad. Punto de montaje sin montar = inmutable (`chattr +i`).

## Evidencia (VM, 2026-11-23 22:20-22:22 -03)

* **Preproduccion intacta**: antes/despues, hash del listado (tamano+ruta) de `/mnt/tbp-respaldo` (688 archivos), `local-disk.key`, `local-disk.luks-header`, `/etc/cron.d/bury-backup`
  y `removable-verify.json` **identicos**. Sus servicios y el monitor de preprod siguieron activos.
* **Copia real**: 3 cadenas de prod (`_182654Z`, `_183013Z`, `_183038Z`, 68 archivos, 38,6 MB) copiadas **a partir de backups ya existentes y con sha256**; **no se ejecuto ningun full adicional ni se detuvo la app**.
  `copy-removable --apply --strict` rc 0 "AL DIA". Verificador V1-V5 OK (33 archivos con sha256, cifrado `tbp-respaldo-prod`).
* **Fallo seguro**: con `umount` + `cryptsetup close` solo de la copia de prod: `touch`/`mkdir` -> *Permission denied*; `copy-removable --strict` rc 1 ("NO es un punto de montaje"); verificador rc 13 y
  `removable-verify.json` `verified:false`; nada escrito en `/`. Remontado por `systemd-cryptsetup@tbp\x2drespaldo\x2dprod` + `mount`: verificador rc 0.
* **Restauracion aislada** (`restore-test-local-copy.sh`, SQL Server descartable `bury-lcrt-*`, base `LocalCopyRestoreTest`): full `_183038Z` + 27 logs con sha256 verificado,
  RESTORE ONLINE, `DBCC CHECKDB` sin errores, 145 migraciones, 141 tablas, 23 s en total; **0 residuos**. No se toco ninguna base viva. La base de prod aun no tiene datos de negocio (0 filas en Clientes/Ventas): no extrapolar el tiempo.
* **Monitor de produccion** (`bury-monitor-prod`): ahora emite `backup.removable-copy` y `backup.removable-verify` (56 -> 58 senales, ambos INFO). Es el mismo `monitor.py`; solo cambia el origen
  (`/srv/bury-backups-prod/monitor-status`). La alerta CRITICAL inmediata por fallo del verificador esta cubierta por `test_monitor.py`; **no se provoco una alerta real en vivo** para no molestar al canal del propietario.
* Capacidad: contenedor 5 GB (uso real ~137 MB en el VMDK), 4,8 GB libres; `C:` tenia 14,2 GB libres. Con el techo de 5 GB el VMDK no puede crecer mas que eso por esta copia.

## Recuperacion (sin depender de la VM original)

Necesario: el archivo `tbp-respaldo-prod.luks` (o el VMDK `sdb`) y **la clave** `local-disk-tbp-respaldo-prod.key`. El encabezado (`.luks-header`) es la red de seguridad si el encabezado
del contenedor se danara. **Sin la clave los datos son irrecuperables.** *Hoy clave y encabezado solo existen en la VM (`/etc/bury-backup/`): copiarlos FUERA de la VM es una accion pendiente del propietario.*

```
# 1) abrir y montar (lectura-escritura: los scripts de restauracion exigen destino escribible; no escribir a mano en el)
sudo cryptsetup open --type luks2 --key-file local-disk-tbp-respaldo-prod.key tbp-respaldo-prod.luks recup-prod
sudo mkdir -p /mnt/recup-prod && sudo mount /dev/mapper/recup-prod /mnt/recup-prod
ls /mnt/recup-prod/bury-backup/sql    # TheBuryProjectDb_full_*.bak + _log_*.trn + .sha256 ; files/*.tar.gz
# 2) verificar integridad
cd /mnt/recup-prod/bury-backup && for f in sql/*.bak sql/*.trn files/*.tar.gz; do (cd "$(dirname "$f")" && sha256sum -c "$(basename "$f").sha256"); done
# 3) restaurar SIEMPRE primero en un SQL descartable: scripts/backup/restore-test-local-copy.sh (con la copia montada como BACKUP_REMOVABLE_DIR)
#    (en una maquina nueva: .env con BACKUP_REMOVABLE_DIR=/mnt/recup-prod y BACKUP_DIR=<el BACKUP_DIR dueno de la marca, /srv/bury-backups-prod>)
#    o restore-sql.sh / restore-files.sh segun docs/backup-restore.md. Nunca sobre una base activa sin ensayo previo.
# 4) si el contenedor no abre (encabezado danado): restaurar el encabezado guardado
sudo cryptsetup luksHeaderRestore tbp-respaldo-prod.luks --header-backup-file local-disk-tbp-respaldo-prod.luks-header
# 5) cerrar
sudo umount /mnt/recup-prod && sudo cryptsetup close recup-prod
```

Si el volumen no monta tras un reinicio: `sudo systemctl start 'systemd-cryptsetup@tbp\x2drespaldo\x2dprod' && sudo mount /mnt/tbp-respaldo-prod`. Entre tanto la copia queda "atrasada" y
el verificador alerta (`nofail`: la VM arranca igual). **El reinicio real de la VM sigue sin probarse** (estaba prohibido reiniciar).

## Pendientes y limites (no ocultar)

1. **Riesgo aceptado, no mitigado**: ambos discos dependen del mismo anfitrion; sin copia externa. P-06 solo puede cerrarse administrativamente **dentro del alcance de respaldos locales** autorizado.
2. **Custodia de claves**: `local-disk-tbp-respaldo-prod.key` y `.luks-header` (y los de preprod) deben copiarse fuera de la VM; **no verificado**. La clave esta en la misma VM: protege contra fuga del VMDK, no contra root.
3. El primer ciclo programado completo (full 03:45 del 24/11 -> copia -> verificador 04:15) **aun no ocurrio**: verificar `monitor-status/removable-copy.json` y `removable-verify.json` despues.
4. La marca de la copia de **preprod** esta vacia (se creo antes de la regla de dueno): se reclama sola en la proxima copia de preprod. No se toco para no alterar preprod.
5. Reinicio de la VM no probado; retencion por capacidad (no por dias); alerta en vivo no provocada.

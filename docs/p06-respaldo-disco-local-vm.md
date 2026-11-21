# P-06: copia LOCAL cifrada en el disco adicional de la VM (2026-11-21)

> **Esto es un respaldo LOCAL adicional, no una copia externa ni offsite.** El disco `sdb` es un VMDK dentro de la carpeta de la VM
> (`C:\Users\…\OneDrive\Documents\Virtual Machines\tbp\`), en el mismo volumen `C:` y el mismo PC que la VM. **No protege contra la pérdida
> completa del PC** (robo, incendio, falla de disco, ransomware, borrado de la VM). P-06 **no queda cerrado** como protección externa:
> `BACKUP_REMOTE` sigue vacío, `BACKUP_REQUIRE_OFFSITE=false` y las alertas de ausencia de copia externa **no se tocaron ni se silenciaron**.

Decisión del propietario: usar exclusivamente el disco virtual de 40 GB ya existente (`/mnt/tbp-pruebas`, UUID `75303265-fb12-443d-8b92-f73902d8e409`),
sin disco externo ni nube.

## Qué protege y qué no

| Riesgo | ¿Cubierto? |
|---|---|
| Corrupción/borrado accidental de `/srv/bury-backups` (disco del sistema `sda`) | **Sí**: segunda copia verificada (sha256) en otro disco virtual |
| Una cadena de backup dañada por bit-rot o truncado | **Sí**: verificación diaria sha256 de cada archivo; la copia conserva cadenas anteriores |
| Fuga del `.vmdk` por separado (OneDrive, copia del archivo) | **Sí**: el contenido está cifrado (LUKS2 AES-XTS 512). Los respaldos del disco `sda` **no** lo están (mismo estado de antes) |
| Quien ya es root en la VM | **No**: la clave está en la misma VM |
| Pérdida del PC / del volumen `C:` / de la VM completa | **No** |

## Diseño (sin tocar el disco, sin formatear)

* **No se formateó ni particionó nada.** Se creó **un archivo** nuevo, `/mnt/tbp-pruebas/tbp-respaldo.luks` (3 GB, disperso), con LUKS2
  (`aes-xts-plain64`, clave de 512 bits) y ext4 dentro, montado en `/mnt/tbp-respaldo`. El contenido previo del disco de pruebas
  (`backups-sinteticos`, `evidencia`, `logs`, `tmp`, `LEEME.txt`, `run-docker-tests.sh`) queda **intacto**.
* El tamaño fijo del contenedor es el **techo duro** de uso real (3 GB): el VMDK no puede crecer por esto más allá de eso.
* **Reutiliza el sistema existente**: `copy-removable.sh` (invocado por `backup.sh` tras cada `all`/`full`/`files`) con
  `BACKUP_REMOVABLE_DIR=/mnt/tbp-respaldo`. Los backups principales (`/srv/bury-backups`) y la programación de `/etc/cron.d/bury-backup`
  **no se modificaron**. La copia nunca hace fallar el backup principal.
* Controles de la copia (ya existentes): punto de montaje real + archivo-marca (`.bury-backup-removable`, dentro del volumen cifrado), sha256 de cada
  archivo copiado, cadenas completas de la más nueva a la más antigua, poda por capacidad (`BACKUP_REMOVABLE_MAX_TOTAL_GB=2`,
  `BACKUP_REMOVABLE_MIN_FREE_GB=0.3`, mínimo 2 cadenas, nunca la última).
* **Escrituras accidentales al disco raíz**: el punto de montaje vacío está **inmutable** (`chattr +i`); con el volumen no montado, `touch`/`mkdir` dan
  *Permission denied* y la copia se salta con aviso (comprobado, ver abajo). El archivo-marca vive dentro del volumen cifrado.
* **Retención**: la de capacidad (cadenas más nuevas hasta 2 GB, mínimo 2). La copia **no** poda por días y puede conservar más historia que
  `/srv/bury-backups` (14 días). Hoy: 7 cadenas, ~126 MB.

## Scripts nuevos (versionados, con pruebas)

| Script | Función |
|---|---|
| `scripts/backup/setup-local-disk-copy.sh --check|--apply` | Prepara el contenedor LUKS, clave, `crypttab`/`fstab` (`nofail`), marca y el cron de verificación. Se niega si el contenedor ya existe, si el volumen anfitrión no es un punto de montaje o si falta espacio. Idempotencia: **no** recrea nada |
| `scripts/backup/verify-local-copy.sh` | Diario 03:15 (`/etc/cron.d/bury-backup-localcopy`): V1 montaje+marca, V2 el montaje sale de `/dev/mapper/<mapper>`, V3 sha256 de **cada** archivo (y que cada dato tenga `.sha256`), V4 full más nuevo ≤ 48 h, V5 espacio. Publica `monitor-status/removable-verify.json` |
| `scripts/backup/restore-test-local-copy.sh` | Restauración de prueba **desde la copia** en un SQL Server descartable (proyecto Compose aleatorio), con full + logs, `DBCC CHECKDB`, migraciones; borra todo al terminar |
| `scripts/monitoring/monitor.py` (`removable_verify`) | Alerta **CRITICAL inmediata** si el verificador falla (montaje, cifrado inactivo, sha256, copia vieja, poco espacio); warning a 2 días / critical a 4 sin corridas. Más el colector `removable_copy` ya existente (copia atrasada: 3/7 días) |

Pruebas automáticas: `test-verify-local-copy.sh` (17 casos sintéticos: sano, 1 byte alterado, truncado, sin `.sha256`, full viejo, sin marca,
destino ausente, espacio, cifrado inactivo, copia vacía, recuperación) y `test_monitor.py` (caso `removable_verify`); ambas en CI vía
`test-hardening.sh` / `unittest`.

## Resultados en la VM (2026-11-21)

* **Disco**: `sdb` 40 G, `sdb1` ext4 `tbp-pruebas`, 38,7 G libres, montado en `/mnt/tbp-pruebas` (fstab `nofail`). Windows: `C:` con 11,3 GB libres
  (VMDK `tbp-0.vmdk` 3,4 GB). Con el techo de 3 GB hay margen; no se hicieron escrituras grandes.
* **Copia**: 7 cadenas (477 archivos, 126 MB) copiadas con sha256; segunda corrida sin cambios (0 copiados). Verificador: 236 archivos con sha256 OK,
  full de ≤ 16 h, uso 4 %, cifrado activo.
* **Cifrado en reposo**: el contenedor no contiene texto plano (`TheBuryProjectDb`, `BACKUP`, `SQLSERVR`: 0 coincidencias); `luksDump` v2 / aes-xts-plain64 / 512 bits.
* **Disco ausente simulado** (`umount` + `cryptsetup close`): `touch`/`mkdir` en el punto de montaje → *Permission denied*; `copy-removable --strict` → rc 1
  «NO es un punto de montaje», nada se escribió en `/`; el verificador → rc 13 y estado `exit 13 verified false`. Al remontar por `crypttab`/`fstab`
  (`systemd-cryptsetup@tbp\x2drespaldo` + `mnt-tbp\x2drespaldo.mount`): datos intactos, verificador rc 0. `findmnt --verify` sin errores.
* **Restauración de prueba** desde la copia en un SQL Server descartable (`bury-lcrt-*`): full `…_2026-11-21_060203Z` + 67 logs con sha256 verificado,
  RESTORE ONLINE, `DBCC CHECKDB` sin errores, 144 migraciones, 141 tablas; **0 residuos** (contenedores/volúmenes/temporal). No se tocó ninguna base existente.
* **Cron de las 02:30 del 21/11** (pendiente del PR #341): `all` completó OK a las 05:30Z con el fix (sin «omitido»).

## Qué NO se validó / riesgos restantes

1. **No es offsite** y no cubre pérdida del PC (ver arriba). Sigue pendiente una copia independiente real (otro equipo/ubicación) y la custodia de claves.
2. **Clave en la misma VM** (`/etc/bury-backup/local-disk.key`, root 0400): protege contra fuga del VMDK, no contra root. **Acción del propietario**: copiar
   **fuera de la VM** la clave y `/etc/bury-backup/local-disk.luks-header`; si se pierde la clave los datos del contenedor son irrecuperables
   (los backups de `/srv/bury-backups` siguen existiendo).
3. **Arranque de la VM no probado con reinicio real**: el remontaje por `crypttab`/`fstab` se probó con `systemctl`/`mount`, sin reiniciar (no se interrumpió la preproducción).
   Si no monta, la copia queda «atrasada» y el verificador alerta (`nofail`: la VM arranca igual).
4. VMDK disperso: no se reduce solo; el contenedor ocupa lo escrito (hoy ~190 MB) hasta 3 GB como máximo. `C:` (11,3 GB libres) es compartido con toda la VM.
5. Si el PC suspende a las 02:30 no hay backup ni copia (riesgo previo, no nuevo).
6. La retención de la copia es por capacidad, no por días.

## Operación

```
# estado (en la VM)
bash scripts/backup/verify-local-copy.sh                       # exit 0 = copia sana
bash scripts/backup/copy-removable.sh --dry-run                # qué copiaría
TMPDIR=/mnt/tbp-pruebas/tmp bash scripts/backup/restore-test-local-copy.sh   # ensayo de recuperación (~30 s)
# .env: BACKUP_REMOVABLE_DIR=/mnt/tbp-respaldo  BACKUP_REMOVABLE_MAPPER=tbp-respaldo  BACKUP_REMOVABLE_MAX_TOTAL_GB=2
#       BACKUP_REMOVABLE_MIN_FREE_GB=0.3  BACKUP_REMOVABLE_MAX_AGE_HOURS=48
# montar a mano si no montó: sudo systemctl start 'systemd-cryptsetup@tbp\x2drespaldo' && sudo mount /mnt/tbp-respaldo
# recuperación sin la VM original: cryptsetup open --key-file <clave> tbp-respaldo.luks tbp-respaldo (o restaurar el encabezado con luksHeaderRestore)
```

Ver también [`p06-disco-pruebas-vm.md`](p06-disco-pruebas-vm.md) (el mismo disco, usado para ensayos) y [`backup-restore.md`](backup-restore.md) §8.2.

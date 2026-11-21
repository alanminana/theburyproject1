# Disco de PRUEBAS de la VM tbp (2026-11-20)

> **Este disco NO es un respaldo externo independiente del PC y no permite cerrar P-06 por sí solo.** Es un VMDK dentro de la misma carpeta
> de la VM, en el mismo volumen `C:` de Windows que el resto de la VM: si se pierde la PC (o ese volumen, o OneDrive lo corrompe), se pierden
> la VM, sus backups productivos simulados **y** este disco a la vez. Sirve únicamente para ensayos con datos sintéticos.

## Identificación (solo lectura, antes de tocar nada)

| Dato | Valor |
|---|---|
| Sistema | `sda` 100 G (`sda2` ext4 `/`, UUID `5a3dc53b…`): **no se tocó** |
| Disco nuevo | `sdb` 40 G, `VMware Virtual S`, `scsi0:1` → `tbp-0.vmdk` (`lsblk`: sin particiones, sin FSTYPE/UUID) |
| Verificación de vacío | `blkid -p` rc 2 (sin firma), `wipefs -n` sin firmas, `sfdisk -d` sin tabla, primeros y últimos 4 MiB en cero, sin holders LVM/md, no en `/proc/mounts` ni `fstab` |
| Ubicación física | `C:\Users\c0sm3\OneDrive\Documents\Virtual Machines\tbp\tbp-0.vmdk` (**monolithicSparse**: crece a medida que se escribe y **nunca se reduce solo**; 3,36 GB tras las pruebas; `fstrim` no soportado) |
| Espacio libre en Windows | `C:` ≈ 14,9 GB al inicio (11,5 GB al cierre de las pruebas, 13,4 GB tras la limpieza). El VMDK de 40 GB **no está reservado**: llenar el disco guest por encima de lo libre en `C:` pausaría/rompería la VM |

Como todo estaba inequívocamente vacío se formateó sin pedir más autorización (instrucción del propietario).

## Preparación

`parted` GPT (partición `tbp-pruebas`, 1 MiB–100 %) → `mkfs.ext4 -L tbp-pruebas -m 1` (UUID `75303265-fb12-443d-8b92-f73902d8e409`) →
`/mnt/tbp-pruebas` (snow:snow, 750) con la línea de `fstab` (respaldo previo `/etc/fstab.bak-20261120`):

```
UUID=75303265-fb12-443d-8b92-f73902d8e409 /mnt/tbp-pruebas ext4 defaults,nofail,x-systemd.device-timeout=10s 0 2
```

Capacidad: 40 G brutos, **39 G útiles** (1 % reservado). **Límite acordado de uso real: ~3-5 GB** por el espacio de `C:`. Rutas productivas
(`BACKUP_DIR=/srv/bury-backups`, `.env`, cron `/etc/cron.d/bury-backup`) **sin modificar**.

Estructura: `/mnt/tbp-pruebas/{tmp,logs,evidencia,backups-sinteticos}`; los arneses se ejecutan con `TMPDIR=/mnt/tbp-pruebas/tmp`
(los volúmenes Docker de los arneses siguen en `/var/lib/docker`, disco del sistema, y se eliminan solos).

## Pruebas (todas con datos sintéticos, resultado 2026-11-20)

| Prueba | Resultado |
|---|---|
| `test-hardening.sh` | 81 OK / 0 fallos |
| `test-check-custody.sh` | 55 OK / 0 fallos |
| `test-retention-capacity.sh` | 96 PASS / 0 FAIL |
| `test-retention-capacity.sh --db` (SQL Server real descartable, restore real) | 112 PASS / 0 FAIL |
| `test-dr-integral.sh` (pérdida total → recuperación; imagen del ERP ya construida) | 53 PASS / 0 FAIL, sin residuos Docker |
| Datos sintéticos propios (~600 MB, 2002 archivos, 3 backups tar.gz + sha256) | integridad, restauración idéntica por manifiesto sha256, corrupción de 1 byte detectada (sha256 y `gzip -t`), truncado detectado, **fallback al backup anterior válido** |
| Persistencia | `umount` → `fsck.ext4 -fn` limpio → `mount -a` por UUID/fstab → sha256 idénticos → `findmnt --verify` sin errores |
| Persistencia (2.ª verificación, 22:5x, con datos nuevos de ~100 MB) | tar.gz + sha256 → `sync` → `umount` → `fsck.ext4 -fn` limpio → `mount -a` → sha256 OK, `gzip -t` OK, restauración idéntica por manifiesto; luego limpieza. `findmnt --verify`: sin errores |

Notas: la primera versión del caso "fallback" falló por un defecto **del test** (el byte elegido ya valía 0, no hubo corrupción); se corrigió
invirtiendo el byte y se repitió: PASS. **No validado**: comportamiento de `nofail` con el disco realmente ausente (exigiría quitar el VMDK de
la VM) y reinicio de la VM con el disco (no se reinició; se pidió no reiniciar VMware/Windows).

## Respaldo automático de las 02:30

Evidencia (`/srv/bury-backups/logs/backup.log`, cron journal): el 2026-11-20 a las 02:30 el job `all` **fue omitido** (`otra ejecucion en
curso`) porque el `log` de 15 min arrancó en el mismo segundo y tomó el lock. Es el defecto que corrige P-06 (PR #338: el diario ahora espera
`BACKUP_LOCK_WAIT_SEC`, default 180 s). El clon de la VM se actualizó a ese código el 20/11 a las 18:01, **después** de ese 02:30, de modo que
**aún no hay evidencia del 02:30 con el fix**. La primera es la de la madrugada del 2026-11-21 (02:30 local = 05:30Z). Verificar con:

```
grep -a "2026-11-21T05:3" /srv/bury-backups/logs/backup.log | grep -a "backup 'all'"   # esperado: 'all' inicio y luego 'all' OK
# (un 'log' omitido mientras corre 'all' es legitimo; lo que NO debe aparecer es "backup 'all' omitido")
ls /srv/bury-backups/sql | grep full_2026-11-21_053                # el .bak y su .sha256
```

Requiere la VM encendida a esa hora (no suspender el PC).

## Qué NO cubre (P-04 / P-06 siguen abiertos)

- P-06: copia externa **independiente del PC** (otro dispositivo/ubicación): sigue sin existir (`BACKUP_REMOTE` vacío, `BACKUP_REQUIRE_OFFSITE=false`).
  Este disco es el mismo hardware y el mismo volumen que la VM. Tampoco: custodia de claves real, ensayo con el offsite real.
- P-04: servidor definitivo físico, UPS, restore on AC power loss, reserva de IP en el router real, Windows Server (si se elige), Tailscale real.
- Evidencia de varias noches del `all` de las 02:30 con el fix.

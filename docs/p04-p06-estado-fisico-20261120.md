# P-04 / P-06: estado real, matriz y recursos físicos que faltan (2026-11-20)

> Histórico: la VM `tbp` quedó fuera del plan el 2026-11-24; este documento conserva la evidencia tal como se emitió.

Base: `origin/main` 4ba63f57. Alcance: auditoría **de solo lectura** del anfitrión Windows y de la VM Ubuntu `tbp` (VMware Workstation) y preparación del mecanismo de copia externa.
No se apagó ni reinició nada, no se borró ni modificó ningún respaldo, no se movieron secretos y no se contrató nada. No se repitieron los ensayos sintéticos ya aprobados
(`p04-p08-matriz-criterios-vm-simulada.md`, `p06-*`). Sin credenciales en este documento.

> **Conclusión corta.** P-06 **no puede cerrarse**: hoy **todos** los respaldos viven en el mismo PC que aloja la VM (los discos virtuales de la VM, el único destino
> de los respaldos, están en el disco del sistema de ese PC) y **no existe ningún medio ni destino externo**. Perder el PC (robo, incendio, falla de disco, ransomware) = perder
> la base y todas sus copias. Un ensayo de recuperación «en otra máquina» hecho en el mismo PC (WSL2) prueba el procedimiento, **no** la independencia física.

## 1. Hallazgos nuevos de esta tanda (con evidencia)

| # | Hallazgo | Evidencia (2026-11-20) | Estado |
|---|---|---|---|
| 1 | **El respaldo diario completo no se hacía.** El cron del `all` (02:30) y el del `log` (`*/15`, que incluye `:30`) arrancan en el mismo segundo; el `log` ganaba el `flock` y el `all` se «omitía» con un `WARN` (exit 4) que no llega al log de errores. El último `full` + `files` de la VM es del 19/11 18:29 UTC, aunque la VM estuvo encendida toda la noche | `backup.log`: `05:30:01Z … backup 'all' omitido: hay otra ejecucion en curso`; `monitor-status/full.json` con 26 h de antigüedad; `/var/log/bury-backup-errors.log` vacío | **Corregido y desplegado** (PR #338, `929b1978`: el modo `all/full/files` espera el lock hasta `BACKUP_LOCK_WAIT_SEC`; si no lo obtiene es `ERROR` en stderr). En la VM un `backup.sh all` manual posterior terminó OK (21:02Z, `full.json`/`files.json` `verified:true`). **Falta comprobar el cron real de las 02:30** (la colisión se da solo a esa hora): revisar `backup.log` mañana |
| 2 | **Todos los discos son internos del mismo PC.** Discos: SATA 224 GB (D:), NVMe 932 GB (C:, arranque), NVMe 1,8 TB (E:). El único dispositivo USB es un lector de tarjetas **vacío** (0 GB, RAW) | `Get-PhysicalDisk`/`Get-Disk`; `Test-HostVmContinuity.ps1` H05/H06 | **Sin destino externo** |
| 3 | **Los discos de la VM están en C: con 16 GB libres** y pueden crecer hasta 100 GB (75 GB más de lo usado hoy: 24,5 GB + 8,6 GB de RAM en disco). Si el invitado llena su disco, el volumen del anfitrión se llena y la VM se pausa o se corrompe | `Test-HostVmContinuity.ps1` H01: **FAIL** | Pendiente (mover la VM exige apagarla: autorización) |
| 4 | **La VM no vuelve sola tras reiniciar Windows.** `VmwareAutostartService` está detenido/manual, no hay `vmAutoStart.xml`, ni tarea programada, ni entrada de inicio. Una actualización con reinicio o un corte de luz la deja apagada hasta que alguien inicie sesión y la encienda | H03: **FAIL**; Windows sin política de reinicio de Windows Update (H04a WARN); suspensión en CA desactivada (H04b OK) | Pendiente (requiere autorización + un reinicio de prueba) |
| 5 | **La carpeta de la VM está bajo una ruta redirigida de OneDrive** (`…\OneDrive\Documents`, punto de reanálisis). OneDrive hoy **no está instalado ni corre**, pero reinstalarlo/iniciar sesión intentaría sincronizar `.vmdk`/`.vmem` en uso | H02: WARN | Pendiente (mover la VM fuera del perfil) |
| 6 | **No existe paquete de recuperación** (`~/secure-preprod`, `~/secure-prod` inexistentes). Los respaldos incluyen SQL, claves de Data Protection (`keys/`), `uploads/` y los certificados/CA de Caddy (`caddy-data/`), pero **no** `.env` ni `backup-offsite.env` (SA, admin, passwords crypt, transporte). Sin ellos no se reconstruye el servidor desde los respaldos | `tar tzf` del último `files_*.tar.gz` (nombres, no contenidos); `check-custody.sh`: 2 avisos (C08, C12), sin paquete | **Herramienta preparada** (`recovery-package.sh`); crearlo exige que el propietario defina la custodia de la passphrase |
| 7 | Los respaldos locales **no están cifrados en reposo** (`BACKUP_DIR` 0700, `.bak` sin compresión ni cifrado). El cifrado `crypt` solo existe en la copia externa, que no está configurada | `.env` sin `BACKUP_REMOTE`; `check-custody.sh` C08 | Esperado hasta que exista destino externo; el medio extraíble debe cifrarse (§4) |
| 8 | La CA de Caddy (`root.key`) viaja en el tar de archivos (`BACKUP_CA_KEY=include`, default) | `check-custody.sh` C12 | Decisión del propietario (`seal`, `docs/backup-ca-key-custodia.md`) |

## 2. Dónde están hoy los respaldos y cómo se protegen

| Aspecto | Estado real |
|---|---|
| Ubicación | VM `/srv/bury-backups` (mismo disco virtual que el sistema, 99 GB, 24 % usado); ese disco virtual es un archivo del disco C: del PC anfitrión |
| Contenido | SQL: `full` + `log` cada 15 min (236 `.trn` verificados); `files`: Data Protection `keys/`, `uploads/`, `caddy-data/` (CA y certificados). **No incluye** `.env`/`backup-offsite.env` (paquete de recuperación, hallazgo 6) |
| Integridad | `RESTORE VERIFYONLY` + `sha256` por archivo en cada corrida (117 corridas de `log` OK en el período); `restore-test.sh` semanal (último OK 19/11, DBCC limpio); set `files`+`full` coherente por manifiesto |
| Cifrado | Ninguno en reposo local. Copia externa: `rclone crypt` (contenido y nombres) implementada y probada sintéticamente (86 + 53 comprobaciones), **sin destino real** |
| Retención | 14 días, mínimo 3 cadenas, capacidad por cadenas (`BACKUP_MAX_TOTAL_GB`/`MIN_FREE_GB` = 0: sin tope); remota 30 días/mín. 3 |
| Dependencias de recuperación | Imagen/código (Git), `.env` y `backup-offsite.env` (**solo en el servidor**), passphrase crypt y del paquete (custodia A, sin definir), `known_hosts` del SFTP, CA de Caddy (en el tar) |
| Anfitrión Windows | Sin tareas programadas de respaldo ni de la VM; los backups del ERP-Windows (`install-windows.ps1`) no están instalados; la VM no se respalda desde el anfitrión |

## 3. Matriz P-04 / P-06

Leyenda: **IMPL** implementado · **PROB** probado · **FÍS** pendiente físico/operativo (hardware, lugar o decisión humana) · evidencia entre paréntesis.

| Requisito | IMPL | PROB | Pendiente físico / dependencia |
|---|---|---|---|
| Respaldo SQL full + log 15 min, archivos, sha256, VERIFYONLY | Sí | Sí (VM real, 117 corridas `log`) | Comprobar mañana que el `all` de las 02:30 corre por cron (fix #338 ya desplegado; el diario real no corrió entre el 19/11 18:29Z y el 20/11 21:01Z) |
| `restore-test` semanal (DBCC) | Sí | Sí (19/11) | — |
| Retención por antigüedad y capacidad | Sí | Sí (sintético) | Topes reales según el disco definitivo |
| Recuperación completa en segunda máquina | Sí | Sí (WSL2 del **mismo PC**, 131 s) | No cuenta como independencia física |
| Copia externa SFTP + crypt + retención remota | Sí | Sí (sintético: 86 + 53 PASS) | **FÍS: destino real en otro sitio** + `known_hosts` por canal independiente + custodia de passphrase |
| Disco extraíble (`copy-removable.sh`) | Sí | Sí (sintético) | **FÍS: el disco** + rotación/ubicación fuera del equipo; cifrar el disco |
| **Verificador de destino externo** (`check-external-destination.sh`, nuevo) | Sí | Sí (47 casos; discos virtuales, carpetas compartidas y rutas comunes se rechazan) | Confirmación humana de independencia física |
| **Paquete de recuperación cifrado** (`recovery-package.sh`, nuevo) | Sí | Sí (34 casos, secretos sintéticos) | **FÍS/propietario:** custodia A (passphrase) y B (paquete) distintas y fuera del servidor; crear el paquete real |
| Custodia de `root.key` de la CA (`seal`) | Sí | Sí (108 PASS) | Decisión del propietario |
| Restauración **comprobada desde un medio físicamente independiente** | Mecanismo sí | **No** | **FÍS: criterio de cierre de P-06; hoy sin medio** |
| Auditoría del anfitrión (`Test-HostVmContinuity.ps1`, nuevo) | Sí | Sí (ejecutada en el anfitrión real: 3 FAIL, 3 WARN) | Resolver los FAIL (abajo) |
| Servicios vuelven tras reinicio de la **VM** / caída de contenedores / `kill -9` | Sí | Sí (VM real, ×4 reinicios) | — |
| La **VM** vuelve tras reinicio de **Windows** | **No** | **No** (H03 FAIL) | Autostart de VMware (§5); prueba con reinicio autorizado |
| Espacio del volumen que aloja la VM | — | H01 **FAIL** (16 GB libres vs hasta 75 GB de crecimiento) | Mover la VM a un volumen con espacio o ampliar; exige apagado |
| Política de reinicios de Windows Update / horas activas | — | H04a WARN | Decisión del propietario |
| UPS, BIOS «Restore on AC Power Loss», hardware definitivo | — | — | **FÍS** (un software no puede verificarlo) |
| Recuperación tras **pérdida del host** | Mecanismo sí | Drill sí (mismo PC) | **Imposible hoy:** respaldos, VM y `.env` están en el mismo equipo; depende del destino externo y del paquete |

## 4. Qué recurso falta exactamente (lista para el propietario)

1. **Un medio externo real**, a elegir (cualquiera cierra la parte física; recomendado combinar 1 y 2): (1) disco USB/HDD dedicado de ≥ 500 GB que se **rote y se guarde fuera del
   local**; (2) un servidor SFTP/NAS en **otro sitio** con usuario propio, o un servicio de almacenamiento (costo → autorización). Un segundo disco del mismo PC, una carpeta
   compartida del anfitrión o WSL2 **no** valen.
2. **Quién custodia** la passphrase del paquete de recuperación y las passwords `crypt` (custodia A) y dónde se guarda el paquete (custodia B), distintas y fuera del servidor
   (`docs/backup-custodia.md`).
3. **Autorización** para: apagar la VM y moverla fuera de C:/OneDrive; configurar el inicio automático de la VM; y reiniciar Windows una vez para probarlo.
4. Decisiones: política de reinicios de Windows Update; `BACKUP_CA_KEY=seal`; plataforma/servidor definitivo y UPS.

## 5. Cómo cerrar cada bloqueo cuando exista el recurso

**Copia externa en disco USB (sin tocar nada hasta que el disco exista):**
1. Conectar el disco a la VM (VMware → *Removable Devices*) o montar en el servidor definitivo; formatear **a mano** (`ext4` o `exfat`), montar por `UUID` con `nofail`
   en `/mnt/respaldo` y crear **una vez** `touch /mnt/respaldo/.bury-backup-removable`.
2. `scripts/backup/check-external-destination.sh --path /mnt/respaldo`: debe dar **0** (E01–E07). Rechaza un disco virtual VMware, una carpeta compartida (`vmhgfs`/`vboxsf`/`9p`),
   `tmpfs` y una carpeta común del mismo disco; con un disco interno no virtual avisa. **No** prueba que el disco salga del equipo: eso lo confirma una persona y se anota en esta matriz.
3. En `.env`: `BACKUP_REMOVABLE_DIR=/mnt/respaldo`; `scripts/backup/copy-removable.sh --apply --strict` y verificar `monitor-status/removable-copy.json` (`verified true`).
   Cifrar el disco (LUKS) si sale del local.
4. **Prueba de restauración desde ese medio** (criterio de cierre): con el disco **desconectado de la VM y reconectado en otro equipo** (o en la VM tras reiniciarla),
   restaurar la última cadena sobre una base aislada (`restore-test.sh` apuntando a la copia) y comprobar `DBCC CHECKDB`, número de migraciones y los archivos (`keys/`, CA).
   El procedimiento `restore-test.sh --removable` **no existe todavía**: hoy se hace con `BACKUP_DIR` apuntando a `<disco>/bury-backup` (requiere `.bury-backup-root`);
   está **sin validar** hasta que haya disco, y se documenta como pendiente.

**Paquete de recuperación:** `scripts/backup/recovery-package.sh create --out <medio externo>` (pide la passphrase por terminal; no admite `--out` dentro de `BACKUP_DIR`),
`verify` y `extract` en un equipo distinto (requiere `gpg` 2.x). El `.sha256` es solo una comodidad; la autenticidad la da el cifrado.

**Autostart de la VM (sin ejecutar nada hasta autorizar):** vía soportada de VMware Workstation: compartir la VM (*VM → Manage → Share…*; la mueve a `C:\ProgramData\VMware\Shared VMs`)
y habilitar *Autostart* en el servidor de Workstation (servicio `VmwareAutostartService` en Automático). Alternativa: tarea programada «al iniciar el equipo» con
`vmrun -T ws start "<ruta>\tbp.vmx" nogui`; **no validada**. En ambos casos probar con un reinicio de Windows autorizado y verificar con `Test-HostVmContinuity.ps1` (H03) y `Check-ErpHealth.ps1`.

## 6. Herramientas nuevas

| Herramienta | Uso | Prueba |
|---|---|---|
| `scripts/backup/check-external-destination.sh` | Descarta lo que no es un medio externo por hechos comprobables (solo lectura) | `test-check-external-destination.sh` (47), en `test-hardening.sh`/CI |
| `scripts/backup/recovery-package.sh` | `create`/`verify`/`extract` del paquete cifrado con `.env`/`backup-offsite.env` (GnuPG simétrico AES-256 con MDC: cifrado **autenticado**; S2K SHA-512 iterado; temporales solo en tmpfs; extracción solo de archivos regulares) | `test-recovery-package.sh` (34: autenticidad, enlaces simbólicos/hardlinks/rutas con espacio, colisión de nombres, passphrase por caracteres, 4 `create` concurrentes, sin temporales en disco), en `test-hardening.sh`/CI |
| `scripts/install/Test-HostVmContinuity.ps1` | Auditoría de solo lectura del anfitrión Windows (espacio, OneDrive, autostart vinculado a **esta** VM —nunca «OK» sin un reinicio comprobado—, reinicios, medio externo) | Ejecutado en el anfitrión real (3 FAIL, 3 WARN) |

## 7. Resumen para sincronizar Notion

> **P-06 — NO finalizado. P-04 — NO finalizado.** Auditoría real 2026-11-20: los respaldos existen solo en el mismo PC que aloja la VM; no hay ningún medio/destino externo (3 discos internos; USB = lector vacío).
> Defecto confirmado y corregido en código (PR #338): el respaldo diario completo no corría (colisión de lock con el log de 15 min); pendiente desplegarlo en la VM. Hallazgos de P-04: la VM no
> vuelve sola tras reiniciar Windows; sus discos están en C: con 16 GB libres (pueden crecer 75 GB) y bajo una ruta redirigida de OneDrive; sin política de reinicios de Windows Update. Herramientas
> nuevas: verificador de destino externo, paquete de recuperación cifrado, auditor del anfitrión. **Faltan (físico/propietario):** medio externo real y su custodia, paquete de recuperación real,
> autorización para mover la VM/autostart/reinicio de prueba, UPS/BIOS. Criterio de cierre de P-06 sin cumplir: restauración comprobada desde un medio físicamente independiente.

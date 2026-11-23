# P-04: plan de ejecución de la ventana de mantenimiento (preparado 2026-11-23; NO ejecutado)

Objetivo: (1) ampliar la RAM de la VM `tbp` a 16 GB, (2) sacar la VM de OneDrive y de `C:`, (3) lograr que arranque sola tras reiniciar Windows y (4) **probar** esa recuperación.
Sirve a `p04-continuidad-matriz-20261123.md` (pasos 3-5 de su §5) y se apoya en `p04-diagnostico-bugcheck-20261123.md`.
**Este documento solo prepara: no se apagó, movió ni reconfiguró nada.** Todo lo de abajo exige autorización del propietario, una persona delante del PC y ≈ 2-3 h sin ERP.

## 0. Datos medidos que fijan el plan (2026-11-23, solo lectura)

| Dato | Valor | Consecuencia |
|---|---|---|
| RAM del anfitrión | 31,5 GB (2×16 GB DDR5-4800); libres ahora 9,3 GB | Capacidad: ver §1 |
| VM hoy | 8 780 MB, 4 vCPU; `.vmem` 8,57 GB **en la carpeta de la VM** (memoria respaldada por archivo) | Con 16 GB el `.vmem` crece a ≈ 16 GB **en el volumen de la VM** |
| Carpeta de la VM | `C:\Users\c0sm3\OneDrive\Documents\Virtual Machines\tbp` (redirección de OneDrive) | Se mueve |
| Tamaño en disco | ≈ 29,3 GB de discos (100 GB lógicos + 40 GB `tbp-0.vmdk` = `sdb`) + 8,6 GB `.vmem` | Destino necesita ≥ 29 + 16 + crecimiento |
| `C:` | 540 GB, **16,6 GB libres** | No sirve de destino |
| `E:` (NVMe 1,8 TB, NTFS) | **743 GB libres** | **Destino propuesto: `E:\VMs\tbp`** (fuera del perfil de usuario) |
| `D:` (SATA 224 GB) | 186 GB libres | Descartado (SATA, más lento) |
| VMware | Workstation 25.0.1 (build 25219725); `VmwareAutostartService` manual/detenido; sin `vmAutoStart.xml` | Ver §4 |
| `.vmx` | CD-ROM apuntando a `C:\Users\c0sm3\Downloads\ubuntu-26.04.1-desktop-amd64.iso` y a `autoinst.iso` | Rutas absolutas/relativas que hay que revisar al mover |
| WSL2/Docker Desktop (desarrollo) | Sin `.wslconfig` ⇒ tope por defecto 15,8 GB | Hay que acotarlo |
| Respaldos | Cron 02:30 `all` y `*/15` `log` OK; copia local cifrada (LUKS) `verified: true` | Hacer un respaldo final antes de apagar |
| Reservas de energía/UPS | No hay UPS | Riesgo de la ventana: un corte durante la copia ⇒ se conserva el original (§7) |

## 1. Comprobación de capacidad para 16 GB (antes de la ventana)

| Concepto | GB |
|---|---|
| VM | 16,0 |
| Sobrecarga de VMware (video, estructuras; **estimación**, medir con el VM corriendo) | ≈ 1,0 |
| Tope de WSL2/Docker de desarrollo, **fijado** con `memory=8GB` | 8,0 |
| Margen de Windows + navegadores | 4,0 |
| **Total** | **29,0** de 31,5 → **cabe, 2,5 GB de holgura** |

Sin acotar WSL2 la suma sería 16 + 1 + 15,8 + 4 = 36,8 GB > 31,5: **no se debe subir la RAM sin fijar antes `memory=8GB`** en `%USERPROFILE%\.wslconfig`. `Test-HostVmContinuity.ps1` (H09) lo comprueba
(acepta `GB`/`MB`). Dentro de la VM, 16 GB cubren una segunda pila (`db` 3 g + `app` 2 g + `caddy` 256 m ≈ 5,3 GB por pila, ≈ 10,6 GB para dos + SO ≈ 3 GB): sin swap sigue siendo ajustado; no instalar la
segunda pila sin medir con `docker stats`.

## 2. Dependencias y precondiciones (todas deben cumplirse)

1. Autorización escrita del propietario para la ventana y para (opcional) desinstalar Riot Vanguard (ver diagnóstico §4).
2. `E:` con ≥ 120 GB libres (hoy 743) y `C:` sin cambios previstos.
3. Respaldo final verificado **el mismo día**: `monitor-status/full.json` y `removable-verify.json` en `verified:true`; sin tareas de backup en curso (la ventana no debe cruzar las 02:30).
4. Sin instantáneas (*snapshots*) de la VM (``vmrun listSnapshots` = 0 hoy): mover con instantáneas complica el rollback.
5. Alguien con sesión de administrador en el PC (varios pasos piden elevación) y acceso físico si algo no arranca.
6. Datos de acceso anotados fuera del PC: clave de `snow`, passphrase de LUKS (`tbp-respaldo`), `.env` del servidor. Sin ellos no se verifica el cierre.
7. Un solo operador de VMware: nadie abre la VM vieja durante o después de la copia (§3 paso 9).

## 3. Ventana A: RAM, ubicación, autostart (orden estricto)

Estimación: 2-3 h. ERP no disponible desde el paso 2 hasta el 12.

| # | Paso | Comando / acción | Verificación (criterio de seguir) | Rollback |
|---|---|---|---|---|
| 1 | Línea base | `powershell -File scripts/install/Test-HostVmContinuity.ps1`, `ssh snow@192.168.50.162 'free -m; docker compose ps'`, `Check-ErpHealth.ps1 -VmHost 192.168.50.162` | Se guarda la salida (evidencia «antes») | — |
| 2 | Respaldo final y parada ordenada del ERP | En la VM: `bash scripts/backup/backup.sh all` y `bash scripts/backup/copy-removable.sh --apply --strict`; luego `docker compose stop` (SQL con `shutdown-timeout 60`) | `full.json`/`removable-copy.json` `verified:true`; contenedores `Exited (0)` | `docker compose start` |
| 3 | Apagar la VM con cierre ordenado | `ssh snow@192.168.50.162 'sudo systemctl poweroff'` (o `vmrun stop <vmx> soft`); esperar que `vmware-vmx` salga | Proceso `vmware-vmx` desaparecido; los `.lck` desaparecen | Encender la VM desde su ubicación original (§7) |
| 4 | **(Opcional, decisión del propietario) Desinstalar Riot Vanguard** | Apps → Riot Vanguard → Desinstalar; **reiniciar Windows** (la VM ya está apagada) | `Get-Service vgk` ausente; arranque limpio | Reinstalar desde Riot (no recomendado) |
| 5 | Fijar el tope de WSL2 | Crear/editar `%USERPROFILE%\.wslconfig` con `[wsl2]` / `memory=8GB`; `wsl --shutdown` (cierra Docker Desktop de desarrollo) | `Test-HostVmContinuity.ps1` H09 con `8 GB` y OK | Borrar el archivo; `wsl --shutdown` |
| 6 | **Copiar (no mover)** la VM | `robocopy "C:\Users\c0sm3\OneDrive\Documents\Virtual Machines\tbp" "E:\VMs\tbp" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XF *.lck *.log *.vmem *.vmss /XD *.lck /NP /LOG:E:\VMs\tbp-copy.log` | `robocopy` sin fallos; `Get-FileHash -Algorithm SHA256` de **cada** `.vmdk` origen = destino (≈ 29 GB, minutos) | Borrar `E:\VMs\tbp`: el original no se tocó |
| 7 | Congelar el original | Renombrar la carpeta de origen a `tbp.OLD-20261123` (misma ubicación) | La VM antigua ya no aparece en la lista de VMware; **no** abrirla | Renombrar de vuelta |
| 8 | Registrar la VM nueva y limpiar CD-ROM | En Workstation: *File → Open* `E:\VMs\tbp\tbp.vmx`; si pregunta, responder **«I moved it»** (conserva UUID y MAC `00:0C:29:60:01:54`: la IP fija no depende de la MAC, pero una reserva DHCP en el router sí); quitar o desconectar las unidades de CD con ISO de `Downloads` | Ajustes de la VM sin rutas a `C:\Users` | Abrir la VM original renombrada de vuelta (§7) |
| 9 | Subir la RAM a 16 GB | *VM → Settings → Memory* = `16384` MB (equivale a `memsize = "16384"` en el `.vmx`); sin tocar vCPU | `Test-HostVmContinuity.ps1` H01/H02/H09: `.vmx` en `E:`, fuera de OneDrive; presupuesto OK | Volver a `8780` MB |
| 10 | Primer arranque manual | Encender desde Workstation, **sin** autostart todavía | En la VM: `free -m` ≈ 16 000 MB; `ip -4 a` = `192.168.50.162`; `docker compose ps` (db/app/caddy `healthy`); `findmnt /mnt/tbp-respaldo` y `lsblk` con LUKS abierto; `scripts/install/Check-ErpHealth.ps1 -VmHost 192.168.50.162` verde; el siguiente cron `*/15` OK | Apagar y volver al original (§7) |
| 11 | Autostart | §4 | `Test-HostVmContinuity.ps1` H03 **WARN «configurado, no probado»** (nunca OK sin el reinicio) | Deshacer lo creado en §4 |
| 12 | Cierre de la ventana A | Esperar un ciclo de respaldo completo (02:30 del día siguiente) y el `*/15` | `backup.log` sin `ERROR`; `monitor-status` actualizado; ERP usable | — |

## 4. Autostart de VMware (a decidir en el paso 11)

Una VM no vuelve sola solo con «arranque automático»: tiene que **apagarse bien** cuando Windows se apaga, o el reinicio de prueba es en realidad un corte (SQL hace recovery, como el 22/11).

| Opción | Cómo | A favor | En contra / riesgo |
|---|---|---|---|
| **A. Servicio de autostart de VMware** (preferida si Workstation 25 la ofrece) | *VM → Manage → Share…* y luego *Autostart*; `Set-Service VmwareAutostartService -StartupType Automatic`; elegir acción de apagado **guest shutdown** | Soportada por VMware; maneja el apagado ordenado | **No validada** en este equipo: confirmar en la UI que Workstation 25.0.1 aún ofrece «Share»/Autostart (en versiones recientes se retiró en parte); mueve la VM a `C:\ProgramData\VMware\Shared VMs`, **que anula el destino `E:`**: revisar antes de elegir |
| **B. Tarea programada** | `schtasks /Create /TN "TBP-VM-Autostart" /SC ONSTART /DELAY 0001:00 /RU SYSTEM /RL HIGHEST /TR "\"C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe\" -T ws start \"E:\VMs\tbp\tbp.vmx\" nogui"` **más** un script de apagado de Windows (`gpedit.msc` → Configuración del equipo → Scripts → Apagar) con `vmrun -T ws stop "E:\VMs\tbp\tbp.vmx" soft` | No depende de sesión; ya reconocida por el auditor (H03) | **No validada**; una VM iniciada como `SYSTEM` no se ve en la interfaz del usuario y bloquea el `.vmx` (`.lck`) si alguien la enciende a mano; hay que probar que el *shutdown script* termina antes del apagado |
| C. Atajo en *Inicio* del usuario | — | Trivial | **Descartada**: exige iniciar sesión; no cumple el objetivo |

Después de crear A o B, comprobar con `Test-HostVmContinuity.ps1` y **no** declarar el autostart funcional hasta pasar la ventana B.

## 5. Ventana B: prueba de recuperación tras reiniciar Windows

Precondición: ventana A cerrada y estable ≥ 24 h, respaldo del día verificado, alguien delante del PC, sin sesión de usuario abierta (cerrar sesión antes del reinicio es parte de la prueba).

| # | Paso | Verificación |
|---|---|---|
| 1 | Línea base (`Test-HostVmContinuity.ps1`, `Check-ErpHealth.ps1`), anotar hora; respaldo `log` reciente | Todo verde |
| 2 | **Cerrar sesión** de Windows y reiniciar con `shutdown /r /t 60` (desde otra sesión remota) o desde la pantalla de bloqueo | Se inicia el reinicio |
| 3 | **No iniciar sesión.** Cronometrar desde «Windows encendido» hasta que `Check-ErpHealth.ps1 -VmHost 192.168.50.162` pase (desde otro equipo o móvil en la LAN) | **Criterio de éxito: ERP sano ≤ 5 min tras el arranque de Windows, sin intervención** |
| 4 | Iniciar sesión y verificar | `vmware-vmx` en sesión 0 (H08 OK); en la VM `journalctl -b -1` termina con apagado ordenado; SQL **sin** líneas «rolled forward» de recuperación; `docker compose ps` `healthy`; LUKS reabierto; cron del siguiente `*/15` OK |
| 5 | Registrar RTO medido y actualizar `p04-continuidad-matriz-20261123.md` | La prueba queda **registrada manualmente** en la matriz: el auditor nunca emite OK para H03 (siempre WARN «no probado») |
| 6 | Repetir una vez más tras el siguiente reinicio planeado de Windows Update (o forzado) | Segunda corrida OK = recuperación probada |

**Fallo:** si pasados 10 min la VM no responde → no tocar nada más, iniciar sesión y encenderla a mano (el RTO queda registrado como fallido) y aplicar §7.

## 6. Riesgos

| Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|
| Corte o cuelgue del anfitrión durante la copia (0x133 cada ~4,5 días mientras Vanguard esté) | Media | Copia parcial | Se **copia, no mueve**; original renombrado e íntegro; hash de cada `.vmdk` |
| Perder la MAC/UUID y con ello la reserva DHCP | Baja | VM sin IP esperada | Responder «I moved it»; IP fija por netplan no depende de la MAC; verificar `ip a` |
| `E:` sin la VM al fallar un disco (único destino) | Baja | Pérdida de VM | Respaldos SQL/archivos siguen en la VM y en el LUKS; mantener `tbp.OLD` ≥ 14 días; P-06 externo sigue pendiente |
| Falta de RAM del anfitrión tras 16 GB | Media si no se fija WSL | Paginación/`vmmem` terminado | Paso 5 antes del 9; H09; margen 2,5 GB |
| Autostart A mueve la VM a `ProgramData\Shared VMs` y rompe el destino | Media | Desvío del plan | Decidir A/B **antes** del paso 6; probar la UI en frío |
| Apagado de Windows mata la VM (sin *stop action*) | Alta si no se configura | SQL en recovery, riesgo de datos | Apagado ordenado obligatorio (A: guest shutdown; B: script de apagado) y verificar `journalctl -b -1` |
| Wi-Fi USB como único enlace sigue siendo frágil | Alta | VM inalcanzable tras arrancar | Resolver con Ethernet (paso 2 de la matriz); la prueba B puede dar falso negativo de red |
| Cron 02:30 durante la ventana | Baja | Backup en medio de la copia | Programar la ventana de modo que termine antes de las 02:00 |
| Vanguard desinstalado/no desinstalado cambia el experimento | — | Interpretación | Registrar la fecha exacta; observar 14 días (diagnóstico §4) |

## 7. Rollback general

- **Antes del paso 9:** volver a la carpeta original (`tbp.OLD-20261123` → renombrar a `tbp`), abrir su `.vmx`, encender. Cambios de `.wslconfig` se deshacen borrando el archivo. Nada se ha perdido (el original sigue íntegro, el respaldo del paso 2 vigente).
- **Después del 9, antes de cerrar:** apagar la VM, volver `memsize` a 8780 MB o abrir la carpeta original. Los datos escritos en la VM nueva desde el primer arranque **no** están en la original: si ya se registraron ventas, restaurar desde `backup.sh`/`restore.sh` (RPO ≤ 15 min por el respaldo de log) o cerrar la ventana sin retroceder.
- **Autostart:** A → `Set-Service VmwareAutostartService -StartupType Manual` y quitar la VM de *Autostart*; B → `schtasks /Delete /TN "TBP-VM-Autostart" /F` y quitar el script de apagado.
- **Datos:** una restauración completa desde respaldo está probada (`restore-test.sh`, DR integral 53 PASS, RTO 38 s en preproducción); ver `docs/backup-restore.md`.

## 8. Qué NO hace este plan

No resuelve el cuelgue del anfitrión (ver diagnóstico), no sustituye a un UPS ni a la conexión por Ethernet, no crea el respaldo externo (P-06) y no instala la segunda pila. Un reinicio probado
cubre «Windows reinicia limpio», **no** «Windows se cuelga o se corta la luz»: eso exige también la prueba de corte con UPS descrita en la matriz (§5, pasos 6-8).

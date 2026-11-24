# P-04 / P-07: diagnóstico del anfitrión, margen de memoria y procedimiento de continuidad (2026-11-23)

> Histórico: la VM `tbp` quedó fuera del plan el 2026-11-24; este documento conserva la evidencia tal como se emitió.

Base: `main` 647207c1 + PR #359 (matriz de continuidad, en revisión). **Solo lectura**: no se reinició Windows, no se movió la VM, no se tocó VMware/WSL/BIOS/controladores, no se cambiaron permisos. **P-04 sigue abierto. La recuperación automática del anfitrión NO está resuelta.** No se habilitan operaciones comerciales reales mientras los riesgos críticos de §1 no se resuelvan o se acepten formalmente por escrito.

Convención: **[V]** verificado en esta pasada · **[R]** tomado de otro documento · **[H]** hipótesis, sin prueba.

## 1. Prioridad 1: diagnóstico del anfitrión

### 1.0 Actualización: los volcados SÍ fueron analizados por otro agente (reportado, no re-verificado aquí)

PR #359 (`docs/p04-diagnostico-bugcheck-20261123.md`) informa, con elevación aprobada por el propietario y WinDbg/`cdb`: **los 3 minidumps son `0x133` y en los 3 el código en ejecución es `vgk.sys` (Riot Vanguard, anticheat de kernel) esperando un spinlock**; el crash del 22/11 ocurrió con la versión posterior a la actualización del 20/11. Los `0x50` del 11/11 y el `0x133` del 9/11 no tienen minidump: **causa no determinada**. Wi-Fi USB, NVMe y RAM no aparecen en ninguna pila. Esto **reemplaza** los sospechosos ordenados de §1.3, que eran hipótesis sin volcados (GPU/PCIe, RAM, BIOS siguen como contexto, no como causa). Lo que sigue en 1.1-1.4 se conserva como registro de lo que se verificó antes de contar con ese análisis. Acción de mayor valor (decisión del propietario, reversible): desinstalar Vanguard y observar ≥ 14 días sin `0x133`; mientras exista, el equipo no es apto como servidor.

### 1.1 (histórico) Los minidumps NO pudieron analizarse desde mi sesión

`C:\Windows\Minidump` (3 archivos: 111726-22843-01, 111726-24187-01, 112226-23828-01, ~12 MB c/u) devuelve **Acceso denegado** a la sesión actual (no elevada) [V]. No se cambió la ACL ni se usó ninguna elevación (modificaría Windows). WinDbg está disponible (`Microsoft.WinDbg` 1.2611, `cdb.exe`/`kd.exe` amd64) [V].

**Acción requerida del propietario (30 s):** en una consola **como administrador**, copiar los volcados a una carpeta legible (copia, no mover):
`Copy-Item C:\Windows\Minidump\*.dmp D:\tmp\dumps\`. Luego se analiza con `cdb -z <dmp> -c "!analyze -v; lmvm <modulo>; q"` con símbolos `srv*D:\tmp\sym*https://msdl.microsoft.com/download/symbols`. El análisis no está hecho: **ninguna de las conclusiones de abajo proviene de un volcado**.

### 1.2 Lo que sí se verificó sin los volcados (eventos del sistema)

| Hecho [V] | Detalle |
|---|---|
| Bugchecks, 30 días | `0x133` ×4 (9/11, 17/11 10:09, 17/11 23:58, 22/11 22:21); `0x50` ×2 (11/11 12:35 y 13:40); 3 apagones sin bugcheck (13/11, 14/11, y 4/11 en transición de suspensión) |
| `0x133` | Parámetro 1 = **1**: el watchdog venció por tiempo **acumulado** en DISPATCH_LEVEL (no por una sola rutina que no cede). Parámetro 2 = 0x1e00 igual en los 4 casos. Parámetro 3 con el mismo desplazamiento (`…63c8`/`…53c8`) en 3 arranques: **mismo punto del kernel**, patrón repetible, no aleatorio |
| `0x50` | Dos casos, 71 min de diferencia: dirección de lectura en no paginado (`p2=0` lectura, `p4=2`) y **mismo desplazamiento de instrucción** (`…7c28`) en dos arranques distintos con ASLR: misma instrucción defectuosa en ambos |
| Hardware | Gigabyte Z890M GAMING X, BIOS F20 (6/10/2026); Intel Core Ultra 5 250K Plus; 2× Kingston KF560C36-16 (16 GB) corriendo a **4800 MHz** (módulos de 6000: no se aplica el perfil de fábrica; cualquiera de las dos configuraciones es hipótesis, no causa probada); NVMe Patriot P410 (C:, fw H241106a) y Seagate FireCuda 530 (E:, fw SU6SM005); GPU RTX 5070 Ti, controlador 32.0.16.2101 |
| **WHEA** | 7 eventos *corregidos* de PCIe Root Port (AER, `PCI\VEN_8086&DEV_7F38`, bus 0x80:0x1C:0): **6 el 16/11** (entre 00:54 y 04:04 UTC) y 1 el 11/11. Los dos días son **inmediatamente anteriores** a las caídas del 17/11 y del 11/11 |
| **NVIDIA** | `nvlddmkm` id 153 (error en GPUID 200) el 6/11, 7/11, 9/11 (×2) y 16/11 (×2) |
| Memoria virtual de volcado | `volmgr` 161/162: error al generar el volcado completo en 3 oportunidades (`BugCheckProgress 0x20063`); se conservaron los minidumps |
| Disco C: | 16 GB libres; `Volsnap` 24/33/35: instantáneas del volumen C: eliminadas por falta de espacio (20/11 y 23/11) |
| Otros | Sin entradas de `Disk`/`stornvme`/`nvme` ni `Kernel-Power` de temperatura en el período [V] |

### 1.3 Interpretación (acotada)

- Hay **dos defectos distintos** (0x133 y 0x50): dos familias de causa, no una sola.
- Los volcados y la coincidencia temporal con WHEA/NVIDIA hacen **plausibles** estos sospechosos, en este orden de la evidencia disponible y **sin ser concluyentes**: (1) camino PCIe del puerto raíz 0x80:0x1C (GPU o NVMe en ese puerto) y/o el controlador NVIDIA; (2) memoria o su configuración (4800 MHz con perfil de 6000 no aplicado; `0x50` también es típico de RAM defectuosa o de un controlador que corrompe memoria); (3) firmware BIOS F20 recién publicado en una plataforma nueva.
- **El adaptador Wi-Fi USB no figura** en ningún evento de falla; **no hay evidencia** de que sea causante. Sí es un riesgo de *red* independiente (§3), no de estabilidad. No se lo puede descartar ni acusar sin leer los volcados (lo hará `lmvm`/pila del hilo).
- No se concluye «hardware» ni «controlador»: eso lo decide el análisis de los 3 volcados.

### 1.4 Plan de diagnóstico (sin modificar nada hasta autorizar)

1. Copia de los 3 volcados (§1.1) → `!analyze -v`, módulo/pila de cada uno, ¿mismo módulo en los 2 `0x133` y en los 2 `0x50`? ¿`nt`, `nvlddmkm`, `storport`/`stornvme`, `rtwlanu`/USB, `vmx86`/`hcmon`/`vmci` (VMware)?
2. Si faltan datos: configurar volcado de kernel (ajuste de Windows: requiere autorización) y verificar espacio de `C:`.
3. Memoria: `MemTest86` fuera de línea (≥ 2 pasadas por módulo, requiere reiniciar y dejar el equipo ocupado varias horas: ventana).
4. Aislar el factor: probar con GPU integrada o bajar el enlace PCIe a Gen4 en BIOS, y comparar; actualizar BIOS/controladores **solo con autorización**, una variable por vez y registrando fecha para medir H07 durante 14 días.
5. **Criterio de cierre:** 0 cierres no limpios en 14 días corridos con la carga habitual (`Test-HostVmContinuity.ps1`, H07).

## 2. Prioridad 2: seguridad de la instalación (memoria)

| Dato [V]/[R] | Valor |
|---|---|
| Anfitrión | 31,5 GB [R #359 E7]; VM `tbp` **8780 MB** (8,6 GB) y 4 vCPU; WSL2 sin `.wslconfig` (tope por defecto ~15,7 GB); Docker Desktop del anfitrión detenido |
| VM, pila actual | `db` 3 g + `app` 2 g + `caddy` 256 m + SO; **sin swap**; `available` ≈ 4,5 GB medido [R] |
| Segunda pila en la **misma** VM | 2 × (3 g + 2 g) + caddy ≈ **10,5 GB > 7,9 GB utilizables** ⇒ OOM-kill probable de `db` (SQL Server) en la preproducción [R #359 §8] |

**Decisión de coordinación (sin ejecutar nada):**

1. **No iniciar la nueva pila** (`theburyproject-prod`) en `tbp` mientras la preproducción esté activa y la VM tenga 8,6 GB. Condición para arrancarla: VM ≥ 16 GB (o bajar `DB_MEMORY`/`APP_MEMORY` de ambas pilas y medir con `docker stats`) **y** autorización expresa. Se incluye: no usar el puerto 443 (la preproducción lo ocupa).
2. **Aviso para el agente 1** (texto a enviar; no se modificó nada de su trabajo): *«Riesgo de memoria: la VM `tbp` tiene 8,6 GB sin swap y la preproducción usa ~5,3 GB. No arranques la pila productiva en esa VM ni cambies recursos de la VM. Podés seguir con lo que no arranque servicios nuevos: `.env`, `docker compose config`, verificadores SQL, `bury-firewall` con cadena propia, preparación del directorio de respaldos propio, checklists. Cuando la VM se amplíe y se autorice, avisame y valido la Sección C.»*
3. **Ampliar la VM a 16 GB** (propuesta, no ejecutada): anfitrión 31,5 GB − 16 GB VM − tope WSL2 de 8 GB (`.wslconfig`: `[wsl2] memory=8GB`, `swap=4GB`) = margen ≈ 7,5 GB para Windows. Aplicación: apagar la VM con cierre ordenado, cambiar `memsize`, encender; el `.wslconfig` requiere `wsl --shutdown`. Son cambios en el anfitrión: **requieren autorización y ventana**.
4. Presupuesto resultante sugerido con la ampliación: VM 16 GB → dos pilas (≈ 10,5 GB de contenedores + SO 2,5 GB) ≈ 13 GB, margen ≈ 3 GB. Aun así sin swap en la VM: considerar 2 GB de swap en la VM.

## 3. Prioridad 3: continuidad (procedimiento preparado, NO ejecutado)

### 3.1 Estado actual [V]

| Elemento | Valor |
|---|---|
| Ubicación de la VM | `C:\Users\c0sm3\OneDrive\Documents\Virtual Machines\tbp\` (`tbp.vmx`). El directorio `OneDrive` es un *reparse point*: la VM vive bajo la sincronización de OneDrive y en `C:` |
| Tamaño actual en disco | ≈ 42 GB (9 GB de `.vmem` + ~33 GB de VMDK dispersos) |
| Crecimiento posible | Hasta unos +70 GB sobre lo actual (disco 99 GB + `sdb` 40 GB) [R #359 H01]; peor caso ≈ 115 GB en total |
| Volúmenes | **C:** NVMe Patriot, 16 GB libres (mismo disco que el sistema) · **D:** SSD SATA Kingston SA400 de 224 GB, 185 GB libres · **E:** NVMe Seagate FireCuda 530 de 2 TB, **743 GB libres** (ya aloja `E:\vm\lab`, `lab2`, `win lab`) |
| Autostart | `VmwareAutostartService` **Stopped / Manual**; sin `vmAutoStart.xml` |
| Sesión | `vmware-vmx` corre en la sesión interactiva 1 [R #359 E3] |

### 3.2 Destino recomendado

**`E:\vm\tbp\`**: es otro disco físico (≠ `C:`), 743 GB libres (6× el peor caso de 115 GB), NVMe de alto rendimiento y fuera de OneDrive. D: queda descartado como principal (SATA económico, 185 GB libres, un 60 % de margen menos). Verificar al ejecutar: `Get-PSDrive E` ≥ 200 GB libres; **y** que E: no sea el volumen que se va a retirar o repartir. Un solo disco físico distinto de C: **no** elimina el riesgo de pérdida total del equipo (sigue siendo el mismo PC; el offsite sigue pendiente).

### 3.3 Procedimiento (ventana autorizada, con la preproducción detenida y avisada)

| # | Paso | Verificación |
|---|---|---|
| 0 | **Respaldo previo**: forzar `bury-backup all` en la VM, confirmar `verifyonly=OK` y la copia en `sdb`/removible cifrada; exportar `.env`/claves del paquete seguro | Último full + logs OK; 0 alertas `backup.*` |
| 1 | Anotar estado: `docker compose ps`, hash de `/etc/bury-backup/*`, IP `192.168.50.162`, MAC `00:0C:29:60:01:54` | Captura en el registro de la ventana |
| 2 | En la VM: `docker compose stop` (cierre ordenado, `shutdown-timeout` 60 s) y `sudo shutdown -h now` | VMware muestra *Powered off* |
| 3 | En Windows: pausar OneDrive (Configuración → Pausar sincronización); **no** mover con el Explorador | `OneDrive` sin actividad |
| 4 | Copiar (no mover) `…\Virtual Machines\tbp\` → `E:\vm\tbp\` con `robocopy /E /COPY:DAT /R:1 /W:1 /LOG:` | Código de salida < 8; comparar `Get-FileHash` de `tbp.vmx` y de los 9 VMDK mayores (o `robocopy /L` sin diferencias) |
| 5 | Abrir `E:\vm\tbp\tbp.vmx` en VMware; al preguntar elegir **«I moved it»** (conserva MAC e UUID) | MAC `00:0C:29:60:01:54` intacta |
| 6 | Arrancar la VM, pasar a §3.5 (pruebas) | Pruebas OK |
| 7 | Dejar la copia original en OneDrive **intacta y renombrada** (`tbp.OLD`) hasta cerrar las pruebas y 7 días de operación estable; luego borrar con orden expresa | Rollback disponible |

**Rollback:** apagar la VM, reabrir `…\OneDrive\…\tbp\tbp.vmx` (intacto), arrancar, correr las mismas pruebas. Si la copia tiene `vmem` o un VMDK corrupto, repetir el paso 4 o restaurar desde el respaldo del paso 0 (probado).

### 3.4 VMware Autostart (preparado, sin aplicar)

1. Mover la VM (§3.3) y **compartirla** en VMware Workstation (*VM → Manage → Share*), que la registra en el entorno de VMware Workstation Server (`vmAutoStart.xml` en `C:\ProgramData\VMware\VMware Workstation Server\`).
2. En el servicio **«VMware Autostart Service»** (`VmwareAutostartService`): Inicio = **Automático (retardo)**, cuenta de servicio con permisos sobre `E:\vm\tbp` y `vmrun` (no una sesión interactiva). Reemplaza el arranque manual del usuario `snowsh`.
3. Configurar en `vmAutoStart.xml`: arranque `poweron`, retardo 60 s, parada `poweroff soft` (cierre ordenado) con tiempo de espera ≥ 120 s (la VM necesita ~60 s para parar Docker/SQL).
4. Alternativa si falla el servicio: tarea programada «Al iniciar el sistema» (cuenta de servicio, sin sesión) con `vmrun -T ws start "E:\vm\tbp\tbp.vmx" nogui`.
5. Política de reinicio de Windows Update: horas activas y no reiniciar con sesión, para no volver a depender de la intervención.

### 3.5 Pruebas posteriores (todas necesarias antes de declarar algo)

1. `Test-HostVmContinuity.ps1` → H01/H02/H03 sin FAIL.
2. VM: IP y MAC iguales, `docker compose ps` todos `healthy`, `Check-ErpHealth.ps1 -VmHost 192.168.50.162` verde.
3. Respaldo: ejecutar `bury-backup all` y un log; `verifyonly=OK`; restore-test del domingo.
4. **Reinicio de Windows sin iniciar sesión** (ventana, solo con autorización): medir el tiempo hasta `app healthy`.
5. Corte: apagar la VM con `vmrun stop hard` y medir RTO (ya probado en #326/#327; repetir sobre la nueva ubicación).
6. Prueba con el anfitrión caído (apagar) y verificación de aviso externo (P-08): sigue pendiente de observador externo real.
7. Registrar RTO/RPO y actualizar la matriz de #359.

### 3.6 Lo que no se resuelve con esto

La VM seguirá dependiendo de un **PC que se cae solo** (§1), de un enlace Wi-Fi USB en bridge (el equipo tiene un `Realtek PCIe 2.5GbE` integrado **desconectado** [V]: basta un cable al router y mover el bridge de VMware a ese adaptador, paso físico del propietario), de la falta de UPS y de la falta de offsite. **P-04 queda abierto y la recuperación ante caídas del anfitrión, sin resolver.**

## 4. PR #359

Autor: `alanminana`, rama `task/p04-continuidad-matriz-20261123`, 2 archivos (`docs/p04-continuidad-matriz-20261123.md`, `scripts/install/Test-HostVmContinuity.ps1`), mergeable. Checks al momento de consulta: `network-security`, `build-test-windows`, `compose-validate`, `docker-build`, `host-independence`, `secret-scan`, `windows-scripts`, 3 de 4 SQL Server en verde; **`build-test`, `sqlserver-concurrencia (0)/(1)` y `Playwright test` pendientes**. Sin revisiones ni comentarios. Revisión independiente de Codex: ver el registro de la PR.

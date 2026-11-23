# P-04: diagnóstico de los cierres no limpios del anfitrión (minidumps, 2026-11-23)

Alcance autorizado: análisis **de solo lectura** de los minidumps de `C:\Windows\Minidump`. Se copiaron (con elevación aprobada por el propietario vía UAC) a una carpeta temporal y se
analizaron con WinDbg/`cdb` 10.0.29680 y símbolos públicos de Microsoft (el servidor de símbolos solo recibe nombres/GUID de módulos, no datos del equipo). No se modificó Windows, ni
VMware, ni controladores, ni la configuración de volcados. Equipo: SNOWDESKMSI, Gigabyte Z890M GAMING X, BIOS F20, Windows 11 build 26100, 32 GB DDR5-4800 (JEDEC, sin XMP).

## 1. Resultado (con evidencia)

**Los tres minidumps disponibles son `0x133` (DPC_WATCHDOG_VIOLATION, `DPC_QUEUE_EXECUTION_TIMEOUT_EXCEEDED`) y en los tres el código que estaba ejecutándose es `vgk.sys`,
el controlador de kernel de Riot Vanguard (anticheat), esperando un *spinlock*.**

| Dump | Hora del crash | Uptime | Proceso | Pila (resumen) | Módulo |
|---|---|---|---|---|---|
| `111726-24187-01` | 17/11 02:35 | 3 d 1 h | `vgc.exe` | `KeBugCheckEx` ← `KeAccumulateTicks` ← … ← interrupción de reloj ← `ExAcquireSpinLockExclusive` ← `vgk+0x1d6d4f` | **vgk.sys** |
| `111726-22843-01` | 17/11 22:50 | 12 h 40 min | `System` | … ← `ExAcquireSpinLockShared` ← `vgk+0x1df18e` | **vgk.sys** |
| `112226-23828-01` | 22/11 16:38 | 4 d 16 h | `vgc.exe` | … ← `ExAcquireSpinLockExclusive` ← `vgk+0x1d4d4a` | **vgk.sys** |

Los tres tienen el mismo `FAILURE_BUCKET_ID` (`0x133_ISR_vgk!unknown_function`) y la misma firma de pila: el hilo de `vgk` llama a `ExAcquireSpinLock*` y queda girando a IRQL
elevado hasta que el *watchdog* del DPC lo corta (parámetro 2 = `0x1E00` ticks acumulados). Que sean 3 de 3 con el mismo módulo y la misma rutina descarta la coincidencia.
Los desplazamientos distintos (`0x1d4d4a`/`0x1d6d4f`/`0x1df18e`) corresponden a versiones distintas del controlador: el de hoy es `1.19.2-31+20261120` (archivo del 20/11 15:10)
y **el crash del 22/11 ocurrió con la versión posterior a la actualización del 20/11**, por lo que actualizar Vanguard no corrigió el defecto.

**Qué prueba y qué no:**
- Prueba: `vgk.sys` es el módulo que no libera el control (espera un *spinlock* que otro CPU no suelta). Eso es un *deadlock/contención dentro del controlador o con otro componente de kernel*.
- No prueba: **quién retiene el lock.** Un minidump contiene solo el CPU que dispara el bugcheck; el titular del lock (puede ser el propio `vgk`, el hipervisor de Hyper-V/VMware o
  otro controlador) no está en estos archivos. Tampoco se debe leer «Vanguard es el único defecto»: es el responsable **demostrado de estos tres crashes**.
- **Wi-Fi, NVMe y RAM no aparecen en ninguna pila** y no se les atribuye nada. Evidencia de contexto, no causal: RAM Kingston 2×16 GB a 4800 MHz (valor JEDEC, sin overclock), los tres
  discos reportan `Healthy`, y el único error de disco del mes (evento 51/7 sobre `Harddisk3`, 2/11) es anterior y no coincide con ningún crash.

## 2. Lo que NO se pudo analizar

- **`0x50` (PAGE_FAULT_IN_NONPAGED_AREA) ×2 del 11/11 y el `0x133` del 9/11:** no existen minidumps (solo se conservan 3 archivos). Los parámetros del evento 1001 (dirección inválida de
  pool + instrucción en `…7c28`) no alcanzan para identificar un módulo. **Causa no determinada.** Si vuelve a ocurrir se necesita el dump: ver §4.
- Los 3 crashes sin bugcheck (13/11, 14/11; 4/11 durante una suspensión) son cortes sin registro: pueden ser energía o un cuelgue total; sin evidencia.
- Un cuelgue anterior al bugcheck: el 22/11 el registro de eventos dejó de escribir a las 15:58:49 y el bugcheck se registró a las 16:38:44 (~40 min). Después el equipo **no reinició solo**
  (`AutoReboot=1`) hasta que alguien lo encendió a las 22:21. Causa no determinada (no se vio ningún evento entre ambos momentos).

## 3. Otros hallazgos de contexto (no causales demostrados; no actuar sin evidencia)

| Hallazgo | Evidencia | Lectura |
|---|---|---|
| Errores recurrentes de GPU (RTX 5070 Ti, controlador 32.0.16.2101 del 18/10/2026): ≥ 15 reportes `LiveKernelEvent 0x141` (timeout del motor de video) y 14 eventos `nvlddmkm 153` (9/11, 16/11) | Registro de aplicaciones y de sistema | Inestabilidad de GPU **separada** de los `0x133`: no aparece en las pilas. Vigilar; no culpar sin dump |
| Errores corregidos de PCIe (WHEA 17) en el puerto raíz `0x80:0x1C:0x0`, ×6 el 16/11 de madrugada | Registro de sistema (WHEA-Logger) | Corregidos por hardware, severidad baja; sin relación con ningún crash. Dispositivo del puerto no identificado |
| Controladores de terceros cargados al momento del crash (22/11): `vgk` (Riot), `gdrv3` (Gigabyte), `rtwlanu` (Realtek USB Wi-Fi, 9/1/2026), `hcmon`/`vmx86`/`vmnetbridge`/`vmnetuserif` (VMware), `googledrivefs`, `ipf_cpu` (Intel), `RTKVHD64`, `KslD`, `wtd` | `lm` del dump | Inventario, sin implicación. El conjunto **Vanguard + Hyper-V/VBS (HVCI activo, `HypervisorPresent=True`) + VMware** es una combinación conocida por interactuar mal; no se probó |

## 4. Acciones recomendadas (ninguna se aplicó)

1. **Decisión del propietario (la acción de mayor valor, reversible):** ¿se usa Riot Vanguard? Si no hace falta, desinstalarlo (Apps → Riot Vanguard; pide reiniciar). Si se necesita,
   dejarlo pero tratar al equipo como **no apto para servidor** y mover el ERP a un equipo sin Vanguard (o sin juegos) antes de la puesta en marcha real.
2. **Prueba de causalidad:** tras desinstalarlo, observar **≥ 14 días sin `0x133`**. La tasa observada es un cierre cada ~4-5 días (4 `0x133` en 14 días), así que 14 días sin ninguno
   equivalen a ≈ 95 % de confianza de que el problema desapareció (3 intervalos esperados; no es una prueba formal). `Test-HostVmContinuity.ps1` (H07) lo mide solo.
3. **Volcado completo para el próximo crash (ventana de mantenimiento, cambia el sistema):** `CrashDumpEnabled` hoy es 3 (minidump) y solo se conservan 3. Pasar a *kernel dump*
   y verificar espacio en `C:` (16 GB libres) permite identificar el titular del lock. No hecho: modifica configuración de Windows.
4. Actualizar BIOS (F20 de 6/10/2026) y el controlador de GPU **solo** si el crash persiste sin Vanguard; hoy no hay evidencia que los señale.
5. Registrar cualquier crash nuevo con `Get-WinEvent` (41/1001) y conservar el dump antes de que se sobrescriba.

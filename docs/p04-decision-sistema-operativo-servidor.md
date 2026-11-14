# P-04 — Evidencia para decidir el sistema operativo del servidor

Fecha: 2026-11-14. Estado: **recomendación técnica para el propietario; no se eligió el sistema operativo, no se instaló ni modificó nada en el servidor definitivo, no se tocó la VM del ERP antiguo ni ninguna base real.**
Alcance: solo dos opciones — **Windows 11 Pro + Docker Desktop (WSL2)** y **Ubuntu LTS + Docker Engine nativo**. Windows Server (Hyper-V + VM) sigue archivado ([instalacion-windows-server.md](instalacion-windows-server.md)).
Base: [p04-plataforma-matriz-automatizacion.md](p04-plataforma-matriz-automatizacion.md) (PR #310), [instalacion-servidor.md](instalacion-servidor.md), [docker-produccion.md](docker-produccion.md), [propuesta-infraestructura-produccion.md](propuesta-infraestructura-produccion.md). No se repitieron los ensayos sintéticos de #310.

## 1. Conclusión corta

**Recomendación principal: Ubuntu Server LTS + Docker Engine nativo, con el ERP antiguo en una VM KVM/libvirt**, condicionada a cuatro cosas (§6). Es una recomendación, no una decisión: **el ERP antiguo es el factor que puede invertirla** y hoy no hay ningún dato sobre su VM en el repositorio (§3).

Por qué se inclina hacia Ubuntu aun sin datos de la VM:

1. Todo el ERP corre en contenedores Linux: en Ubuntu el motor es el mismo que en CI y en producción de cualquier despliegue; en Windows se agrega una capa (Docker Desktop + WSL2 + sesión de usuario) que es la fuente de casi todas las trampas encontradas (§4).
2. Arranca sin sesión de usuario (`systemctl enable docker`); en Windows 11 nada arranca sin inicio de sesión automático con una contraseña guardada en el equipo.
3. Menos sobrecarga fija de RAM, y los límites del compose (`DB_MEMORY`, `APP_MEMORY`) se aplican directo sobre cgroups.
4. El propio proyecto ya había llegado a lo mismo: «**B (Ubuntu) recomendada si se compra/dedica equipo**» (`propuesta-infraestructura-produccion.md` §1).

Lo que **inclinaría a Windows 11**: que la VM antigua dependa de algo que solo funcione bien con el hipervisor de Windows (formato VHD/VHDX ya existente y sin tiempo para convertir, herramientas de Windows para esa VM), que nadie pueda mantener un Linux, o que el equipo ya sea una PC Windows que se usa para otras cosas.

## 2. Requisitos de cada opción

| Tema | Windows 11 Pro + Docker Desktop (WSL2) | Ubuntu LTS + Docker Engine |
|---|---|---|
| CPU | 64 bits con SLAT y virtualización activada en firmware ([Docker Desktop, requisitos](https://docs.docker.com/desktop/setup/install/windows-install.md)) | Para el ERP solo: 64 bits, sin virtualización. Para la VM antigua con KVM: VT-x/AMD-V y EPT/NPT activados |
| RAM mínima declarada | 8 GB para Docker Desktop con WSL2 (misma fuente) | 8 GB del proyecto (`instalacion-servidor.md` §2) |
| RAM real del ERP (medida, datos sintéticos) | idle ≈ 1,05 GiB (app 252 MiB + db 776 MiB + caddy 13 MiB); pico ≈ 1,2 GiB; migración ≈ 0,9 GiB. Límites del compose: db 3 GiB, app 2 GiB, caddy 0,25 GiB, migrate 3 GiB (`docker-produccion.md`) | igual |
| Sobrecarga fija del host | Windows 11 + servicios + `vmmem` de WSL2 (sin `.wslconfig` toma hasta la mitad de la RAM; con `memory=6GB` queda acotada). Orden de magnitud 3–4 GB para el SO: **no medido en esta tarea** | Ubuntu Server ≈ 0,5–1 GB: **orden de magnitud, no medido** |
| Almacenamiento | 20 GB libres mínimo; imágenes ≈ 10 GB + build cache 15 GB medidos en la PC de desarrollo; SQL Express ≤ 10 GB por base; backups aparte. **SSD** y **UPS** (riesgo aceptado: no hay UPS) | igual; ext4 nativo, sin 9p |
| Edición / versión del SO | Pro (Hyper-V y administración remota); versión dentro del soporte de Microsoft | 24.04 LTS (estándar hasta ≈ abr. 2029, dato general sin verificar aquí) o 26.04 LTS (publicada el 23-abr-2026, soporte estándar hasta abr. 2031; Docker la soporta según su [guía oficial](https://docs.docker.com/engine/install/ubuntu.md)). #310 validó **24.04**; 26.04 **no** se probó |
| Para correr además la VM antigua | Rol Hyper-V (VHD/VHDX nativos) | KVM/QEMU + libvirt (lee qcow2/raw; vmdk/vdi/vhd/vhdx se leen o convierten con `qemu-img`) |

**Dimensionamiento orientativo** (la RAM de la VM es el dato desconocido): RAM total ≥ SO + ERP (≈ 2 GiB para uso normal, hasta ≈ 8 GiB si se reserva todo el presupuesto de límites) + RAM asignada a la VM antigua + margen de caché. Con una VM de 4 GB asignados (**suposición, no un dato**) → 16 GB cubren ambas opciones con holgura; 8 GB solo alcanzan sin la VM. Con la VM antigua en el mismo equipo, el cuello de botella probable es el **disco** (SQL Server del ERP nuevo + la base de la VM antigua), no la CPU.

## 3. ERP antiguo: qué sabemos (nada) y qué haría falta

Búsqueda en el repositorio (docs, scripts, deploy): **no hay ninguna descripción de la VM antigua** — ni hipervisor actual, ni formato de disco, ni tamaño, ni sistema invitado, ni RAM/vCPU, ni firmware (BIOS/UEFI), ni red, ni dependencias de hardware. Las únicas referencias a «sistema anterior» son de datos importados (`sistema-documental.md`), y los `.vhdx` del repo son de la VM Ubuntu del plan archivado de Windows Server. **No se puede afirmar compatibilidad con ninguna de las dos opciones.**

Lo que sí se puede decir en general (conocimiento técnico estándar, **no verificado contra su VM**):

| Aspecto | Hyper-V en Windows 11 | KVM en Ubuntu |
|---|---|---|
| Formatos | VHD/VHDX nativos; VMDK/VDI/qcow2/raw → convertir (`qemu-img`, `Convert-VHD`) | qcow2/raw nativos; VMDK/VDI/VHD/VHDX se leen o convierten con `qemu-img` |
| Invitado Windows | Integración nativa (drivers sintéticos de Hyper-V); si viene de VMware/VirtualBox, desinstalar sus *tools* y revisar drivers | Necesita drivers VirtIO (o emulación IDE/e1000, más lenta) para buen rendimiento; invitado migrado desde otro hipervisor puede pedir reactivar o dar pantalla azul al cambiar el controlador de disco |
| Invitado Linux | funciona (Gen 1/Gen 2) | funciona, mejor integrado |
| Firmware | Gen 1 = BIOS, Gen 2 = UEFI: **hay que elegir el que coincida** con el invitado | BIOS (SeaBIOS) o UEFI (OVMF): igual criterio |
| Dispositivos | USB/serie pasan con limitaciones | USB/PCI passthrough posible; depende del hardware |
| Convivencia con Docker | Hyper-V y WSL2/Docker Desktop comparten la plataforma de hipervisor de Windows: conviven, pero VirtualBox/VMware Workstation corren sobre ella con penalización de rendimiento | Docker y KVM comparten kernel sin capas extra |
| Arranque automático de la VM | Acción de inicio automático de Hyper-V: arranca con el servicio, **sin sesión de usuario** (asimetría: Docker Desktop sí necesita sesión) | `virsh autostart`: arranca sin sesión |

Tres riesgos que **solo los datos reales resuelven**: (a) licencia del sistema invitado y de su software (puede estar atada a hardware/MAC); (b) dependencias físicas (llave USB, puerto COM, impresora fiscal); (c) si el invitado arranca tras la conversión. Conversión y prueba de arranque deben hacerse sobre una **copia** de la VM, nunca sobre la original.

**Herramienta incluida en esta rama:** `scripts/install/vm-disk-intake.py` — inventario de **solo lectura** de los archivos de la VM (formato por cabecera, tamaño, si falta el extent de un VMDK, si es OVA) que indica qué conversión haría falta para cada opción. Se prueba sin VM real: `bash scripts/install/test-vm-disk-intake.sh` (cabeceras sintéticas, extents VMDK y falsos positivos). **No** determina SO invitado, drivers ni si arranca.

## 4. Comparación

Leyenda: ● ventaja clara, ◐ neutra / depende, ○ desventaja. Evidencia = lo realmente probado en #310 u otras pruebas del repo; «sin probar» = no hay prueba en el servidor real.

| Criterio | Windows 11 Pro + Docker Desktop | Ubuntu LTS + Docker Engine | Evidencia |
|---|---|---|---|
| Consumo de recursos | ○ SO más pesado + capa WSL2 | ● | Orden de magnitud sin medir en ambos; ERP medido (§2) |
| Estabilidad / reinicio automático del ERP | ○ depende de inicio de sesión automático (contraseña guardada), Docker Desktop al iniciar sesión y tarea `TheBury Docker Start`; Windows Update puede reiniciar | ● `systemctl enable docker` + `restart: unless-stopped`, sin sesión | #310: caída real de `app`/`db`/`caddy` se recupera en ambos. **Reinicio del equipo completo: sin probar en ninguno** (`instalacion-servidor.md` §3.3) |
| Docker | ◐ Docker Desktop: capa extra y actualizaciones propias | ● Docker Engine + containerd, soportado oficialmente en 24.04 y 26.04 | #310: 47/47 y 48/48 PASS. El Ubuntu de #310 es **Ubuntu anidado en WSL2** (kernel compartido): valida motor/userland, **no** kernel/systemd/ufw del servidor final |
| SQL Server | ◐ en contenedor sobre VHDX de WSL2; ext4 interno | ● en contenedor sobre ext4/overlayfs nativo | Parada limpia (SIGTERM) resuelta con entrypoint en ambos; **rendimiento de disco real sin medir** |
| Virtualizar el ERP antiguo a la vez | ◐ Hyper-V maduro y con VHDX nativo; convive con WSL2 | ◐ KVM/libvirt eficiente; exige convertir si no es qcow2/raw y VirtIO en invitados Windows | **Sin evidencia de la VM**; nada de esto se probó |
| Backups / recuperación | ◐ Programador de tareas con el mismo usuario; repo en 9p es lento | ● cron/systemd, permisos POSIX reales | DR sintético verificado con los mismos scripts (`backup-restore.md`); copia externa real **sin verificar** |
| Actualizaciones | ○ Windows Update (reinicios forzados) + Docker Desktop + WSL | ◐ `unattended-upgrades` + parches de kernel; alguien debe mantener Linux | — |
| Tailscale | ◐ servicio de Windows; la IP `100.x` puede no existir al arrancar Docker | ● `tailscale0` + systemd; atado de `100.64.0.0/10` | Políticas ACL: 131 pruebas; **nodo real y ACL publicada sin probar** |
| Caddy | = | = | `tls internal` + CA: 14 pruebas, ambos |
| Firewall | ○ las reglas Allow de `com.docker.backend.exe` pueden anular el filtro: verificar con cliente LAN real | ◐ `DOCKER-USER` (un puerto publicado no pasa por `ufw`) — exige `bury-firewall.sh` | Linux: 18 pruebas en dockerd descartable; Windows: modelo 49 pruebas; **ninguno con cliente real** |
| Monitoreo | ◐ `Check-ErpHealth.ps1` vía `wsl.exe` | ● `monitor.py` como `bury-monitor.service` | P-08: smoke en ambos; receptor externo y alertas reales sin configurar |
| Licencias | ◐ Windows 11 Pro (licencia del equipo); Docker Desktop es gratis para empresas < 250 empleados y < US$ 10 M de facturación ([términos](https://docs.docker.com/subscription/desktop-license/)); si el propietario supera cualquiera, requiere suscripción | ● sin costo de SO ni de motor (Docker Engine es gratuito). Soporte comercial opcional | La licencia del **sistema invitado** del ERP antiguo se paga igual en ambas |
| Mantenimiento | ◐ conocimiento Windows probablemente ya existente; las trampas de WSL2/Docker Desktop son específicas | ◐ exige a alguien que administre Linux (SSH, parches); documentado paso a paso | El proyecto documenta ambas |

## 5. Matriz de decisión

| Escenario (según lo que informe el propietario) | Opción favorecida | Motivo |
|---|---|---|
| VM antigua en VMDK/qcow2/raw, invitado Linux o Windows sin hardware especial; hay quien pueda administrar Linux | **Ubuntu** | menor sobrecarga, arranque sin sesión, mismo motor que CI |
| VM antigua ya en VHD/VHDX (Hyper-V) y no hay margen para convertir/probar | **Windows 11** | cero conversión; Hyper-V nativo |
| Invitado de la VM necesita dispositivo USB/serie/licencia ligada a hardware | **A medir en ambos** con una copia; sin evidencia no se decide | passthrough y licencias dependen del caso |
| Nadie puede mantener Linux y el equipo se usa también para otras tareas de oficina | **Windows 11** | el costo operativo de Linux supera el beneficio; asumir sesión automática, reinicios de Windows Update acotados y la trampa del firewall |
| Equipo dedicado, con 16 GB o más, SSD y UPS | **Ubuntu** | caso para el que el proyecto ya recomendaba Linux |
| RAM total ≤ 8 GB con la VM antigua en el mismo equipo | **Ninguna sin ampliar RAM** | no cabe el ERP + VM + SO con margen |

## 6. Condiciones para que la recomendación (Ubuntu) se sostenga

1. La VM antigua se convierte y **arranca** desde una copia en KVM (o, si no, queda decidido que el ERP antiguo corre en otro equipo).
2. Alguien responsable instala parches (`unattended-upgrades` o ventana mensual) y accede por SSH solo desde LAN/Tailscale.
3. El servidor tiene al menos 16 GB de RAM (o la suma real de §2 lo confirma), SSD y UPS (hoy sin UPS: riesgo aceptado).
4. Se **repite el arnés en el Ubuntu real** (§7) antes de cargar datos, incluido el reinicio completo del equipo, que #310 no pudo cubrir. Si en esa prueba aparece una falla de plataforma, se reabre la decisión.

## 7. Qué se puede automatizar sin el servidor definitivo, y qué no

| Prueba | Sin servidor | Con el servidor (cuando se autorice) |
|---|---|---|
| Compose, persistencia, caída real, restore + `DBCC CHECKDB`, primer ingreso, pantallas | **Ya hecho** en #310 (ambos motores) | Repetir `test-host-independence.sh` en el host final: valida kernel/systemd/ufw/disco reales |
| Política de acceso Tailscale, firewall `DOCKER-USER`, Caddy/CA | **Ya hecho** (suite P-05) | Nodo real, ACL publicada, cliente remoto y móvil |
| Compatibilidad de formato de la VM antigua | **Sí, con los archivos reales de la VM** (`vm-disk-intake.py`; `qemu-img info`/`check`) — solo lectura sobre una copia | — |
| Conversión del disco y arranque de la VM | **Parcial**: se puede ensayar en una VM anidada en cualquier PC con la **copia**; no mide rendimiento ni hardware | Arranque definitivo, red/MAC, licencia, rendimiento |
| Dimensionamiento de RAM/disco con ambas cargas | **No** (depende de la VM y del equipo) | Medición con `docker stats` + VM encendida durante una semana |
| Reinicio del equipo, corte de energía, Windows Update, BIOS *Restore on AC Power Loss* | **No** | Siempre en el hardware real |
| Rendimiento del disco con SQL Server | **No** | Prueba en el SSD real |

## 8. Datos que debe proporcionar el propietario

**Del equipo físico**
1. Marca/modelo, CPU (modelo exacto) y si la virtualización (VT-x/AMD-V + EPT/NPT) está activada en el firmware.
2. RAM instalada y ampliable; tipo de disco (SSD/NVMe/HDD), capacidad y cantidad de discos; si habrá un segundo disco para backups.
3. UPS: sí/no y modelo; opción de BIOS *Restore on AC Power Loss*.
4. ¿Equipo dedicado al ERP o también de uso general? ¿Quién lo administra y con qué conocimientos (Windows / Linux)?

**De la VM del ERP antiguo (solo lectura, sobre una copia)**
5. Hipervisor actual y versión (VMware, VirtualBox, Hyper-V, otro).
6. Archivos de la VM: nombres, extensiones, tamaños y cantidad de discos; si tiene *snapshots*.
7. Sistema operativo invitado, versión y **licencia** (¿transferible?, ¿atada a hardware o MAC?).
8. RAM y vCPU asignadas; BIOS o UEFI.
9. Red: bridged/NAT, IP fija, nombre; si el software depende de la MAC.
10. Dependencias físicas: llave USB, puerto COM/serie, impresora fiscal, otros.
11. Motor de base de datos del ERP antiguo y tamaño de la base; ¿debe estar encendido 24/7 o solo para consultas?
12. Procedimiento actual de respaldo de esa VM y plan a futuro (¿se apaga cuando termine la migración?).

Con los puntos 5–12 se puede cerrar la compatibilidad; con los puntos 1–4, el dimensionamiento. Sin ellos, la comparación de §4 queda como criterio técnico general, no como garantía.

## 9. Límites de esta tarea

- Sin instalación ni cambios en el servidor definitivo; sin tocar la VM antigua ni bases reales. `vm-disk-intake.py` solo lee cabeceras.
- Las cifras de sobrecarga de SO (§2) y los comportamientos de conversión/hipervisor (§3) son **conocimiento general, no medidos aquí**; están marcados como tales.
- Ubuntu 26.04 LTS está soportada por Docker según su documentación, pero #310 se validó en 24.04; elegir la versión es parte de la decisión.

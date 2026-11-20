# Matriz de criterios P-03..P-08 y D-05 con la VM Ubuntu como servidor definitivo SIMULADO (2026-11-19)

Estrategia: la VM Ubuntu (VMware, 4 CPU, 7,8 GiB, disco 100 GB, Docker Engine 29.10, ERP real en Docker + Caddy + SQL Server Express, datos
**sintéticos**) es el servidor definitivo **simulado**. Todo lo que se puede probar ahí se probó; lo que no, queda pendiente con el motivo exacto.
**Nada de esto cierra un hito**: las condiciones de aceptación originales no se modificaron. Sin credenciales ni datos productivos en este documento.

> **Fuente de los criterios.** No tuve acceso al tablero de Notion (no hay herramienta de Notion en la sesión): los criterios se tomaron de la
> documentación versionada (`propuesta-infraestructura-produccion.md`, `p05-tailscale-acceso-remoto.md` §8-9, `p08-receptor-alertas-procedimiento.md`,
> `p04-plataforma-matriz-automatizacion.md`, `csp-enforce-activacion.md` §3, `p03-ensayo-transicion-cuentas.md`, `estado-software-pre-instalacion.md`).
> **Hay que reconciliar esta matriz con Notion** y copiar allí los estados y la evidencia.

Leyenda: **VALIDADO** (evidencia suficiente) · **VALIDADO HOY** (ejecutado en esta tanda) · **EJECUTABLE** (se puede en la VM; no se hizo) ·
**PROPIETARIO** (decisión, credencial o gasto) · **FÍSICO/PRODUCCIÓN** (exige el servidor real, la red real o uso real).

## 1. Matriz por criterio

### P-03 — Seguridad de usuarios
| Criterio | Estado | Evidencia |
|---|---|---|
| Transición de cuentas (SuperAdmin ×2, rotar `admin`, desactivar demo, último SuperAdmin, recuperación, sesiones revocadas) en clon Development/Staging | VALIDADO | `p03-ensayo-transicion-cuentas.md`; `TransicionCuentasCookieHttpTests` en CI |
| Mismo ensayo en **Production** (login SQL del ERP, Docker, Caddy/TLS) | **VALIDADO HOY** | `scripts/qa/p03/p03_vm_produccion.py`: **19 OK / 0 FALLA**; exige `--confirmo-entorno-de-ensayo`, usa cuentas `p03.*` con sufijo único, restaura `admin` (activo, clave original, verificado) y da de baja (lógica) las cuentas creadas |
| Bootstrap Production exige `Admin:Email/Password` | VALIDADO | `estado-software-pre-instalacion.md`; la instalación limpia de la VM lo ejerció |
| Cuentas reales del negocio, rotación de la clave real de `admin`, qué hacer con `vendedor2`, límite de nombres | PROPIETARIO | credenciales reales y decisiones de producto (§3) |

### P-04 — Servidor
| Criterio | Estado | Evidencia |
|---|---|---|
| Instalación limpia (`install.sh`), 144 migraciones, health, smoke | VALIDADO HOY | + `test-host-independence.sh` nativo: 48 PASS |
| Persistencia: `restart`, `down`/`up` sin `-v`, reinicio de la VM (×4) | VALIDADO HOY | el dato sintético persiste siempre |
| Fallos reales: `kill -9` de app y de SQL; reinicio de `dockerd` | VALIDADO HOY | app vuelve en ~2 s; SQL: `ready` 503 con `live` 200, recupera en ~4 s |
| **Corte de energía simulado** (apagado hard desde VMware con un dato confirmado un instante antes) | **VALIDADO HOY** | los 2 clientes (incl. el confirmado) persisten; SQL recuperó solo; contenedores, firewall, monitor y servicios volvieron; **hallazgo** §2.1 |
| Disco lleno controlado (`fallocate` hasta 5-6 GiB libres) | **VALIDADO HOY** | WARNING a 16 GiB, CRITICAL + "acción" a 6 GiB; la app siguió escribiendo, el backup de log y `ready` siguieron OK; todo `resolved` al liberar |
| Arranque sin sesión de usuario | VALIDADO HOY | Linux/systemd: sin autologin gráfico, todo vuelve |
| Disco 20 → 100 GB en línea | VALIDADO | `growpart`+`resize2fs`, `e2fsck -n` limpio |
| IP fija / reserva | VALIDADO HOY (VM) | netplan estático 192.168.50.162/24 (hallazgo §2.1); reserva en el router real: FÍSICO |
| Plataforma definitiva (Windows Server+Hyper-V / Windows 11 / Ubuntu) | PROPIETARIO | decisión abierta (§3) |
| UPS, *restore on AC power loss* de BIOS, hardware, corte de luz real | FÍSICO | una VM no tiene fuente de energía ni BIOS reales |
| Windows Server + VM, `install-windows-server.ps1`, firewall de Windows | FÍSICO | requiere Windows Server real |

### P-05 — Red segura
| Criterio | Estado | Evidencia |
|---|---|---|
| Caddy/TLS interno; solo 443 publicado; 1433/8080 inalcanzables | VALIDADO HOY | desde Windows (LAN): 443 → 200 con la CA; 8080 y 1433 sin respuesta |
| Firewall del host sin saltarse el de Docker (`bury-firewall.sh`/`DOCKER-USER`) | VALIDADO HOY | LAN → 200; origen ajeno (contenedor en `docker0`) → bloqueado; persiste tras reinicio |
| **Comportamiento de la política Tailscale** (2 personas autorizadas, 1 sin acceso, sin acceso entre personas, solo `tcp/443`, revocación individual) | **VALIDADO HOY (ensayo con Headscale)** | §1.1. Es un ensayo de red; **no sustituye** la tailnet real |
| **Perfil administrador remoto** (443 + 22) y usuario común limitado a 443; intruso sin acceso; revocación del administrador | **VALIDADO HOY (ensayo con Headscale)** | §1.2 y `p05-tailscale-acceso-remoto.md` §9b: administrador 443+22 (banner SSH), usuario solo 443, intruso nada; ACL ampliada por error o revocada: el resultado es el esperado |
| **Capa de host (`ufw`/`DOCKER-USER`) efectiva sobre la tailnet** | **VALIDADO HOY, con corrección** | solo con `tailscale up … --netfilter-mode=nodivert`; con el modo por defecto la capa de host no ve la tailnet (§2.7). Documentación y mensaje del bootstrap corregidos |
| CA confiada por clientes | VALIDADO HOY (parcial) | curl desde Windows con la CA; navegadores reales: ver abajo |
| Aplicar `policy.hujson` en la consola real y pasar sus `tests`; `tag:erp`; expiración deshabilitada; *Device approval*; *Node sharing* off | PROPIETARIO | cuenta/tailnet real. **La tailnet existente (Raspberry Pi) no se tocó**; ver §3 pregunta 1 |
| Acceso real desde dos clientes reales y tercer dispositivo denegado; Tailscale apagado/datos móviles | PROPIETARIO + FÍSICO | personas, equipos y un teléfono reales |
| Instalar la CA en navegadores reales (Windows/Firefox/iOS/Android) | PROPIETARIO | modifica el almacén de confianza de equipos reales; `Install-ErpCa.ps1` ya probado |
| Router sin *port-forwarding*, CGNAT, DNS/`hosts` de la red real | FÍSICO | la red definitiva |

#### 1.1 Resultado del ensayo de red con Headscale (control de acceso real, tailnet descartable)
Servidor de control Headscale v0.26.1 propio en la VM (nodo `tbp`, `tag:erp`), clientes Tailscale oficiales en esta PC (otro host), política de ensayo
equivalente a la real. **Ninguna cuenta ni tailnet de Tailscale fue usada o modificada.**

| Prueba (desde cada cliente, por la tailnet) | persona1 (autorizada) | persona2 (autorizada) | persona3 (sin grupo) |
|---|---|---|---|
| `https://tbp` con la CA (SNI `tbp`) | **200** | **200** | sin respuesta |
| `tcp/443` | abierto | abierto | bloqueado |
| `tcp/22`, `1433`, `8080`, `80`, `3389` | bloqueados | bloqueados | bloqueados |
| acceso a otra persona | no | no | no |
| tras **revocar a persona2** (sacarla del grupo y recargar la política) | 200 | **sin respuesta** | — |

Trampa: la imagen de Tailscale usa modo *userspace* por defecto; sin `TS_USERSPACE=false` no hay interfaz TUN y los "bloqueados" son falsos. Reproducción:
`scripts/qa/tailscale-ensayo/README.md`.

#### 1.2 Perfil administrador remoto y capa de host (ensayo con Headscale)
Roles del perfil `linux-vm`: **usuario** (`tcp/443`), **administrador** (`443` + `22`), **intruso** (sin grupo). Misma infraestructura descartable de §1.1. Las mediciones se hicieron con la política de ensayo **sin campo `proto`** (TCP/UDP/ICMP); la versión versionada ahora es explícita por protocolo (TCP/UDP 443 y solo TCP 22 para el administrador): validada de sintaxis con `headscale policy check`, **no re-medida**. Solo se midió TCP.

| Prueba (por la tailnet) | usuario | administrador | intruso |
|---|---|---|---|
| `https://tbp` con la CA | 200 | 200 | sin respuesta |
| `tcp/22` | bloqueado | **abierto** (banner `SSH-2.0-OpenSSH`) | bloqueado |
| `1433`, `8080`, `80`, `3389` / ping a otras personas | bloqueados / no | bloqueados / no | bloqueados / no |

Independencia de las dos capas (con `--netfilter-mode=nodivert` y `ufw allow in on tailscale0 from <IP /32 del admin> to any port 22`): ACL ampliada por error (el filtro
del servidor listaba el 22 para el usuario) → el host lo bloquea; ACL revocada al administrador con la regla de host aún presente → pierde 443 y 22; grupos vacíos → nadie
llega. Detalle en `p05-tailscale-acceso-remoto.md` §9b.

### P-06 — Backups y recuperación

> **Actualización 2026-11-20:** auditoría real del anfitrión y de los respaldos, matriz P-04/P-06 y recursos físicos que faltan en `docs/p04-p06-estado-fisico-20261120.md` (no hay ningún medio externo; el respaldo diario completo no corría por una colisión de lock, corregida en #338; la VM no vuelve sola tras reiniciar Windows).
| Criterio | Estado | Evidencia |
|---|---|---|
| `backup.sh` full/log/files, `restore-test` (DBCC OK), cron de backups, log cada 15 min | VALIDADO HOY | cron ejecutó solo; `Check-ErpHealth` lo lee desde Windows |
| Mecanismo SFTP + crypt, retención remota, fallos de conexión/host key/credenciales | VALIDADO HOY | `test-offsite-sftp.sh`: **86 PASS** |
| Pérdida total y recuperación solo desde la copia externa aislada | VALIDADO HOY | `test-dr-integral.sh`: **53 PASS**, RTO técnico 38 s |
| **Drill de recuperación completa en una SEGUNDA MÁQUINA** (criterio de aceptación de P-06) | **VALIDADO HOY** | respaldos reales de la VM (par `files`+`full` coherente + log posterior) → clon nuevo en esta PC (WSL2 + Docker Desktop): `restore-files` 2 s, SQL (full + 1 log) 4 s, stack arriba en **131 s** (≈115 s son build de imagen). 4 clientes (el 4.º posterior al full: RPO por log), 144 migraciones, `DBCC CHECKDB` limpio, **CA idéntica** y **cookie de la VM original válida (200; sin cookie 302)** = claves Data Protection restauradas |
| Destino offsite real en OTRO sitio, `known_hosts` por canal independiente, restore desde esa copia, retención remota con datos reales | PROPIETARIO | gasto/credenciales/servidor externo (§3 pregunta 2) |
| Custodia de la password `crypt`, del recovery package y de `BACKUP_CA_KEY=seal` | PROPIETARIO | decisión de custodia (§3 pregunta 2) |
| RTO real contra el destino real | FÍSICO/PROPIETARIO | depende de lo anterior |

### P-07 — Preparación del servidor y recorridos
| Criterio | Estado | Evidencia |
|---|---|---|
| Recorridos integrales en contenedor (`run-p07.sh`, 127 chequeos) | VALIDADO (no repetido) | #317; sin cambios de código desde entonces que lo invaliden; repetirlo no aporta algo nuevo en la VM |
| `Check-ErpHealth.ps1` contra el servidor vivo (modo `-VmHost`, SSH solo lectura) | **VALIDADO HOY** | desde esta PC (otro host): HTTPS 200, contenedores, backups verificados, servicios; solo FALLA por Tailscale sin autenticar |
| `preflight-windows.ps1`, Windows Server, Docker Desktop, tareas programadas `TheBury*` | FÍSICO | equipo Windows de destino |

### P-08 — Monitoreo y operación automática
| Criterio | Estado | Evidencia |
|---|---|---|
| Agente `monitor.py` como servicio systemd (arranca solo) | VALIDADO HOY | `bury-monitor` activo tras 4 reinicios |
| Canal webhook (receptor de ensayo, dedupe por `event_id`), `firing`/`resolved` | VALIDADO HOY | `--test-channel` + incidentes reales |
| Caída controlada de Caddy / app / SQL → alerta → `resolved` | VALIDADO HOY | Caddy: `http.erp/live/ready` y `docker.caddy`; app/SQL: `kill -9` + `isolated_smoke.py` (CI) |
| Disco lleno (70/85/90 %, GiB libres) | **VALIDADO HOY** | §2.2: corregida la tormenta de alertas |
| **Observador EXTERNO** (otro host) detecta la caída | **VALIDADO HOY** | Uptime Kuma 2.5.5 en esta PC con monitores HTTP a `https://tbp/health/{live,ready}` por la LAN: **DOWN** a los ~76 s de detener Caddy (`ECONNREFUSED`), **UP** al volver. `scripts/qa/p08-observador/kuma_observador.py` |
| Heartbeat *push* del agente a Kuma (detección de agente muerto) | **VALIDADO HOY (mecanismo, Kuma en la propia VM)** | `monitor.py` envía `UP "collection completed"` cada ciclo; con el agente detenido ~4 min Kuma marca **DOWN** ("No heartbeat in the time window"); al reiniciarlo vuelve a **UP**. `scripts/qa/p08-observador/kuma_observador.py push`. Falta repetirlo con Kuma **en otro sitio** (el firewall de Windows impide que la VM alcance a Kuma en esta PC; no se tocó) |
| Aviso humano (correo/Telegram/WhatsApp) y receptor real | PROPIETARIO | canal y destinatario (§3 pregunta 3) |
| Caída total del host/energía/router avisando desde otro sitio | FÍSICO/PROPIETARIO | requiere observador en otro sitio y un corte real |
| Umbrales de CPU/RAM/disco con semanas de carga real | PRODUCCIÓN | histórico real |

### D-05 — CSP
| Criterio (`csp-enforce-activacion.md` §3) | Estado | Evidencia |
|---|---|---|
| G3 circuito de reporte: centinela visto en el log | VALIDADO HOY (sintético) | `img-src` de `csp-centinela.invalid` registrada como `CSP violation … disposition=report` |
| G8 humo automatizado (`csp-humo-lectura.js`) | VALIDADO HOY (sintético) | `reportonly` y `enforce`: 7 pantallas 200, SignalR conectado, **0 violaciones**, 0 errores de página, sobre Docker + Caddy real |
| G9 respaldo y copia del `.env` previos | VALIDADO HOY (procedimiento) | `.env` guardado 0600 fuera del repo, backup verificado |
| G10 reversión ensayada ≤ 5 min | VALIDADO HOY | `Enforce` → `ReportOnly`: **29 s** de punta a punta (archivar log + cambio + humo); cabeceras correctas en cada modo |
| Modo original restaurado | VALIDADO HOY | la VM quedó como estaba (`Csp__Mode` por defecto) |
| **G1** ≥ 10 días hábiles de uso real con cierre/inicio de mes | PRODUCCIÓN | una observación sintética **no** cuenta |
| G2 0 violaciones propias en todo el período · G4 12 áreas por rol real · G5 navegadores/equipos reales · G6 SignalR en cada navegador real · G7 impresión/descargas reales | PRODUCCIÓN | uso real, personas y equipos reales |
| G11 autorización escrita y ventana | PROPIETARIO | autorización |

Script: `scripts/qa/d05/csp-vm-ensayo.sh`. Pasar a Docker/Caddy reales **reinicia** la evidencia de G5/G6 (cambia el origen).

## 2. Hallazgos y correcciones de esta tanda

1. **2.1 — Sin DHCP tras un corte (VMware, red puenteada por Wi‑Fi).** Tras el apagado hard, `ens33` quedó 9+ min sin lease ("Activation failed … ip-config-unavailable"; también explica las demoras de reconexión de los reinicios anteriores). Se fijó IP estática en netplan (192.168.50.162/24, gateway 192.168.50.1) y volvió al instante. Un servidor físico debe usar **IP fija o reserva DHCP** y cable; documentado en `instalacion-servidor.md`.
2. **2.2 — `monitor.py`: 9 alertas por un solo disco lleno** (corregido). `/`, `DockerRootDir`, `BACKUP_DIR` y los 7 volúmenes comparten filesystem y emitían cada uno `disk.*` y `disk.action.*`. Ahora se conserva una señal por dispositivo (`st_dev`) con el detalle "same filesystem as N other monitored path(s)". +2 tests (39 en total). Verificado en la VM.
3. **2.3 — `monitor.py`: `tls.expiry` crítico permanente** con certificados de 12 h de Caddy (corregido en #326; desplegado).
4. **2.4 — Alertas de claves retiradas quedan abiertas.** Al quitar `offsite-*` de `backup_windows`, sus alertas previas siguen `firing` en el receptor (una muestra ausente nunca resuelve, por diseño). Operativa: resolverlas a mano en el receptor o recrear el estado. Documentado.
5. **2.5 — `IDENTITY` salta ~1000 tras una caída de SQL** (cliente id 2 → 1002 tras el `kill -9`): comportamiento conocido del *identity cache* de SQL Server 2017+. Sin pérdida de datos; los números visibles de venta/factura/comprobante no usan esa columna. Si el propietario quiere evitar los saltos: `ALTER DATABASE SCOPED CONFIGURATION SET IDENTITY_CACHE = OFF` (costo menor de rendimiento). **No se aplicó** (§3 pregunta 5).
6. **2.6 — Trampas de herramientas** (para quien repita): `UPTIME_KUMA_DB_TYPE=sqlite` evita el asistente de Kuma 2.x; `uptime-kuma-api` 1.2.1 no envía `conditions` (Kuma 2.x lo exige); el cliente de Tailscale en contenedor necesita `TS_USERSPACE=false`; el cliente Windows de `curl` con CA interna necesita `--ssl-no-revoke`; `echo pw | sudo -S` consume el *stdin* (usar `bash -c`); el arnés de independencia se niega a correr en el checkout real; **dos `systemctl restart docker` seguidos** dejan los contenedores caídos.

7. **2.7 — Sin `--netfilter-mode=nodivert`, la capa de host no ve la tailnet** (corregido en doc y mensaje del bootstrap). `tailscaled` pone `ts-input`/`ts-forward` antes que `ufw`
   y `DOCKER-USER`: el SSH del administrador quedó abierto **sin ninguna regla `ufw`** y el contador de `BURY-ERP` para `tailscale0` no se movió tras 5 solicitudes HTTPS; con `nodivert`
   el contador sube (167 → 243) y `ufw` decide. La documentación afirmaba "las dos capas deben coincidir" y dejaba "a verificar en la VM real" si `ts-forward` iba antes que `DOCKER-USER`:
   está verificado. Comando correcto: `tailscale up … --netfilter-mode=nodivert` (o `tailscale set --netfilter-mode=nodivert`). **Pendiente en producción:** repetirlo con la tailnet real.

## 3. Decisiones del propietario (agrupadas)

1. **Tailscale real.** ¿Cuenta/tailnet **nueva solo para el ERP** (recomendado: no toca la de la Raspberry Pi) o la existente con `tag:erp` y `grants` acotados (riesgo de interferir con la política actual)? Hace falta el correo de las dos personas, quién administra la tailnet y una clave de pre-autenticación **con etiqueta** (no la daré yo: la genera el administrador).
2. **Copia externa.** Destino en otro sitio (SFTP propio —¿la Raspberry Pi en otro lugar?—, disco externo/NAS, B2/S3 con costo), quién custodia la password `crypt`, el recovery package y si se activa `BACKUP_CA_KEY=seal`.
3. **Avisos.** Canal humano (correo/Telegram), y dónde corre el observador externo (¿la Raspberry Pi con Kuma, sin costo, por Tailscale?).
4. **SSH.** El acceso quedó limitado a la IP de esta PC (DHCP): ¿se pasa a la IP de Tailscale al autenticarla y se deshabilita la contraseña de SSH (solo clave)?
5. **SQL.** ¿Desactivar `IDENTITY_CACHE` para evitar saltos de id tras una caída? (impacto funcional nulo; costo de rendimiento menor).
6. **Plataforma definitiva** (Windows Server + Hyper-V / Windows 11 / Ubuntu) y presupuesto de UPS.
7. **P-03:** cuentas reales a crear, destino de `vendedor2` y los límites de nombres (SoloLetras 20/6).
8. **D-05:** autorizar la ventana real de `ReportOnly` (10 días hábiles con cierre de mes) y quién es Responsable/Verificador/suplente.

## 4. Qué sigue ejecutable sin ustedes
Repetir el reinicio con Tailscale **real** cuando exista (§3.1); drill contra el destino offsite real (§3.2); heartbeat *push* cuando Kuma esté en su sitio definitivo; `run-p07.sh` en la VM si cambia el código relevante.

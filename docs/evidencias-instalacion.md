# Evidencias de instalación y recuperación: vigentes e históricas

Actualizado: 2026-11-27. Base de referencia: `main` `799676e1` (145 migraciones) más las correcciones de firewall y vuelta atrás que acompañan
esta versión del documento.

**Vigente** = sirve para sostener la guía actual. Vigente no significa probado sobre el commit actual: cada fila indica sobre cuál se midió.
**Histórica** = se conserva como registro; no usarla para afirmar que algo funciona hoy.

## Laboratorio del 2026-11-27 (VM Ubuntu)

Ubuntu Server 24.04.5 (imagen cloud oficial, suma verificada) en una VM KVM con systemd y reinicios reales, Docker Engine 29.10.1, 4,5 GB de RAM
y 6 CPU. Tres redes: administración (SSH), una LAN autorizada y una red no autorizada, con cada cliente en su propio espacio de red. Datos
sintéticos. **No es hardware físico** y no es la notebook, que no se modificó. «Corrección» = código que acompaña esta versión, llevado a la VM
por `git bundle` porque aún no estaba publicado.

| Qué | Resultado | Sobre |
|---|---|---|
| Instalación desde Ubuntu limpio siguiendo [instalacion-servidor.md](instalacion-servidor.md) §4.1–4.5 | 145 migraciones, health 200, cron; autorizado 200 y ajeno sin respuesta; 1433/8080 cerrados; `verificar-servidor.sh` 20 PASS, 0 FAIL, 4 WARN tras cerrar el crédito; `backup.sh all` y `restore-test.sh` OK. **Hallazgos:** sin swap, la compilación agotó la memoria; antes de cerrar el crédito hay un `FAIL V07` | `799676e1` |
| Firewall **anterior**, en la instalación real | **Falla abierto:** configuración inválida + reinicio → el ajeno recibe HTTP 200 y el ERP queda sano. `systemctl stop bury-firewall` → 200. En un reinicio normal, 18 `SYN-ACK` al ajeno durante el apagado. Repetir el bootstrap no aplicaba redes nuevas | `799676e1` |
| Pasar al firewall nuevo con el ERP en marcha | 0 conexiones del ajeno; sin reiniciar Docker ni contenedores | corrección |
| Firewall nuevo con el ERP: reinicio de Docker y del equipo, unidad detenida, redes agregadas y quitadas, `ufw reload`, corte en seco de la VM | 0 conexiones no autorizadas; el autorizado recupera el acceso; HTTPS saliente desde la red del ERP OK | corrección |
| Configuración inválida + reinicio del equipo | Docker `failed`, nadie escucha el 443 (tampoco tras intentar activarlo por `docker.socket`), SSH disponible; recuperado con los comandos del manual | corrección |
| `test-firewall-failclosed.sh`: bootstrap y systemd reales, fallos inducidos (incluidos un bootstrap fallido y reglas desordenadas), arranque en frío, segunda instalación | 62 PASS, 0 FAIL. Control: con el firewall anterior falla (al menos 25 comprobaciones; la corrida se cortó por tiempo) | corrección |
| Recuperación en una **máquina nueva** ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §6) con la original apagada: repositorio, paquete, passphrase, claves SFTP y copia externa cifrada | `recover OK`: 145 migraciones, `CHECKDB` OK, dato posterior al full recuperado con logs, archivo subido, CA idéntica, login, copia externa reanudada; `verificar-servidor.sh` 23 PASS, 0 FAIL, 3 WARN | corrección |
| Actualización con migraciones (§2): `c442b380` (137) → 145 | Datos y archivo conservados; respaldo previo y `restore-test` OK | corrección |
| Vuelta atrás restaurando la base (§5) con `rollback.sh` y `backup.sh` **anteriores** | La secuencia termina en 137, pero el primer `backup.sh all` **volvió a migrar a 145** | anterior |
| Ídem con `rollback.sh` y `backup.sh` corregidos | Sigue en 137 tras respaldo con scripts viejos y nuevos, `compose stop`/`start` y `restart`; health OK | corrección |

El destino externo de la recuperación fue un servidor SFTP **descartable dentro del laboratorio** (host key fijada, `crypt`): prueba el
procedimiento, no un destino físico concreto. La opción B de §6.4 (disco extraíble) no se repitió; rige la evidencia anterior.

## Vigilancia del firewall y ensayo de actualización (VM Ubuntu **26.04.1**)

Misma versión de sistema que la notebook; Docker 29.10.1 (la notebook tiene 29.10.0). Contenedores sintéticos, **sin el ERP**: no se repitió el
recorrido integral.

| Qué | Resultado |
|---|---|
| `test-firewall-watch.sh`: reglas borradas, vaciadas, desordenadas; `iptables -F` y `-t nat -F`; segunda pérdida; configuración rota con reglas intactas y alteradas; parada manual; reinicios y redes nuevas | 84 PASS, 0 FAIL. Antes: 6 FAIL por un defecto de la prueba (aceptaba una recuperación anterior del historial); 6 y luego 3 FAIL reales al sumar los casos de las dos revisiones (con la configuración rota aceptaba un `ACCEPT` previo, un origen alterado o un `ACCEPT` con el comentario de la barrera; corregido). Ventana observada de acceso del ajeno: 8–11 s |
| `test-firewall-failclosed.sh`, primera vez sobre 26.04 | 62 PASS, 0 FAIL |
| Ensayo de [actualizacion-notebook-firewall.md](actualizacion-notebook-firewall.md): firewall anterior con el mismo SHA-256 que el de la notebook, dos instancias, `ufw` activo | Comprobación previa sin cambios; instalación con 0 conexiones del ajeno (94 sondeos) y sin reiniciar Docker ni contenedores; exclusión de un cliente real; reposición; reinicio con 0 conexiones (352 sondeos); vuelta al firewall anterior con el binario exacto; `guard` fallido sin tocar las reglas. Encontró que `ack` abortaba a mitad (corregido) |

No ensayado: el ERP y sus datos, el monitor como servicio, el observador instalado en la PC y el hardware de la notebook.

## Notebook: estado observado (solo lectura, 2026-11-27 23:06–23:10)

Arranque 22:57:57 tras un apagado **ordenado** a las 22:53 (batería agotada, según el observador). Con corriente, batería 14 % y cargando.
Docker, `cron`, `ssh`, `ufw`, las dos unidades de firewall y el monitor activos; seis contenedores de servicio sanos; `/health/ready` 200 en
443 y 8443. Sin marcas de respaldo o recuperación interrumpidos y con los logs de error de cron vacíos. Copias de logs al día en las dos
instancias; la de las 23:07 de la provisional, verificada y recibida en la PC. Full y archivos del 27/11 presentes. Copias locales cifradas
verificadas por última vez a las 03:15 del 27/11. `verificar-servidor.sh` sin `sudo`: 16 PASS, 0 FAIL, 1 WARN, 1 SKIP (firewall no verificable
sin root). El WARN `V19` («no hay full») es **falso**: el usuario no puede listar la carpeta `sql`; desde el contenedor se ven los full.
El firewall instalado es el anterior (**falla abierto**).

## Copia externa y custodia de la instalación provisional

Contraste del 2026-11-27 **sin conectarse a la notebook** (archivos de la PC administradora, su observador y la documentación):

| Punto | Estado y evidencia |
|---|---|
| Copia **fuera del servidor** | **Activa** desde el 2026-11-26 hacia la PC administradora ([backup-restore.md](backup-restore.md) §6.7, PR #379; envío, verificación, restauración desde el destino y corte real ya probados, no se repitieron). El 27/11 seguía llegando: logs cada 15 min y la copia diaria de las 03:00 |
| Copia **fuera del local** | **No hay.** La PC está en el mismo lugar |
| Paquete de recuperación fuera del servidor | Hay copia en la PC; la suma del vigente (27/11) coincide. No se abrió |
| Passphrase fuera del servidor | **Sin evidencia.** El último registro (26–27/11) indica que estaba solo en la notebook y que la custodia externa no se verificó. Si sigue así, perder la notebook deja ilegible el paquete y, con él, las copias de la PC |
| Recuperación desde **ese** destino en una máquina nueva | **No ensayada** (el mecanismo SFTP + `crypt` sí, en el laboratorio) |

Esto reemplaza la afirmación anterior de este documento de que no había ninguna copia externa activa: era incorrecta desde el #379.

## Otras evidencias vigentes

| Qué | Dónde y sobre qué | Cuándo | Fuente |
|---|---|---|---|
| Aislamiento de reglas del firewall (18 PASS), modelo de acceso (44), política Tailscale (131), Caddy TLS (14) | Runner Ubuntu de CI, `43dc7bcf`. El modelo de acceso usa capturas sintéticas | 2026-11-27 | CI, job `network-security` (run 46050160872) |
| Ubuntu 26.04 físico: bootstrap, `install.sh`, cron, solo 443 expuesto desde otro equipo | Notebook, `ab27a838`, **firewall anterior** | 2026-11-24 | [notebook-laboratorio-independiente.md](notebook-laboratorio-independiente.md) §4 |
| Reinicio completo y corte brusco: Docker, cron y contenedores vuelven; `CHECKDB` OK. Lo que decía del firewall queda superado por el laboratorio de arriba | Ídem | 2026-11-24 | Ídem |
| Recuperación en pila nueva desde paquete cifrado + disco extraíble; datos sintéticos, 47/47 | Ídem; misma máquina física | 2026-11-24 | Ídem §4 y [backup-restore.md](backup-restore.md) §10 |
| Segunda instalación y ensayo de traslado desde paquete + extraíble | Notebook, tag `servidor-provisional-20261125` | 2026-11-25 | [servidor-provisional-notebook.md](servidor-provisional-notebook.md) §3 y §6 (ver la corrección de `--no-deploy`) |
| 8 migraciones entre 137 y 145 sobre una copia de una base de desarrollo; datos previos conservados | LocalDB en Windows, `ea7229d8` | 2026-11-27 | Regularización del entorno local |
| Base local nueva: con la base inexistente `--migrate` falla; con una vacía aplica 145 migraciones (8 usuarios de prueba en Development) | LocalDB, `43dc7bcf` | 2026-11-27 | [desarrollo-local.md](desarrollo-local.md) |
| `iniciar-erp-local.ps1`: 28/28; verificación contra LocalDB real y arranque completo sobre una copia. Las 28 pruebas corren en CI desde esta versión | Windows, `43dc7bcf` | 2026-11-27 | `scripts/test-iniciar-erp-local.ps1` |

## Históricas

| Documento | Por qué ya no es vigente |
|---|---|
| [p04-p08-preproduccion-ubuntu-vm.md](p04-p08-preproduccion-ubuntu-vm.md) y la matriz de la VM `tbp` | La VM quedó fuera del plan el 2026-11-24 (144 migraciones) |
| Validación de WSL2 + Docker Desktop del 2026-10-03 | 115 migraciones |
| Ensayo de recuperación del 2026-10-29 | 133 migraciones, Docker Desktop. Sus tiempos no son medidas actuales |
| [estado-software-pre-instalacion.md](estado-software-pre-instalacion.md) | Foto del 2026-11-19 |
| [preparacion-operativa-2026-11-05.md](preparacion-operativa-2026-11-05.md) | Foto del 2026-11-05; el arranque local actual está en [desarrollo-local.md](desarrollo-local.md) |
| [red-local-vpn.md](red-local-vpn.md) | Registro del 2026-09-22 |

## No validado

- Ubuntu **26.04** y hardware físico con estas correcciones: lo físico más reciente es del 24–25/11 y usa el firewall anterior.
- La notebook provisional sigue con el firewall y los scripts anteriores (última evidencia): no se modificó.
- Instalación con 8 GB de RAM sin swap.
- Recuperación en otra máquina **física**, y desde el destino real de la PC administradora.
- Vuelta atrás de **archivos subidos**; vuelta atrás entre otras versiones o con volúmenes de datos grandes.
- Actualización de una instalación con datos comerciales reales.
- Clientes remotos reales por VPN o Tailscale, y el corte de energía real con UPS.
- Detección de reglas borradas con Docker en marcha: ningún monitor incluido lo avisa.
- `install-windows.ps1` con efectos reales sobre un servidor de destino.

# Evidencias de instalación y recuperación: vigentes e históricas

Actualizado: 2026-11-27. Base de referencia: `main` `43dc7bcf` (145 migraciones).

**Vigente** = sirve para sostener la guía actual. Vigente no significa probado sobre el commit actual: cada fila indica sobre cuál se midió.
**Histórica** = se conserva como registro; no usarla para afirmar que algo funciona hoy.

## Vigentes

| Qué | Dónde y sobre qué | Cuándo | Fuente |
|---|---|---|---|
| Firewall `bury-firewall.sh` con Docker real y clientes en redes distintas: 18 PASS, 0 FAIL (incluye que sin él un origen ajeno llega al puerto publicado) | Runner Ubuntu de CI, `43dc7bcf` | 2026-11-27 | CI, job `network-security` (run 46050160872) |
| Modelo de acceso (44 PASS), política Tailscale (131), Caddy TLS (14) | Ídem. El modelo de acceso usa capturas sintéticas | 2026-11-27 | Ídem |
| `server-linux-bootstrap.sh` + `install.sh` + cron en Ubuntu 26.04 físico: 145 migraciones, health, solo 443 expuesto (medido desde otro equipo) | Notebook, `ab27a838` | 2026-11-24 | [notebook-laboratorio-independiente.md](notebook-laboratorio-independiente.md) §4 |
| Reinicio completo y corte brusco: Docker, firewall, cron y contenedores vuelven; `CHECKDB` OK | Ídem | 2026-11-24 | Ídem |
| Recuperación en pila nueva con volúmenes vacíos desde paquete cifrado + disco extraíble; datos sintéticos, 47/47 | Ídem; **misma máquina física** | 2026-11-24 | Ídem §4 y [backup-restore.md](backup-restore.md) §10 |
| Segunda instalación y ensayo de traslado desde paquete + extraíble | Notebook, tag `servidor-provisional-20261125` | 2026-11-25 | [servidor-provisional-notebook.md](servidor-provisional-notebook.md) §3 y §6 (ver la corrección de `--no-deploy`) |
| 8 migraciones entre 137 y 145 sobre una copia de una base de desarrollo; datos previos conservados | LocalDB en Windows, `ea7229d8` | 2026-11-27 | Validación de la regularización del entorno local |
| Base local nueva: con la base inexistente `--migrate` falla; con una vacía aplica 145 migraciones (8 usuarios de prueba en Development) | LocalDB, `43dc7bcf` | 2026-11-27 | [desarrollo-local.md](desarrollo-local.md) |
| `iniciar-erp-local.ps1`: 28/28 en repositorios temporales; verificación contra LocalDB real y arranque completo sobre una copia (login y `/health/ready` 200, sesiones en la base validada) | Windows, `43dc7bcf` | 2026-11-27 | `scripts/test-iniciar-erp-local.ps1` |
| `compose config`: sin `ERP_IMAGE`, `app` y `migrate` resuelven `:local` | Windows, solo configuración | 2026-11-27 | [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §0 |

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

- Instalación siguiendo **estos documentos reorganizados**, de punta a punta, en una máquina nueva.
- Instalación o recuperación sobre el commit actual de `main` en Ubuntu (lo último medido es `ab27a838` / `41775ca5`, previos a la reorganización de navegación).
- Recuperación en **otra máquina física**, y recuperación desde una **copia externa** real (hoy no hay ninguna activa).
- Custodia de claves **fuera** del servidor provisional: según la última evidencia, solo existía en la notebook.
- La restauración de la base como vuelta atrás tras una actualización, como secuencia completa.
- La actualización de una instalación con datos reales a una versión con migraciones nuevas.
- La instalación de `bury-firewall` como servicio no tiene prueba en CI (sí se observó tras reiniciar en la notebook). El caso de fallo al aplicarlo no se probó.
- Clientes remotos reales por VPN o Tailscale, y el corte de energía real con UPS.
- `install-windows.ps1` con efectos reales sobre un servidor de destino.

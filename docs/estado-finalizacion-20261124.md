# Qué falta para finalizar el ERP (2026-11-24)

Base: `main` `3c659f21` (#367 fusionado). **Decisiones del propietario vigentes:** la VM `tbp` queda fuera del plan (no se vuelve a usar); la notebook Ubuntu es el entorno independiente de pruebas hasta finalizar el ERP; no se usan datos comerciales reales.
Evidencia técnica de esta pasada: [`notebook-laboratorio-independiente.md`](notebook-laboratorio-independiente.md) §4. No se repitieron pruebas ya aprobadas ni se reabrieron módulos cerrados.

**Clasificación.** A = verificable en la notebook sin datos reales · B = requiere operación comercial real · C = contador / legal · D = servidor productivo definitivo, hardware físico o custodia externa del propietario.

## 1. Efecto de abandonar la VM sobre lo que estaba «cumplido»

Lo que la documentación daba por verificado en la VM (`theburyproject-prod`, `tbp-prod`, `tbp-respaldo-prod`, `bury-monitor-prod`, drill VM→PC, `csp-vm-ensayo.sh`, el full automático del 24/11 03:45 de la VM) **ya no es evidencia vigente de este plan**: se sustituye por lo medido en la notebook (abajo) o queda como histórico. El full del 24/11 03:45 de la VM pasa a ser irrelevante.

## 2. Estado por hito

| Hito | Cerrado técnicamente (A) en la notebook | Falta |
|---|---|---|
| **P-02 Fiscal V2** | Mecanismo y verificadores: `validacion-base-nueva.sh` 37/37, `p02-verificar-instalacion.sql` sin FALLA, V2 desde el primer arranque; ventas V2 y facturas coherentes en `run-p07` 131/131 | **B**: instante real de `--fiscal-v2-desde`, primera venta real V2 con Subtotal+IVA=Total y fecha efectiva = `MIN(FechaVenta)`. **C**: IVA de punitorio con alícuotas mixtas, neteo de IVA por NC, redondeo C1-C15, interés de financiación y recargo |
| **P-04 Servidor definitivo** | Con la notebook como anfitrión Linux: arranque sin sesión de usuario, reinicio completo (33 s), recuperación automática de Docker/firewall/cron/LUKS, corte brusco (SQL rolled forward 22/0), aislamiento (solo 443; 80/1433/2019/8080 cerrados), energía sin suspensión | **Redefinir con el propietario qué es «servidor definitivo»**: los criterios H01-H09/VMware/`vgk.sys` eran del anfitrión Windows y ya no aplican. **D**: UPS y corte de energía real, BIOS «Restore on AC Power Loss», cable Ethernet o IP reservada (hoy solo Wi-Fi/DHCP), observador externo en otro sitio |
| **P-06 Respaldos** | Backup diario/log, `verify-local-copy` V1-V5, fallo seguro, remontaje tras reinicio y tras corte, restauración aislada, `restore-test.sh`, `check-custody.sh`, **recuperación completa desde instalación limpia** con `.env` del paquete cifrado (cookie vieja válida, misma CA, `CHECKDB` OK) | **D**: respaldo independiente del anfitrión (el pendrive solo cuenta si se guarda fuera del equipo; decisión vigente: sin nube), custodia externa de las claves LUKS, passphrase del paquete de recuperación y `.env`; `BACKUP_CA_KEY=seal` (decisión) |
| **P-07 Puesta en marcha** | `install.sh` 145 migraciones + health + identidad de imagen, `instalacion-limpia.sh` OK, 3 verificadores SQL (crédito cerrado y P-02 sin FALLA; cuentas con FALLA solo por la cuenta sintética de laboratorio), `run-p07` 131/131 (incluye crédito → contrato → cobros, antes NO VALIDADO), crédito cerrado por pantalla, punitorio 0 %/30 d/gracia 12 cargado por pantalla | **B**: ≥ 2 SuperAdmin reales (`p07-verificar-cuentas.sql @Cierre=1`), datos de empresa/cajas/orígenes/medios de pago/envío y armado reales, venta de contado real con devolución y cierre de caja cuadrado, instalación en el servidor definitivo. **C/legal**: cláusula de mora y plantillas de contrato ya cargadas |
| **D-05 CSP** | Política, 0 violaciones con `Enforce` (7 roles × 3 tamaños, sintético), activación `ReportOnly` y reversión medidas en la notebook (5,2 s / 4,9 s), centinela por Caddy 204 con y sin `Content-Length` (riesgo R2 medido) | **B**: 10 días hábiles de observación con uso real, archivado diario con responsable y suplente, G4 (12 áreas por rol real), autorización escrita para `ReportOnly` y para `Enforce`. **D**: Safari/iOS/móvil físico, impresoras y navegadores reales. La ventana **no empezó** |

## 3. Pendientes del propietario (decisiones)

1. Definición de «servidor definitivo» y fecha de corte (P-04) y si la notebook sigue como preproducción permanente.
2. Aceptar por escrito o mitigar la falta de respaldo independiente del anfitrión; dónde se guardan las claves y el pendrive.
3. Identidad de los 2 SuperAdmin; datos de empresa; sucursales; cajas y orígenes de fondos; medios de pago con recargos; costos de envío y armado; método de costeo; instante fiscal V2.
4. Autorización de `ReportOnly` y, después, de `Enforce`.
5. Contador: IVA de punitorio, NC, redondeo, financiación. Legal: cláusula de mora.
6. Apertura o descarte del crédito personal (hoy cerrado por decisión).

## 4. Riesgos y deuda detectados en esta pasada

* La copia del extraíble pierde el dueño uid 10001 de `sql/` (se documentó el `chown` en `backup-restore.md` §10; el procedimiento no está automatizado).
* `ufw` permite SSH solo desde una IP; con DHCP puede cambiar y dejar la notebook sin acceso remoto.
* La instancia de laboratorio guarda en la misma notebook la clave LUKS, el paquete de recuperación (en el pendrive) y su passphrase (`~/.bury-custodia/`): **no hay custodia externa**.
* El runner `run-p07.sh` no estaba en CI y quedó desactualizado ante #363 hasta hoy (corregido en #367). Sigue fuera de CI.

## 5. Texto para Notion (no hay conector disponible en esta sesión: pegar a mano)

| Elemento | Estado propuesto | Respaldo |
|---|---|---|
| Infraestructura | **VM `tbp` fuera del plan (decisión 2026-11-24). Notebook Ubuntu 192.168.50.223 = entorno independiente de pruebas hasta finalizar el ERP. Sin datos comerciales reales** | `notebook-laboratorio-independiente.md` |
| Instalación notebook | Instalada y verificada técnicamente: ERP `ab27a838`, 145 migraciones, HTTPS propio, solo 443 | §4 |
| Reinicio y recuperación | Reinicio completo y corte brusco superados sin intervención; recuperación desde instalación limpia con respaldos reales demostrada | §4 |
| P-02 | En progreso: técnico listo; falta operación real (B) y contador (C) | §2 |
| P-04 | En progreso: redefinir alcance; faltan UPS/energía/red física (D) | §2 |
| P-06 | En progreso: técnico listo; falta respaldo independiente del anfitrión y custodia externa (D) | §2 |
| P-07 | En progreso: técnico listo; falta puesta en marcha con datos y cuentas reales (B) | §2 |
| D-05 | Preparado, no activado: la ventana de 10 días hábiles no empezó (B/D) | §2 |
| #367 | Fusionado: runner P-07 crea punitorios (#363); 131/131 | PR #367 |

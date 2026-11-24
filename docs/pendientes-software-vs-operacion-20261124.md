# Pendientes: finalizar el software vs. comenzar la operación productiva (2026-11-24)

Dos listas **independientes**. La primera es lo que falta para dar por **terminado el software** (se puede cerrar sin datos reales, sin contador y sin el servidor definitivo). La segunda es lo que **solo** hace falta para empezar a operar en producción: nada de ella bloquea el cierre técnico del software.
Decisiones vigentes: la notebook Ubuntu es exclusivamente el servidor de pruebas; el servidor productivo definitivo se define después; la VM `tbp` no se usa; no hay datos comerciales reales. Evidencia: [`notebook-laboratorio-independiente.md`](notebook-laboratorio-independiente.md); detalle por hito: [`estado-finalizacion-20261124.md`](estado-finalizacion-20261124.md).

## 1. Cierre técnico del software: lo ya demostrado (no se repite)

Instalación limpia y reinstalación (145 migraciones, identidad de imagen), `run-p07` 131/131 (compras, stock, caja, cotización→venta→factura, devoluciones y NC, crédito→contrato→cuotas→cobros, cierre de caja, reportes, permisos),
P-02 sobre base nueva 37/37, reinicio completo y corte brusco sin intervención, respaldos con verificación y fallo seguro, **recuperación con datos comerciales conocidos 38/38** (huella idéntica de las 141 tablas, documentos, 0 violaciones de FK, casos negativos que
se detienen sin dejar una base vacía), D-05 activación/reversión y centinela por Caddy, smoke de interfaz en Chrome sobre el sistema recuperado (48/48), pruebas de backup existentes (108 + 102 + 23 sin fallos).

## 2. LISTA A — Requisitos pendientes para finalizar completamente el software

| # | Requisito | Qué falta exactamente | Cómo se cierra |
|---|---|---|---|
| A1 | **Integración de los PR en curso** | #368 (evidencias) y los dos siguientes (limpieza de referencias a la VM; recuperación estricta + prueba) esperan la **revisión de Codex** (límite de uso agotado hasta las 20:00) y CI verde | Revisión de Codex → corrección de hallazgos → fusionar. Sin fusionar no se declara el cierre |
| A2 | **Navegador/interfaz sobre la instalación real (Docker + Caddy + HTTPS)** | Nunca corrió un navegador contra Caddy; el smoke 48/48 fue por HTTP y túnel sobre el sistema recuperado. Falta: axe-core y desborde sobre la instalación servida por Caddy (CA propia, CSP real), `https-signalr.js` (SignalR `wss`) contra `tbp-nb` | Instalar Chrome/Playwright en la notebook (o túnel + `--host-resolver-rules`) y correr `scripts/qa/a11y-responsive/crawl.js` + `scripts/qa/p07/p07-ui-smoke.mjs` + `scripts/qa/csp-roles/https-signalr.js`. Requiere usuarios sintéticos (overlay de CI): **no usar la base del laboratorio** |
| A3 | **Accesibilidad técnica no cubierta** | Recorrido de teclado global (trampas de foco, orden de tabulación, `Esc` en modales; hoy solo muestral), reflow/zoom 200 % y 400 % (WCAG 1.4.10), viewports 360×740 y 412×915 en barrido global, emulación móvil/táctil real (`isMobile`/`hasTouch`), `prefers-reduced-motion` y `forced-colors` | Escribir 1-2 specs Playwright (no existe script) y correrlos sobre la instalación sintética. Es trabajo acotado, no un rediseño |
| A4 | **Firefox y WebKit en UI funcional** | axe, desborde y E2E solo corrieron en Chromium (Firefox/WebKit solo en CSP/navegación) | `crawl.js`/`impresion.js` con `--browser firefox|webkit` |
| A5 | **CSS de impresión** | Solo se prueba que `print()` se invoca y vuelve; no se verifica el render | `page.emulateMedia({media:'print'})` / `page.pdf()` sobre cotización, factura y recibo |
| A6 | **Cierre formal de pantallas con 4 capas pendientes** | `UI-REFACTOR-STATUS.md`: Dashboard/Index y Seguridad en ◐ (aplicadas las 4 capas, sin cierre formal); pantallas sin cierre de 4 capas (Tesorería, Mora, Reportes, Devolución, Cuentas por pagar, Listas de precio, Crédito) | Cierre por pantalla con el protocolo `ui-module`; **solo si el propietario lo exige** (hoy están cubiertas por axe/CSP) |
| A7 | **Regresión automática en CI de lo que hoy es manual** | `run-p07.sh`, `run-dr-datos.sh`, el crawl axe y `https-signalr.js` no corren en CI; la regresión depende de ejecución manual (el runner P-07 estuvo roto hasta el #367 sin que CI lo notara) | **Propuesta** (no se aplica sin su autorización: cambios en workflows se consultan): job nocturno/manual con `run-p07` + `run-dr-datos` |
| A8 | **Estados con datos y modales bajo axe** | El crawl visita páginas GET; modales, drawers y estados de error con datos solo están en specs puntuales | Sembrar datos sintéticos (p. ej. con `run-p07 --keep`) y ampliar el crawl |
| A9 | **Referencias residuales a la VM en scripts** | Defaults de `setup-local-disk-copy.sh` (`tbp-respaldo`, `/mnt/tbp-pruebas`), `csp-vm-ensayo.sh`, `p02/ensayo-puesta-en-marcha.sh`, `p03_vm_produccion.py`, `p05_*_vm*`, `vm-disk-intake.py`, `monitoring/instalar-instancia.sh`: nombres con la VM como ejemplo; no rompen nada | Neutralizar nombres/comentarios en una pasada de limpieza (bajo riesgo, sin cambio de comportamiento) |

No hay defectos reproducibles abiertos en módulos cerrados. Los defectos hallados en esta etapa (runner P-07 desactualizado, permisos de la copia del extraíble, restauración fallida seguida de base vacía) están corregidos y con prueba.

## 3. LISTA B — Requisitos pendientes exclusivamente para comenzar la operación productiva

No bloquean el cierre técnico del software. Clasificación: **H** = hardware/servidor definitivo, **O** = operación comercial real, **C** = contador, **L** = legal, **P** = decisión del propietario.

| # | Requisito | Tipo |
|---|---|---|
| B1 | **Definir y adquirir el servidor productivo definitivo**; redefinir P-04 (los criterios H01-H09/VMware/`vgk.sys` eran del anfitrión Windows y ya no aplican) | H, P |
| B2 | Instalar el ERP en ese servidor con `install.sh` + `server-linux-bootstrap.sh`, **con su propia CA**, y repetir allí: reinicio, corte, recuperación (`recover.sh`) y los verificadores SQL | H |
| B3 | UPS con apagado ordenado, BIOS «Restore on AC Power Loss», red por cable o IP reservada (hoy la notebook es solo Wi-Fi/DHCP), corte de energía real | H |
| B4 | **Respaldo independiente del anfitrión** (offsite o medio extraíble guardado fuera del equipo) y su ensayo real; aceptar por escrito el riesgo si no se mitiga | H, P |
| B5 | **Custodia externa** de las claves LUKS, la passphrase del paquete de recuperación, el `.env` y la clave de la CA (`BACKUP_CA_KEY=seal`: decisión) | P |
| B6 | Observador externo de caídas en otro sitio, Telegram (token/chat_id), rotar la URL de heartbeat, Tailscale real | H, P |
| B7 | Crear **2 SuperAdmin reales**, rotar o desactivar la cuenta inicial, `p07-verificar-cuentas.sql @Cierre = 1` | O |
| B8 | Cargar datos reales de empresa (CUIT, domicilio, condición fiscal), sucursales, cajas, orígenes de fondos, medios de pago con recargos/descuentos aprobados, costos de envío y armado, método de costeo | O, P |
| B9 | Instante real de `--fiscal-v2-desde`; primera venta de contado real con Subtotal + IVA = Total y fecha efectiva V2 = `MIN(FechaVenta)`; venta con devolución controlada y un cierre de caja cuadrado | O, P |
| B10 | Reglas del contador: IVA de punitorio con alícuotas mixtas, neteo de IVA por NC fiscal, redondeo C1-C15, interés de financiación y recargo; facturación fiscal externa | C |
| B11 | Redacción legal de la cláusula de mora y de las plantillas de contrato/pagaré ya cargadas; apertura o descarte definitivo del crédito personal (hoy cerrado) | L, P |
| B12 | **D-05**: autorización escrita para `ReportOnly`; 10 días hábiles de observación con uso real (G1-G7, archivado diario con responsable y suplente); luego autorización de `Enforce` | O, P |
| B13 | Verificación con dispositivos y software físicos: Safari macOS/iOS, Android real, lectores de pantalla (NVDA/JAWS/VoiceOver), impresoras y visor PDF reales | H |
| B14 | Decisiones de alcance diferido: facturación electrónica ARCA/AFIP, cheques propios y endosos, conciliación bancaria/saldos por origen, exportación del estado de cuenta, Mercado Libre (DEF-ML-01), RMA→NC de proveedor, R-08 | P |
| B15 | Checklist de «listo para operar» (`aceptacion-final-20261123.md` §9) cumplido y evidenciado en el servidor definitivo | O, H |

## 4. Regla de lectura

Terminar la Lista A deja el **software** cerrado técnicamente (y cubre el riesgo de regresión). Terminar la Lista B es lo que convierte ese software en una **operación** productiva. Ningún ítem de B se puede cerrar desde la notebook ni con datos sintéticos; ningún ítem de A depende de B.

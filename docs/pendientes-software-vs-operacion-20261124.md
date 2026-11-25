# Pendientes: finalizar el software vs. comenzar la operación productiva (2026-11-24)

Dos listas **independientes**. La primera es lo que falta para dar por **terminado el software** (se puede cerrar sin datos reales, sin contador y sin el servidor definitivo). La segunda es lo que **solo** hace falta para empezar a operar en producción: nada de ella bloquea el cierre técnico del software.
Decisiones vigentes: la notebook Ubuntu es exclusivamente el servidor de pruebas; el servidor productivo definitivo se define después; la VM `tbp` no se usa; no hay datos comerciales reales. Evidencia: [`notebook-laboratorio-independiente.md`](notebook-laboratorio-independiente.md); detalle por hito: [`estado-finalizacion-20261124.md`](estado-finalizacion-20261124.md).

> **Vigente:** el estado actual y el índice de evidencia vigente/histórica están en [`cierre-tecnico-software-20261125.md`](cierre-tecnico-software-20261125.md). Este documento conserva la lista original; el estado de la Lista A está en §1b.

## 1. Cierre técnico del software: lo ya demostrado (no se repite)

Instalación limpia y reinstalación (145 migraciones, identidad de imagen), `run-p07` 131/131 (compras, stock, caja, cotización→venta→factura, devoluciones y NC, crédito→contrato→cuotas→cobros, cierre de caja, reportes, permisos),
P-02 sobre base nueva 37/37, reinicio completo y corte brusco sin intervención, respaldos con verificación y fallo seguro, **recuperación con datos comerciales conocidos 47/47** (huella idéntica de las 141 tablas, documentos, 0 violaciones de FK, casos negativos que
se detienen sin dejar una base vacía), D-05 activación/reversión y centinela por Caddy, smoke de interfaz en Chrome sobre el sistema recuperado (48/48), pruebas de backup existentes (108 + 102 + 23 sin fallos).

## 1b. Estado de la Lista A tras la segunda etapa (2026-11-24, `main` d067f3e2 + PR de cierre)

| # | Estado | Evidencia | PR | Pruebas |
|---|---|---|---|---|
| A1 | **Hecho** | #367, #368, #369, #370 fusionadas con revisión de Codex y CI verde | #367-#370 | CI + Codex |
| A2 | **Hecho** | Pila Docker + Caddy `tls internal` + CSP Enforce; axe/desborde por HTTP (7 roles) y por HTTPS a través de Caddy; WSS de SignalR, en Chromium, Firefox y WebKit | esta PR | `docs/qa-ronda-interfaz-20261124.md`; 135+52 páginas × 3 viewports × 3 navegadores, 0 reglas axe |
| A3 | **Hecho** | `a11y-global.js`: teclado, foco, zoom 200/400 %, 360×740 y 412×915 con toque, reduced-motion, forced-colors; 4 defectos reales corregidos | esta PR | 0 fallos en los 3 navegadores |
| A4 | **Hecho** | Mismas suites en Firefox y WebKit | esta PR | idem |
| A5 | **Hecho** | `impresion-render.js`: render `print` a A4 y PDF real (cotizaciones, facturas) + PDF servidos (recibos/documentos) | esta PR | 32/32 Chromium, 24/24 Firefox y WebKit |
| A6 | **Hecho (cierre técnico por evidencia)** | Dashboard y Seguridad pasan a ✅; Tesorería, Mora, Reportes, Devolución, Cuentas por pagar, Listas de precio y Crédito se agregan a `UI-REFACTOR-STATUS.md`. Se reutilizaron los cierres previos y se agregó evidencia renderizada (5 viewports, axe, capturas). **No se ejecutó de nuevo el pipeline `/ui-module` (ux-heuristics + impeccable) en las 7 pantallas nuevas: no hubo rediseño** | esta PR | `cierre-pantallas.js` 0 fallos en los 3 navegadores |
| A7 | **Hecho** | Workflow **solo manual** `regresion-manual.yml` (`p07`, `dr-datos`, `ui-stack`, `todo`); sin cron ni disparo por push | esta PR | validado por CI (sintaxis) y por ejecución local en la notebook |
| A8 | **Hecho** | Modales y drawers con datos sembrados abiertos bajo axe (32 por navegador), estados 404/403/login inválido | esta PR | `a11y-global.js` |
| A9 | **Hecho** | Defaults de la VM como *fallback* + variables de entorno; comentarios neutralizados; sin renombrar archivos | esta PR | `bash -n`, `py_compile`, `test-hardening` 108/0 |
| A10 | **Hecho** | `CAP_DELETE_SH` propaga el fallo del `rm` (rc 4 → `capacity_run` rc 7, sin declarar éxito) | esta PR | `test-retention-capacity.sh` 105/0 (2 casos nuevos fallan con el código anterior) |

El cierre técnico del software se declara **solo** cuando esta PR esté fusionada con la revisión de Codex y el CI verde.

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
| A10 | **Retención con `rm` fallido enmascarado** (hallazgo de Codex, preexistente, fuera del alcance de la recuperación) | `scripts/backup/lib-capacity.sh` (`CAP_RETENTION`): un `rm` que falla queda tapado por el `echo` final y `capacity_run` puede informar éxito sin haber liberado el espacio necesario | Propagar el fallo del `rm` y cubrirlo en `test-retention-capacity.sh` (bajo riesgo) |
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

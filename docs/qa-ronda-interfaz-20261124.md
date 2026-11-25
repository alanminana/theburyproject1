# Ronda de interfaz sobre Docker + Caddy + HTTPS (2026-11-24)

Cierra A2, A3, A4, A5, A8 y la parte de evidencia de A6 de [`pendientes-software-vs-operacion-20261124.md`](pendientes-software-vs-operacion-20261124.md).
Reproducible con **una sola orden** (en Linux con Docker; no instala nada en el host):

```bash
bash scripts/qa/ui-stack/run-ui-stack.sh --out <dir>          # todo, 3 navegadores
bash scripts/qa/ui-stack/run-ui-stack.sh --suites global,cierre --browsers firefox --keep   # parcial
```

## Qué levanta
Una pila **descartable** `buryp07<hex>` (proyecto Compose, red, volúmenes, puertos y secretos propios; **no toca la base ni la instalación del laboratorio**):
SQL Server real + app + **Caddy `tls internal`** (CA propia, `ERP_DOMAIN=localhost`), `Csp__Mode=Enforce`, usuarios sintéticos del overlay de CI
(7 roles) y datos sembrados por los recorridos de P-07 (clientes, compras, stock, cotizaciones, ventas, facturas, devoluciones, crédito con cuotas, caja).
Los navegadores corren en el contenedor oficial `mcr.microsoft.com/playwright:v1.59.1-noble` (Chromium, Firefox y WebKit; `--network host`).
La pila y la imagen se destruyen al terminar. El sembrado repite P-07 (131/131) solo como fuente de datos.

## Suites (`scripts/qa/ui-stack/` + las existentes)
| Suite | Qué comprueba | Criterio |
|---|---|---|
| `https-signalr` (existente) | Página por HTTPS, `wss://…/hubs/notificaciones` abierto y estable | 0 violaciones CSP, 0 errores |
| `crawl` (existente, ahora `--browser` y HTTPS) | axe (WCAG 2.x A/AA + buenas prácticas), desborde, 4xx/5xx, CSP; 7 roles por HTTP y administrador **a través de Caddy** (HTTPS), 3 viewports | 0 reglas axe, 0 desbordes, 0 5xx |
| `a11y-global.js` (nueva) | Teclado (Tab sin trampas, foco visible, indicador), modales/drawers abiertos con axe, Esc, foco, confinamiento en `aria-modal`; 404, acceso denegado y login inválido con axe; reflow a 200 % (640×512) y 400 % (320×256); 360×740 y 412×915 con `hasTouch` (`isMobile` salvo Firefox) y menú por toque; `prefers-reduced-motion`; `forced-colors` | 0 fallos |
| `cierre-pantallas.js` (nueva) | Dashboard con los 7 roles (todo acceso visible debe abrir para ese rol), Seguridad (pestañas, detalle, edición, errores, denegado), y las pantallas sin cierre formal (Tesorería, Mora, Reportes, Devolución, Cuentas por pagar, Listas de precio, Crédito) a 5 viewports con axe, desborde y capturas | 0 fallos |
| `impresion-render.js` (nueva) | `emulateMedia(print)` a ancho A4 sobre cotizaciones y facturas descubiertas desde los detalles: controles ocultos, sin desborde, fondo blanco, importes presentes; en Chromium `page.pdf()` real (firma `%PDF`, 1–3 páginas); los PDF servidos por la app (recibos/documentos) se descargan y validan (firma, `%%EOF`, páginas) | 0 fallos |

Las escrituras (`POST/PUT/DELETE`) se abortan en las suites que abren modales: solo se **abre**, nunca se confirma.

## Defectos reales hallados y corregidos (todos reproducidos antes de tocar código)
| # | Defecto | Dónde | Corrección | Cobertura |
|---|---|---|---|---|
| 1 | `role="tab"` dentro de un `<nav>` bajo `role="tablist"` (axe **crítico** `aria-required-children`/`aria-required-parent`) en el modal Nuevo/Editar producto | `Views/Catalogo/Index_tw.cshtml` | `<nav>` → `<div role="presentation">` | crawl/global (axe con el modal abierto) |
| 2 | «Ver alertas» del Dashboard se decidía con `cotizaciones.view` y llevaba a *Acceso denegado* a gerente, cajero, técnico y contador | `Views/Dashboard/Index.cshtml` | solo se ofrece con `stock.viewalerts`; va a Catálogo solo si además hay `productos.view` | `DashboardVerAlertasUiContractTests` + `cierre-pantallas.js` |
| 3 | Drawers de Cliente (Nuevo cliente, Asignar garante) con `aria-modal="true"` pero sin confinar el foco: Tab salía del diálogo | `wwwroot/js/cliente-modal.js`, `cliente-garante.js` | trampa de foco (Tab/Shift+Tab ciclan dentro) | `a11y-global.js` |
| 4 | Medios de pago: el foco de teclado de botones y campos se señalaba solo con un cambio sutil de borde/fondo (`outline: none`), sin el contraste 3:1 de WCAG 1.4.11 | `Views/ConfiguracionPago/MediosPago_tw.cshtml` | `:focus-visible` con contorno de 2 px del color de acento | `a11y-global.js` (Firefox medía 41/70 sin indicador) |

Falsos positivos descartados y corregidos **en el spec** (no en la app): «Notas rápidas» es un diálogo no modal (el Tab puede salir; solo se exige confinamiento a `aria-modal="true"`); contenedores vacíos de modales cargados por AJAX; carrera en el login inválido de WebKit; login con la ruta de solo lectura ya activa.
Una observación aislada de axe (`label-content-name-mismatch` en `EditUsuario` a 768 px, solo Firefox, 1 de 8 mediciones de la misma URL) **no se reproduce**; se espera a `document.fonts.ready` antes de axe y no reapareció.

## Límites (no cubiertos por esta ronda)
- Safari real y móvil físico (WebKit de Playwright no equivale a Safari/iOS), lectores de pantalla (NVDA/JAWS/VoiceOver), impresoras y visor PDF reales: son B13 (requieren dispositivos).
- Objetivos táctiles < 24 px (WCAG 2.5.8): se **informan** (5 en Venta/Index y Venta/Create a 360/412) sin fallar la corrida; no se rediseñó.
- El zoom se emula por viewport equivalente (criterio estándar de reflow), no con el zoom del navegador.

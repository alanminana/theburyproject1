# CSP — qué falta antes de activar `Enforce` y defectos previos registrados

**Estado (2026-11-09):** `Csp:Mode=Off` por defecto. `ReportOnly` medido en clon: 0 violaciones en 161 páginas recorridas (admin, Chrome) y en las pantallas con datos sintéticos (`scripts/qa/csp-sintetico/`, PR #270). **No se activa `Enforce` hasta cerrar la lista de abajo.** Cada ítem es una prueba, no un cambio de código.

## Pruebas pendientes (orden de prioridad)

| # | Prueba | Cómo | Criterio de cierre |
|---|---|---|---|
| 1 | **Suite E2E existente con CSP en `ReportOnly`** | **Instrumentado en CI:** `docker-compose.ci.yml` levanta la app con `Csp__Mode=ReportOnly` y el paso `Run Playwright tests` corre con `scripts/qa/csp-medicion/preload.js` (engancha `securitypolicyviolation`, `pageerror` y consola CSP en cada contexto, sin tocar los specs). El paso «Resumen de violaciones CSP» publica el conteo (resumen del job + artefacto `qa-evidence/e2e/csp/`) y falla si la medición no se enganchó (0 contextos). **Gate desde 2026-11-10**: falla con más de 0 violaciones (`resumen.js --max-violaciones 0`). | 0 violaciones en todos los specs del job `Playwright Tests` (ventas, caja, crédito, cotizador, clientes, mora). Resultado: ver el resumen del job de la PR que lo introdujo |
| 2 | **Contenido cargado por AJAX / `innerHTML`** | Abrir cada modal/parcial remoto (Producto edit, Seguridad, Proveedor, Cliente, Catálogo) y las vistas armadas por JS (cotizador, carrito) con el medidor de violaciones activo | 0 violaciones; ningún `style`/`onclick` generado en cadenas JS |
| 3 | **Roles distintos del admin** | Recorrer Vendedor, Cajero, Contador, Repositor, Técnico, Gerente | 0 violaciones y mismas pantallas que `RolPermisos` |
| 4 | **Páginas sin layout, impresión y exportes** | `Cotizacion/Imprimir`, `Venta/ComprobanteFactura`, recibos, tickets, reportes (HTML/PDF/Excel) | Botones Imprimir/Volver funcionan (`data-action`), 0 violaciones |
| 5 | **Estados que el clon no tenía** | DocumentoCliente vacío (`click-id`), pestaña «ver» de Unidades, Borrador de ML, confirmaciones de ML en modo real (`modoSimulacionMensajes=false`) | Cada interacción verificada en `scripts/qa/csp-sintetico/` ampliado |
| 6 | **Otros navegadores** | Firefox y Safari/WebKit (nonce, `style-src-attr`, `script-src-attr`) y móvil real | 0 violaciones; mismas interacciones |
| 7 | **Proxy / despliegue** | Caddy + HTTPS: `connect-src 'self'` con WebSocket de SignalR, fuentes de Google, `frame-ancestors` | SignalR conecta; nada bloqueado en la consola |
| 8 | **Ventana en `ReportOnly` en uso real** | `Csp__Mode=ReportOnly` en el servidor durante un período acordado; revisar el WARN `CSP violation` (`/csp-report`, con rate limit) | 0 violaciones durante el período; recién entonces proponer `Enforce` |

Reglas: `Enforce` se habilita por configuración (`Csp__Mode=Enforce`), nunca por defecto; se prueba primero en un clon; la reversión es volver a `Off`/`ReportOnly` sin redeploy de código. Funciones llamables por `data-*` están en la lista cerrada de `wwwroot/js/csp-actions.js` (un test impide agregar una sin registrarla).

## Defectos previos registrados (no causados por la migración de handlers, no corregidos en #270)

### DEF-UNI-01 — Unidades: clic fuera del modal «Gestionar» no lo cierra — **CORREGIDO (2026-11-09)**
- **Dónde:** `Views/Producto/Unidades.cshtml`, `#modal-manage`.
- **Causa:** el contenedor interno `min-h-full flex items-center justify-center p-4` cubre todo el fondo; `event.target` nunca era el overlay, así que `call-self` (misma condición `target === this` del `onclick` original) no se cumplía. Era previo a D-05.
- **Corrección:** el contenedor interno lleva también `data-action="call-self" data-fn="puCloseModal"`; cierra solo si el clic cae sobre él (fondo), no sobre la tarjeta ni sus controles. Test de contrato `Unidades_ModalGestionar_CierraConClicEnElFondoInterior`.
- **Validación (clon + datos sintéticos, CSP `ReportOnly`, 1366 y 390 px):** clic en el fondo cierra; clic dentro de la tarjeta no cierra; «Cancelar» y Escape cierran; 0 violaciones, 0 errores de página (`scripts/qa/csp-sintetico/unidades.js`).

### DEF-ML-01 — Mercado Libre / Listings: Vincular/Desvincular no hacen nada
- **Dónde:** `Views/MercadoLibre1/Listings.cshtml`: `<form id="ml-sync-form">` (línea 29) envuelve toda la tabla (cierra en 174) y contiene los formularios `Vincular` (125, 136) y `Desvincular` (160).
- **Causa:** formularios anidados son HTML inválido; el parser descarta los internos. `this.closest('tr').querySelector('.ml-unlink-form')` devuelve `null` (ya fallaba con el `onclick` original).
- **Efecto:** el botón «Desvincular» no envía nada; los formularios de vincular tampoco se envían como formularios propios.
- **Corrección propuesta:** sacar los formularios de la tabla (usar el atributo `form="..."` de los controles o formularios fuera del `<form>` de sincronización). Módulo ML diferido por el propietario: queda registrado, sin fecha.

## Resultados de la ronda de cierre funcional (2026-11-10, réplica local del stack CI, `Csp__Mode=ReportOnly`)

| # | Prueba | Resultado | Evidencia |
|---|---|---|---|
| 1 | Suite E2E con ReportOnly | **0 violaciones, 0 errores de página** en 223 tests (213 correctos + 10 omitidos clasificados, ver `docs/e2e-omitidos-clasificacion.md`) y en 3 corridas consecutivas de CI en `main`. **El umbral de cero violaciones pasa a ser gate** (`resumen.js --max-violaciones 0`; sigue fallando con exit 3 si la medición no se engancha: falla de forma segura) | job `Playwright Tests`, paso «Resumen de violaciones CSP» |
| 2 | AJAX / `innerHTML` | **Parcial.** Estático: 0 `on*=` en cadenas JS ni en vistas; los `style="…"` generados por JS son válidos por la excepción acotada `style-src-attr 'unsafe-inline'`; los `<script>` inline reciben el nonce por tag helper. Dinámico: modales de Punitorios, Caja, Cotizador y Venta cubiertos por la suite E2E (0 violaciones). Siguen sin recorrerse con datos: modales remotos de Producto/Proveedor/Catálogo fuera de lo que toca la suite | `scripts/qa/csp-sintetico/` (PR #270) + suite |
| 3 | Roles distintos del admin | **Hecho** para los 7 roles de prueba (administrador, gerente, vendedor, cajero, repositor, tecnico, contador): 139 páginas (menú + un nivel de detalle) por navegador, 0 violaciones, 0 5xx, 0 errores de página. Límite: se recorre lo que cada rol ve en su menú, no se fuerzan URL no visibles (eso es autorización, F3) | `scripts/qa/csp-roles/crawl.js` (también corre en CI) |
| 4 | Páginas sin layout / impresión | **Hecho para HTML**: `Cotizacion/Imprimir` y `Venta/ComprobanteFactura` (2 cotizaciones y 2 facturas): «Imprimir» invoca `window.print()`, «Volver» regresa a la página previa, 0 violaciones, en los 3 navegadores. **No cubierto**: recibos y contratos (se generan como PDF/descarga, no como documento HTML con CSP) y exportes Excel/PDF (archivos) | `scripts/qa/csp-roles/impresion.js` |
| 5 | Estados que el clon no tenía | Sin cambios respecto de #270 (DocumentoCliente vacío, Unidades, ML modo real) | — |
| 6 | Otros navegadores | **Hecho en escritorio**: Chromium, Firefox y WebKit (Playwright) con los mismos recorridos de los puntos 3 y 4: 0 violaciones en los tres. WebKit emite 160 avisos «frame-ancestors se ignora en Report-Only» (informativo: la directiva solo se evalúa con Enforce; la política declara `frame-ancestors 'self'`). **No cubierto**: Safari real y móvil real (WebKit de Playwright no equivale a Safari iOS) | `scripts/qa/csp-roles/crawl.js --browser firefox|webkit` |
| 7 | HTTPS + SignalR tras proxy | **Hecho en un Caddy local** (`tls internal`, igual que el `Caddyfile` del repo, certificado propio) delante del stack QA: página cargada por HTTPS, cookies `secure`, WebSocket `wss://…/hubs/notificaciones` abierto y estable, 0 violaciones y 0 errores, en Chromium, Firefox y WebKit, tanto en Report-Only como en `Enforce`. **No cubierto**: el servidor y el nombre de host reales (`ERP_DOMAIN`), fuentes de Google con salida a Internet del servidor y la CA raíz instalada en los clientes | `scripts/qa/csp-roles/https-signalr.js` |
| 8 | Ventana en uso real | Sigue dependiendo del propietario (no se activa nada en el entorno real) | — |

**Qué sigue habilitando `Enforce` solo con autorización del propietario:** (2) modales remotos restantes, (5), (7) y (8). No se activa `Enforce` en el entorno real.

### Suite funcional con `Csp__Mode=Enforce` (2026-11-10, stack QA aislado, nunca el real)

Se levantó el stack con la política **realmente aplicada** (cabecera `Content-Security-Policy`, no `-Report-Only`; verificado) y se corrieron las mismas pruebas:

- Suite E2E completa 1366x768: **213 correctos, 10 omitidos clasificados, 0 fallidos** (igual que en Report-Only: nada se bloquea).
- Recorrido por roles: 3 navegadores × 7 roles × 139 páginas: 0 violaciones, 0 5xx, 0 errores de página; los avisos de `frame-ancestors` de WebKit desaparecen porque con Enforce la directiva sí se evalúa.
- HTTPS + SignalR (wss) con Caddy local: conectado en los 3 navegadores, 0 violaciones.

Lectura honesta: es evidencia fuerte de que `Enforce` no rompe las pantallas que cubre la suite y el menú de cada rol. **No** demuestra ausencia de bloqueos en: modales remotos fuera de la suite (ítem 2), estados sin datos (ítem 5), Safari/móvil real (ítem 6), host real (ítem 7) ni uso real prolongado (ítem 8). Por eso `Enforce` sigue sin activarse en el entorno real; la decisión y la ventana de observación (ítem 8) son del propietario.

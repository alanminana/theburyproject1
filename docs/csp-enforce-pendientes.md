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


## Ronda F2 (2026-11-11): escenarios sin cobertura, compatibilidad funcional con Enforce y gate sin falsos verdes

Stack QA aislado (`bury-mb`, `APP_PORT=18220`, base SQL Server sintética propia, nunca la real), política **realmente aplicada** (`Content-Security-Policy`, no `-Report-Only`; el script lo verifica antes de empezar y aborta si no). `Csp__Mode=Enforce` se fijó solo en un compose propio fuera del repo; el default del repo sigue siendo `Off`. **No se activa ni se propone activar `Enforce` en el entorno real.**

### (a) Validación técnica ejecutable en QA (lo que se cierra con esta ronda)

Herramienta nueva: `scripts/qa/csp-roles/escenarios.js` + `scripts/qa/csp-roles/escenarios/*.js` (reutiliza el medidor `securitypolicyviolation`/consola/`pageerror` de `csp-roles`; agrega comprobaciones **funcionales** por escenario). Cada escenario falla ante: violación CSP, `pageerror`, error de consola, 5xx, 4xx no declarado, documento HTML sin cabecera CSP, request fallida, check funcional en falso, o un escenario sin comprobaciones/interacciones (exit 3 si no corrió ninguno). Guardas: solo `127.0.0.1`/`localhost`, `CSP_QA_STACK_AISLADO=1` obligatorio, no corre en CI; las escrituras solo se permiten en escenarios que las declaran (`escribe:true`), en el resto el arnés aborta todo POST y lo registra.

Comando: `CSP_QA_STACK_AISLADO=1 E2E_BASE_URL=http://127.0.0.1:18220 node scripts/qa/csp-roles/escenarios.js [--browser chromium|firefox|webkit] [--modo enforce|reportonly] [--solo <regex>] [--out r.json]`.

| # | Escenario | Qué se ejercita y qué se comprueba (funcionamiento, no solo ausencia de violaciones) |
|---|---|---|
| 1 | Producto: modal de edición AJAX (`GetJson`/`EditAjax`) | GetJson 200 JSON, formulario poblado, 5 solapas, característica agregada/quitada (`innerHTML`), cálculo de precios, validación fallida, guardado 2xx y fila actualizada sin recargar, cierre por X/fondo/Escape/Cancelar |
| 2 | Producto: alta AJAX, ajuste masivo (wizard), comisión, menú de fila, historial de precio | validación client-side, autocompletes, `CreateAjax` 2xx, wizard siguiente/atrás, modal de comisión, API de historial |
| 3 | Categoría y Marca | alta AJAX con validación fallida y alta real, edición remota (`GetJson`), baja con `confirmAction` (cancelar no borra; confirmar da de baja vía `form.submit()` bajo `form-action 'self'`) |
| 4 | Proveedor (drawer) | alta con validación fallida, picker de productos (dropdown/chips por JS), edición remota (`GetEditData`), validación de edición, baja con confirmación |
| 5 | Cliente (modal AJAX por partial + `innerHTML`) | `ModalCreate`/`ModalEdit` 200 HTML, wizard por pasos, validación fallida, `confirm()` nativo por cambios sin guardar (cancelar mantiene, aceptar cierra), modal de límites por puntaje |
| 6 | Seguridad: usuarios, roles, permisos | modales inyectados por `fetch` (nuevo usuario, bloquear, cambiar contraseña, nuevo/editar/duplicar rol, copiar permisos), validación fallida (POST inválido real), `RolDetails`, matriz de permisos, filtros/pestañas |
| 7 | Cotizador / carrito (`Venta/Create`) | búsqueda de cliente y producto (dropdowns por JS), aviso `<dialog>` de actualizar datos, alta al carrito |
| 8 | Cotización: listado, detalle, impresión, PDF | `DescargarPdf` 200 `application/pdf` con CSP y firma `%PDF`, descarga real desde el botón, `Imprimir` HTML con `data-action=print` |
| 9 | Exportaciones | Reportes Ventas (xlsx+pdf), Márgenes (xlsx), Morosidad (xlsx+pdf), Devolución (CSV): descarga real, extensión, tamaño y firma de archivo (`PK`/`%PDF`); `CuotasVencidas` «Exportar» es un `data-action=print` |
| 10 | DocumentosConfig: vista previa PDF (iframe `blob:`) | plantilla/paquete/regla: 200 PDF, iframe recibe el blob, enlace «abrir», Escape cierra. **Defecto real, ver abajo** |
| 11 | Estados especiales | DocumentoCliente vacío (`click-id` abre el modal de subida), Unidades (pestañas `activateTab`, «ver» vacío), POST inválido que re-renderiza la página completa (`Cliente/Create`, `OrdenCompra/Create`) con cabecera CSP y errores de servidor |
| 12 | Layout compartido | notificaciones, notas rápidas (alta, checklist), panel de tickets (alta con modal, lista por `innerHTML`, detalle) |
| 13 | Manejadores `data-confirm`/`data-action` | `confirm()` declarativo en formulario y botón (cancelar no envía; aceptar envía), `toggle-hide`, `remove-closest`, `print`, `history-back` |
| 14 | Barridos de controles (21 pantallas) | clic en cada control distinto (hasta 2 por firma, escrituras bloqueadas, diálogos descartados): Catálogo (5 pestañas), Proveedor, Cliente, Seguridad, Crédito, Caja, Cotización, Venta, OrdenCompra, Ticket, Mora, Devolución, Documento, MovimientoStock, AlertaStock, Reporte, Dashboard |
| 15 | Política efectiva (control negativo) | script inline sin nonce, `onclick` inline, `<style>` inline, imagen e iframe externos: **bloqueados y detectados** (no se ejecutan) y el log de la app registra `CSP violation` (WARN) para `script-src-elem`, `script-src-attr` y `style-src-elem` |
| 16 | Política: nonce, errores, SignalR | nonce distinto por solicitud y coincidente con los `<script nonce>`, 404 HTML y 404 de API con cabecera, WebSocket `/hubs/notificaciones` abierto |
| 17 | `/csp-report`: rate limit | 204 mientras no se supera el límite (30/min por IP), 429 después, nada fuera de 204/429, cuerpo > 8 KB rechazado |

Cifras exactas por navegador: ver «Resultados de la corrida» al final de esta sección.

**Defectos reales encontrados en esta ronda (reproducir → causa → corrección → test):**

1. **DocumentosConfig: la vista previa PDF quedaba bloqueada bajo `Enforce`.** Reproducido: `DocumentosConfig/Plantillas|Paquetes|Reglas` → «Vista previa» → violación `frame-src | blob | enforce` (una por listado, 3 en total, más el aviso de consola «Framing 'blob:…' violates … frame-src 'self'») y el iframe vacío. Causa: `documentos-preview.js` descarga el PDF con `fetch`, crea un `blob:` same-origin y lo carga en un `<iframe>`; `frame-src 'self'` no cubre `blob:`. Corrección **mínima**: `frame-src 'self' blob:` (sin `unsafe-inline`, sin orígenes externos; un `blob:` solo lo crea JavaScript del propio origen, ya limitado a `'self'`+nonce). Alternativas descartadas: cargar la URL del PDF directo en el iframe (el editor de plantillas pasa una `Promise<Blob>` de contenido sin guardar, no hay URL) y relajar `object-src` (medido en Chromium con el visor PDF real: el PDF se renderiza dentro del iframe `blob:` con `object-src 'none'`, así que `object-src` no cambia). Test: `CspHttpTests.Politica_FrameSrcPermiteBlobPorLaVistaPreviaPdf_SinAmpliarAOrigenesExternos`; revalidado con el escenario 10 (0 violaciones en los 3 listados). Evidencia: `D:\tmp\mision\b\pdf-B-blob.png` y el experimento aislado `D:\tmp\mision\b\pdftest.js`.
2. **`Csp:Mode` inválido degradaba la política en silencio.** Reproducido (mutación): `Csp__Mode=5` o `ReportOnly,Enforce` (el binder acepta números y listas) emitían **Report-Only** sin avisar, y `Csp__Mode=` (vacío) quedaba en **Off**. Causa: `Configure<CspOptions>` sin validación. Corrección: `AddCspOptions` con `ValidateOnStart` (nombre exacto `Off|ReportOnly|Enforce`, sin distinguir mayúsculas); un valor inválido **impide el arranque**. Una clave presente pero vacía también falla (quitar la clave = `Off`). Tests: `CspHttpTests.ModoInvalido_ImpideElArranque_NoDegradaEnSilencio` (6 valores) y `Enforce_NoDependeDeMayusculas`; los 6 casos inválidos fallan al revertir la corrección.
3. **El gate de CSP del job de CI daba verde con mediciones rotas** (F2.3, abajo).

Pruebas xUnit nuevas (`TheBuryProyect.Tests/Integration/CspHttpTests.cs`, 23 en total): modo inválido/mayúsculas, nonce único por solicitud y coincidente con el de `style-src`, cabecera en 404 HTML/AJAX/API, 302 a login y `/csp-report` con error, reporte → WARN en el log con rate limit exacto (30 aceptados, luego 429), política de `frame-src`.

### F2.3 — el gate de CSP no da falsos verdes

`scripts/qa/csp-medicion/selftest.js` (corre en el paso «Resumen de violaciones CSP» del workflow existente, sin navegador, <1 s; con `--con-navegador` usa un servidor local con CSP y ejecuta `preload.js`, `crawl.js` e `impresion.js` reales) demuestra que el gate falla cuando corresponde y pasa con datos sanos: 25 casos, 0 discrepancias. Contra la versión anterior de `resumen.js`, 9 de los 17 casos sin navegador daban resultado equivocado; defectos reales corregidos:

| Caso | Antes | Ahora |
|---|---|---|
| archivo truncado (última línea cortada) o con líneas corruptas (podían ocultar una violación) | exit 0 (verde) | exit 3 |
| contextos creados sin instrumentar | exit 0 | exit 3 (`contexto-creado` vs `contexto`) |
| error de consola CSP sin evento `securitypolicyviolation` | exit 0 | exit 1 (cuenta como violación) |
| pocos contextos medidos para los tests ejecutados / 0 tests / reporte ilegible | sin control | exit 3 con `--playwright-json` (cociente ≥ 0,5; calibrado en 1,06 contextos por test en una corrida real de 17 tests) |
| `crawl.js` con inventario vacío (0 páginas) | exit 0 | exit 1 (cobertura esperada por rol, `cobertura-esperada.json`; ver la ronda siguiente: reemplaza el mínimo fijo de 10 páginas, que era incorrecto para Repositor/Técnico) |
| umbral inválido (`--max-violaciones abc`) | verde | exit 2 |

Ya fallaban bien (confirmado): instrumentación no instalada/archivo inexistente, resumen vacío, 0 contextos, fallo de instalación, 1 violación; `impresion.js` con botones ausentes o sin `--rutas`. `--max-pageerrors N` es un gate opt-in (no se activó en CI para no cambiar el criterio vigente; el resumen siempre informa los errores de página).

### F2.4 — límites explícitos de cobertura

- **Mercado Libre: diferido por el propietario.** No se trabajó (incluye DEF-ML-01 y el modo real de mensajes de ML).
- Los `data-confirm` de pantallas reales (medios de pago, plantillas, alertas de mora/stock, lotes de precios) necesitan datos que la base sintética no tiene: se probó el **mismo manejador** (`csp-actions.js`) sobre elementos con el mismo marcado en una pantalla real, no cada pantalla.
- Reporte *Movimientos valorizados*: el enlace de Excel solo aparece con movimientos valorizados; sin datos sembrados **no se probó** su descarga.
- Recibos, contratos y comprobantes de caja se entregan como **PDF** (`Documento/Ver`, `ContratoVentaCredito/Generar`, `Cotizacion/DescargarPdf`); no hay HTML con CSP para ellos. Se verificó la descarga/preview de PDF de plantillas, paquetes, reglas y cotización; **no se emitieron** recibos/contratos reales (requiere flujos de cobro/crédito con efectos).
- El visor PDF del escenario 10 se probó solo en Chromium; Firefox/WebKit se ejercitan hasta la carga del iframe `blob:`, sin prueba de que su visor renderice.
- Los escenarios corren como `administrador`; los menús por rol ya están en `crawl.js`.
- `innerHTML` (`wwwroot/js`, 39 archivos): ejercitados dinámicamente por esta ronda o por la suite E2E: catálogo (producto crear/editar, categoría, marca, precio, historial), cliente (modal, index), proveedor (módulo, base, picker), seguridad (index, módulo, roles), venta-create, cotización (simulador, ui), layout (tickets, notas). **Solo cubiertos por la suite o estáticamente** (sin escenario propio): `cliente-garante`, `cliente-details`, `credito-*`, `caja-*`, `configurar-venta-credito`, `venta-credito-embebido`, `venta-facturar`, `venta-devolucion-modal`, `ordencompra-form`, `inventario-global`, `plantillas-contrato`, `contrato-venta-credito-preparar`, `movimientos-inventario-modal`. **Fuera de alcance (ML diferido):** `mercadolibre-*`.

### (b) Pendientes operativos que dependen del entorno real (no cerrables en QA)

1. Safari real e iOS/Android físicos (el WebKit de Playwright no equivale a Safari iOS; visor PDF y `print()` reales).
2. Servidor definitivo y nombre de host real (`ERP_DOMAIN`), Caddy con certificados definitivos (CA raíz instalada en los clientes), fuentes de Google con salida a Internet del servidor, `frame-ancestors` tras el proxy real.
3. Ventana en `ReportOnly` con usuarios reales durante el período que acuerde el propietario y revisión del WARN `CSP violation` (`/csp-report`): es la única evidencia sobre datos y flujos que la base sintética no reproduce.
4. Decisión del propietario sobre `Enforce`: este documento **no** la propone; la reversión sigue siendo volver a `Off`/`ReportOnly` por configuración.

### Resultados de la corrida (2026-11-11, Enforce, 1366x900, stack `bury-mb`)

Un contexto de navegador por escenario (44 por navegador, instrumentados con el medidor de violaciones/consola/`pageerror`) más 2 de arranque (sonda de cabecera y login). Una «página» es una navegación de documento del frame principal.

| Navegador | Escenarios | Correctos | Checks funcionales | Interacciones | Páginas | Violaciones CSP | `pageerror` | Errores de consola | 5xx |
|---|---|---|---|---|---|---|---|---|---|
| Chromium | 44 | 44 | 257 | 342 | 244 | 0 | 0 | 0 | 0 |
| Firefox | 44 | 44 | 257 | 350 | 250 | 0 | 0 | 0 | 0 |
| WebKit (Playwright) | 44 | 43 (+1 repetido) | 253 | 345 | 246 | 0 | 0 | 0 | 0 |

WebKit: el escenario «Exportaciones: /Reporte/Ventas» falló en esa corrida por una carrera del arnés (el enlace aún no estaba tras el POST de «Generar»; sin violaciones ni errores); se corrigió la espera y se repitió el bloque de exportaciones: 4/4 correctos, 34 checks. Salvo esa espera, los ajustes de arnés para Firefox/WebKit (stub de `window.print()` que bloquea Firefox, cargas canceladas por descargas, reintento de `goto` tras la navegación automática de un alta AJAX) no afectan a la política.

Evidencia (fuera del repo): `D:\tmp\mision\b\final-{chromium,firefox,webkit}.{txt,json}`, `esc-chromium.txt` (corrida previa), `selftest-antes.txt` / `selftest-despues.txt`, `pdf-B-blob.png`, `pdftest.js`.

## Ronda 2026-11-11 (cierre funcional): recorrido por roles con descargas y cobertura por rol

Causa de un rojo en CI al agregar los specs de configuración aislada (C-03): el spec de excepción documental ahora **genera un contrato**, y el recorrido de roles encontró el enlace `/ContratoVentaCredito/Ver?ventaId=N`, que responde con un PDF adjunto. Para el navegador eso no es una navegación (`page.goto: Download is starting`) y el arnés lo contaba como error. No era una falla de la aplicación ni de la CSP (0 violaciones).

| Cambio en `scripts/qa/csp-roles/` | Qué garantiza |
|---|---|
| `descargas.js` | Un inicio de descarga solo es válido si la misma ruta, pedida con la sesión del rol, responde **200** (no 302 a Login ni 403/5xx), es un **adjunto** (`Content-Disposition: attachment` o tipo de archivo conocido), **no está vacía** y su **firma** coincide con el tipo (`%PDF-`, `PK` para xlsx/zip). Cualquier otra cosa sigue siendo error. Los permisos se validan con esa misma sesión: un rol sin permiso no obtiene un 200 con archivo. |
| `descargas.selftest.js` | 12 casos con servidor local (PDF/XLSX válidos; vacío, firma rota, HTML, 403, 302, 500, 404 inválidos). Corre en el paso del workflow. |
| `cobertura-esperada.json` | **Reemplaza el mínimo fijo de páginas.** Cada rol registra las pantallas de menú estables que debe ver (administrador 15, gerente 10, vendedor 9, cajero 8, repositor 4, técnico 4, contador 8). Falla si falta alguna (inventario incompleto o vacío), si responde ≥ 400, o si el rol no tiene cobertura registrada; avisa (sin fallar) si aparece una pantalla nueva sin registrar. Un mínimo común de 10 páginas era incorrecto: Repositor y Técnico solo tienen 4 pantallas de menú por permisos. |
| `crawl.js --salida` | Guarda rutas visitadas, menú, descargas y avisos por rol (artefacto `crawl-<navegador>.json`). |

Verificación (stack QA de CI, ReportOnly): el arnés original reproduce el error (`ERR /ContratoVentaCredito/Ver?ventaId=43: page.goto: Download is starting`, exit 1); con el cambio, 3 navegadores × 7 roles × 175 páginas, 0 errores, 0 violaciones, 2 descargas válidas (PDF de ~47 KB) por cada rol con permiso y 0 para Repositor/Técnico. Mutación: agregar una ruta inexistente a la cobertura de Técnico hace fallar el recorrido (exit 1, «inventario incompleto»).

### Avisos `frame-ancestors` de WebKit (clasificados)

WebKit imprime en consola, una vez por documento, `The Content Security Policy directive 'frame-ancestors' is ignored when delivered in a report-only policy.` (196 avisos en el recorrido completo en ReportOnly; 0 en Chromium y Firefox).

- **Naturaleza:** informativo, **no es una violación**. La directiva `frame-ancestors` solo se evalúa con la política en Enforce; la política declara `frame-ancestors 'self'`.
- **Sin exposición mientras tanto:** la aplicación envía además `X-Frame-Options: SAMEORIGIN` en todas las respuestas (`Program.cs`, middleware de cabeceras), que sí aplica en cualquier modo. El recorrido ahora **falla** si falta esa cabecera.
- **Contraste con Enforce:** con la política en Enforce (stack QA aislado) WebKit emite **0** avisos (administrador y repositor, 60 páginas).
- **Control:** el arnés solo tolera estos avisos con la política en Report-Only; con Enforce, cualquier aviso es error.

## Ronda 2026-11-11 (ronda 2): activación de Enforce PREPARADA, NO ACTIVADA

**Preparado, no activado; requiere autorización posterior del propietario.** `Csp:Mode` sigue en `Off` en el entorno real (no se cambió ninguna configuración real ni se tocó la base real). El ítem 8 de la tabla (ventana en `ReportOnly` en uso real) **sigue pendiente**: lo único que se hizo es dejar listo cómo se hace.

Procedimiento, criterios y ensayo: **`docs/csp-enforce-activacion.md`**. En resumen:

- **Cómo se activa y revierte:** una línea de configuración + reinicio, sin redeploy de código. Local: `scripts\ejecutar-erp-local.ps1 -CspMode Enforce|ReportOnly|Off`; Docker: `CSP_MODE=` en el `.env` (nuevo; antes la app no recibía `Csp__Mode`) + `docker compose up -d --no-deps app` (`restart` no aplica el cambio). Un valor inválido impide el arranque; `preflight.sh` lo valida antes del deploy.
- **Observación:** `scripts/ops/csp-observacion.ps1` (solo lectura) resume el WARN `CSP violation` por directiva/URI/página/día, separa ruido de extensiones y devuelve exit 0 solo con 0 violaciones propias y medición válida (sin log, sin días suficientes o sin centinela = exit 3).
- **GO/NO-GO:** ≥ 10 días hábiles de uso real que incluyan un cierre de mes, 0 violaciones propias, centinela visto, 12 áreas críticas y todos los navegadores/dispositivos reales cubiertos, SignalR/impresión/descargas OK, respaldo verificado, reversión ensayada ≤ 5 min y autorización escrita.
- **Verificación inmediata:** `scripts/ops/csp-humo-lectura.js` (solo lectura) más un humo manual guiado.
- **No lo cubre la evidencia de QA:** Safari/iOS/Android reales, el host y Caddy definitivos, fuentes de Google con salida a Internet, extensiones/impresoras reales y datos reales (ver §4 de ese documento).

## Ronda 2026-11-14 (D-05): módulos que solo cubría la suite o el análisis estático

Reutiliza la evidencia anterior (gate de cero violaciones, 44 escenarios, recorrido por 7 roles x 3 navegadores, impresión, HTTPS+SignalR, `Enforce` real en QA: no se repitió). CI de `main` cd44a614 (2026-11-14): 227 de 227 contextos instrumentados, 0 violaciones, 0 errores de página, 0 fallos de instrumentación, 216 tests ejecutados / 4 omitidos clasificados. **No se activó `Enforce`; `Csp:Mode` sigue en `Off` por defecto.**

Nuevo: `scripts/qa/csp-roles/escenarios/modulos-sin-escenario.js` (11 escenarios, solo lectura: el arnés aborta toda escritura; el detalle se descubre desde datos reales del stack y **falla** si no hay dato). Cubre `credito-details`/pago/adelanto, `cliente-details`/garante, devolución de venta, `caja-detalles-apertura`/`caja-historial`/`caja-abrir`, `ordencompra-form`, `inventario-global`, listado de plantillas de contrato, `venta-credito-embebido`/`configurar-venta-credito` (Venta/Create) y movimientos de inventario. Stack de réplica de CI (`bury-c03d05`, SQL Server propio, ReportOnly por el overlay; datos de los 4 seeders): **Chromium, Firefox y WebKit 11/11, 0 violaciones, 0 errores de página/consola, 0 5xx** (36 comprobaciones, 43 interacciones, 79 páginas por navegador). `lib.js` ahora ignora en modo `reportonly` el aviso informativo `frame-ancestors ... report-only` de WebKit (antes solo se toleraba en `crawl.js`).

**NO validado (declarado):** formulario de alta de plantillas de contrato (`/PlantillaContratoCredito/Create` exige `configuracion.update`, que Administrador no tiene; con ese rol responde AccessDenied y el arnés no inicia sesión como SuperAdmin); `venta-facturar`, `contrato-venta-credito-preparar` y `venta-devolucion-modal` (envío del modal) requieren escrituras o estados que este recorrido de solo lectura no ejecuta; Safari/iOS reales, host real y ventana en uso real (sin cambios: dependen del propietario).

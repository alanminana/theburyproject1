# COTIZ-UX-0 - Auditoria UX/UI completa de Cotizacion

## A. Objetivo

Auditar el modulo Cotizacion a nivel UX/UI, accesibilidad, baja vision, mobile, claridad operativa y flujo comercial, sin modificar codigo productivo.

Esta fase abre el frente COTIZ-UX y deja un roadmap de micro-lotes seguros para reformular la experiencia visual sin romper simulacion, persistencia, descuentos, PDF ni conversion a venta.

## B. Base y contexto

- Rama base: `main`.
- HEAD auditado: `d1d9dfa`.
- `git pull --ff-only`: `Already up to date`.
- Tipo de fase: audit-only / diagnostico / roadmap.
- Cambios permitidos: solo este documento.
- Cambios no permitidos: Razor, JS, CSS, backend, models, viewmodels, migrations, tests, Playwright specs, endpoints, payloads, permisos y reglas de negocio.

Contexto funcional real:

- Cotizacion es un flujo separado de Venta.
- Cotizacion simula y guarda presupuestos.
- La simulacion no confirma venta, no descuenta stock, no registra caja y no factura.
- La conversion crea una Venta editable en estado `Cotizacion`.
- La conversion no confirma venta, no descuenta stock, no registra caja y no genera factura.
- `ProductoPrecioLista` / resolver de precio vigente siguen siendo autoridad de precios actuales.

## C. Archivos auditados

Vistas Razor:

- `Views/Cotizacion/Index_tw.cshtml`
- `Views/Cotizacion/Listado_tw.cshtml`
- `Views/Cotizacion/Detalles_tw.cshtml`
- `Views/Cotizacion/Imprimir_tw.cshtml`

JavaScript:

- `wwwroot/js/cotizacion-simulador.js`
- `wwwroot/js/cotizacion-conversion.js`

CSS compartido relacionado:

- `wwwroot/css/shared-components.css`

Controllers:

- `Controllers/CotizacionController.cs`
- `Controllers/CotizacionApiController.cs`

Services e interfaces:

- `Services/CotizacionService.cs`
- `Services/CotizacionPagoCalculator.cs`
- `Services/CotizacionConversionService.cs`
- `Services/CotizacionPdfService.cs`
- `Services/CotizacionVencimientoBackgroundService.cs`
- `Services/Interfaces/ICotizacionService.cs`
- `Services/Interfaces/ICotizacionPagoCalculator.cs`
- `Services/Interfaces/ICotizacionConversionService.cs`
- `Services/Interfaces/ICotizacionPdfService.cs`

Models de servicio:

- `Services/Models/CotizacionCrearRequest.cs`
- `Services/Models/CotizacionSimulacionRequest.cs`
- `Services/Models/CotizacionSimulacionResultado.cs`
- `Services/Models/CotizacionProductoRequest.cs`
- `Services/Models/CotizacionProductoResultado.cs`
- `Services/Models/CotizacionMedioPagoResultado.cs`
- `Services/Models/CotizacionPlanPagoResultado.cs`
- `Services/Models/CotizacionConversionModels.cs`

Tests y Playwright:

- `TheBuryProyect.Tests/Unit/CotizacionApiControllerTests.cs`
- `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs`
- `TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs`
- `TheBuryProyect.Tests/Unit/CotizacionPagoCalculatorContractTests.cs`
- `TheBuryProyect.Tests/Unit/CotizacionConversionApiTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionServicePersistenceTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionConversionServiceTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionConversionSecurityTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionCancelacionServiceTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionCancelacionSecurityTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionNumeracionTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionVencimientoServiceTests.cs`
- `TheBuryProyect.Tests/Integration/CotizacionVencimientoSecurityTests.cs`
- `e2e/cotizacion-simulador.spec.js`
- `e2e/cotizacion-conversion.spec.js`
- `e2e/ui-4e-layout-visual.spec.js`

Docs previas relevantes:

- `docs/cotiz-1a-formulario-cotizacion-campos-comerciales.md`
- `docs/cotiz-1b-descuento-por-producto.md`
- `docs/cotiz-3a-simulador-resultados-visual-minimo.md`
- `docs/cotiz-3b-simulador-resultados-cards.md`
- `docs/cotiz-3c-simulador-agrupacion-medio-pago.md`
- `docs/cotiz-qa-simulador-playwright.md`
- `docs/cotiz-qa-2-guardar-conversion-descuentos.md`
- `docs/cotiz-qa-3-conversion-e2e-cotizacion-venta.md`
- `docs/ux-comercial-1a-design-tokens-comerciales.md`
- `docs/ux-comercial-1b-cotizacion-badges-estados.md`
- `docs/ux-comercial-1c-cotizacion-imprimir-simulador.md`
- `docs/ui-0b-analisis-funcional-flujos-modulos.md`
- `docs/ui-1-design-system-dark-accesible.md`
- `docs/ui-5b-componentes-base.md`
- `docs/ui-5l-auditoria-js-dinamica-restante.md`

## D. Mapa de pantallas

### `/Cotizacion` - alta/simulador

Vista: `Views/Cotizacion/Index_tw.cshtml`

Rol actual:

- Pantalla principal de creacion.
- Busca productos.
- Permite agregar por buscador o ID manual.
- Edita cantidades y descuentos por item.
- Busca cliente opcional.
- Permite datos libres de cliente.
- Elige medios a incluir.
- Carga descuentos generales.
- Define vencimiento y observaciones.
- Ejecuta simulacion.
- Muestra resultados por medio de pago.
- Permite seleccionar opcion de pago.
- Habilita guardado despues de simular.

### `/Cotizacion/Listado` - cotizaciones guardadas

Vista: `Views/Cotizacion/Listado_tw.cshtml`

Rol actual:

- Filtra por busqueda, estado, fecha desde y fecha hasta.
- Lista numero, fecha, cliente, estado, total base, total seleccionado y accion.
- Usa badges canonicos `.quote-state-badge`.
- CTA principal: nueva cotizacion.

### `/Cotizacion/Detalles/{id}` - detalle y conversion

Vista: `Views/Cotizacion/Detalles_tw.cshtml`

Rol actual:

- Muestra numero, cliente, fecha, estado.
- Acciones: descargar PDF, imprimir, volver.
- Si la cotizacion esta emitida y el usuario tiene permiso `cotizaciones:convert`, muestra panel de conversion.
- Muestra resumen de totales.
- Muestra observaciones.
- Muestra productos cotizados.
- Muestra opciones simuladas.
- Si ya fue convertida, muestra link a la venta resultante.

### `/Cotizacion/Imprimir/{id}` - impresion HTML

Vista: `Views/Cotizacion/Imprimir_tw.cshtml`

Rol actual:

- Vista standalone sin layout.
- Optimizada para imprimir.
- Muestra marca, cotizacion, cliente, pago seleccionado, productos, ajuste por plan, totales y disclaimer.
- Tiene botones `Volver` e `Imprimir` visibles fuera de print.

### `/Cotizacion/DescargarPdf/{id}` - PDF real

Controller/service:

- `CotizacionController.DescargarPdf`
- `CotizacionPdfService`

Rol actual:

- Genera archivo PDF desde el resultado de cotizacion.
- No depende del layout HTML de impresion.

## E. Mapa de flujo actual

### Flujo de crear cotizacion

1. Usuario entra a `/Cotizacion`.
2. Busca producto por texto en `#cotizacion-producto-buscar` o usa ID manual.
3. Selecciona producto desde `#cotizacion-productos-dropdown`.
4. Define cantidad.
5. Agrega producto con `#cotizacion-agregar-producto`.
6. Edita cantidad y descuentos por producto en la tabla dinamica.
7. Opcionalmente busca cliente en `#cotizacion-cliente-buscar`.
8. Opcionalmente completa nombre/telefono libres.
9. Activa/desactiva medios de pago.
10. Opcionalmente define descuento general, vencimiento y observaciones.
11. Ejecuta `#cotizacion-simular`.
12. JS POSTea a `/api/cotizacion/simular`.
13. Backend calcula opciones con `CotizacionPagoCalculator`.
14. JS renderiza subtotal, descuento, total base y grupos de opciones.
15. JS auto-selecciona recomendada si existe.
16. Usuario selecciona opcion de pago.
17. Boton `#cotizacion-guardar` queda habilitado.
18. JS POSTea a `/api/cotizacion/guardar`.
19. Backend persiste via `CotizacionService.CrearAsync`.
20. UI navega a `/Cotizacion/Detalles/{id}`.

### Flujo de convertir a venta

1. Usuario entra a `/Cotizacion/Detalles/{id}`.
2. Razor calcula `puedeConvertir = EstadoCotizacion.Emitida && User.TienePermiso("cotizaciones", "convert")`.
3. Si puede convertir, aparece `#cotizacion-conversion-panel`.
4. Click en `#cotizacion-btn-convertir`.
5. JS abre modal `#cotizacion-conversion-modal`.
6. JS POSTea a `/api/cotizacion/{id}/conversion/preview`.
7. `CotizacionConversionService.PreviewConversionAsync` devuelve total, detalles, errores, advertencias, cambios de precio y cliente faltante.
8. El modal muestra errores, advertencias, detalle de productos, cliente override si aplica y politica de precios.
9. Boton confirmar se habilita solo si cliente y advertencias cumplen las condiciones.
10. JS POSTea a `/api/cotizacion/{id}/conversion/convertir`.
11. `CotizacionConversionService.ConvertirAVentaAsync` crea venta editable.
12. JS navega a `/Venta/Edit/{ventaId}`.

## F. Mapa de JS

### `cotizacion-simulador.js`

Responsabilidades principales:

- Mantiene `state.productos`, producto seleccionado, cliente seleccionado, ultima simulacion y opcion seleccionada.
- Lee URLs desde atributos `data-*` del root `[data-cotizacion-simulador]`.
- Busca productos y clientes.
- Renderiza dropdowns.
- Renderiza tabla dinamica de productos.
- Construye request de simulacion.
- Ejecuta `fetch` a `/api/cotizacion/simular`.
- Renderiza resultados agrupados por medio de pago.
- Construye request de guardado.
- Ejecuta `fetch` a `/api/cotizacion/guardar`.
- Redirige a detalle si el backend devuelve `detalleUrl`.

Contratos DOM criticos:

- `[data-cotizacion-simulador]`
- `#cotizacion-producto-buscar`
- `#cotizacion-productos-dropdown`
- `#cotizacion-agregar-producto`
- `#cotizacion-productos-tbody`
- `#cotizacion-cliente-buscar`
- `#cotizacion-clientes-dropdown`
- `#cotizacion-cliente-id`
- `[data-cotizacion-medio]`
- `#cotizacion-descuento-gral-pct`
- `#cotizacion-descuento-gral-importe`
- `#cotizacion-simular`
- `#cotizacion-guardar`
- `#cotizacion-resultados`
- `#cotizacion-resultados-tbody`
- `input[name="cotizacion-opcion-pago"]`
- `[data-cotizacion-opcion-key]`
- `[data-cotizacion-row-key]`
- `[data-cotizacion-cantidad-index]`
- `[data-cotizacion-desc-pct-index]`
- `[data-cotizacion-desc-importe-index]`
- `[data-cotizacion-eliminar-index]`

Observacion de seguridad frontend:

- La mayoria del render nuevo de resultados usa `createElement` y `textContent`.
- Persisten usos de `innerHTML` en feedback, filas de producto y dropdowns. Hay escape manual `esc()`, pero es deuda a migrar gradualmente a DOM seguro.

### `cotizacion-conversion.js`

Responsabilidades principales:

- Abre/cierra modal de conversion.
- Carga preview desde `/api/cotizacion/{id}/conversion/preview`.
- Renderiza errores, advertencias y detalle de precios.
- Permite seleccionar cliente override si falta cliente.
- Permite elegir politica de precio cotizado o actual.
- Requiere check de advertencias cuando corresponde.
- Ejecuta conversion con `/api/cotizacion/{id}/conversion/convertir`.
- Navega a `/Venta/Edit/{ventaId}`.

Contratos DOM criticos:

- `[data-cotizacion-conversion]`
- `#cotizacion-btn-convertir`
- `#cotizacion-conversion-modal`
- `#cotizacion-conversion-loading`
- `#cotizacion-conversion-contenido`
- `#cotizacion-conversion-errores`
- `#cotizacion-conversion-advertencias`
- `#cotizacion-conversion-resumen`
- `#cotizacion-total-cotizado`
- `#cotizacion-detalles-preview-panel`
- `#cotizacion-cliente-override-panel`
- `#cotizacion-override-cliente-buscar`
- `#cotizacion-override-clientes-dropdown`
- `#cotizacion-override-cliente-id`
- `#cotizacion-precio-cotizado`
- `#cotizacion-precio-actual`
- `#cotizacion-check-advertencias`
- `#cotizacion-btn-confirmar-conversion`

Observacion positiva:

- El script de conversion usa predominantemente `createElement` y `textContent` para datos externos.

## G. Mapa de CSS

CSS propio de cotizacion:

- No se detecto archivo `wwwroot/css/*cotiz*`.

CSS compartido relevante:

- `.payment-option-card`
- `.payment-option-card--selected`
- `.payment-option-group`
- `.payment-status-chip`
- `.payment-status-chip--available`
- `.payment-status-chip--blocked`
- `.payment-status-chip--requires-client`
- `.payment-status-chip--selected`
- `.commercial-context-bar`
- `.quote-state-badge`
- `.quote-state-badge--emitida`
- `.quote-state-badge--convertida`
- `.quote-state-badge--cancelada`
- `.quote-state-badge--vencida`
- `.total-breakdown-card`
- `.sticky-action-footer`
- `.input-erp`
- `.select-erp`
- `.alert-erp`

Estado actual:

- `Listado_tw.cshtml` y `Detalles_tw.cshtml` usan `.quote-state-badge`.
- `cotizacion-simulador.js` usa `.payment-option-card`, `.payment-option-group` y `.payment-status-chip`.
- Las vistas todavia usan mucho Tailwind inline para inputs, tablas, cards, secciones y botones.
- `.input-erp`, `.select-erp`, `.alert-erp`, `.commercial-context-bar`, `.total-breakdown-card` y `.sticky-action-footer` existen como patrones compartidos, pero no estan aplicados sistematicamente en Cotizacion.

## H. Mapa de controllers/services relevantes

### `CotizacionController`

Endpoints UI:

- `Index()` -> `Index_tw`.
- `Listado(...)` -> `Listado_tw`.
- `Detalles(id)` -> `Detalles_tw`.
- `Imprimir(id)` -> `Imprimir_tw`.
- `DescargarPdf(id)` -> PDF.
- `BuscarProductos(term, take)` -> busqueda para el simulador.
- `ProductoResumen(id)` -> alta por ID manual.
- `BuscarClientes(term, take)` -> busqueda de cliente.

### `CotizacionApiController`

Endpoints API:

- `POST /api/cotizacion/simular`
- `POST /api/cotizacion/guardar`
- `POST /api/cotizacion/{id}/conversion/preview`
- `POST /api/cotizacion/{id}/cancelar`
- `POST /api/cotizacion/vencer-expiradas`
- `POST /api/cotizacion/{id}/conversion/convertir`

### `CotizacionPagoCalculator`

Autoridad de simulacion:

- Calcula subtotal, descuentos, total base.
- Calcula medios de pago.
- Evalua efectivo, transferencia, tarjeta credito, tarjeta debito, MercadoPago y credito personal.
- Marca estados como disponible, requiere cliente/evaluacion, bloqueado o no disponible.

### `CotizacionService`

Autoridad de persistencia de cotizacion:

- Crea cotizacion desde request.
- Recalcula simulacion antes de persistir.
- Guarda snapshots de producto, precio, descuentos y opciones.
- Lista, obtiene, cancela y vence cotizaciones.

### `CotizacionConversionService`

Autoridad de conversion:

- Valida estado convertible.
- Detecta vencimiento, conversion previa, cancelacion y advertencias.
- Compara precio cotizado contra precio actual.
- Crea venta editable en estado `Cotizacion`.
- No confirma venta, no registra caja, no descuenta stock, no genera factura.
- Marca cotizacion como convertida.

### `CotizacionPdfService`

Autoridad de PDF:

- Genera PDF real.
- Refleja cliente, pago seleccionado, productos, descuentos, ajuste por plan, totales y estado.

## I. Tests y specs existentes

Unitarios relevantes:

- `CotizacionApiControllerTests`: simular, serializacion, ruta, permisos, no tocar venta/caja/stock.
- `CotizacionControllerUiTests`: vistas `_tw`, script propio, layout separado, boton conversion, permisos, impresion.
- `CotizacionControllerPdfTests`: PDF, nombre de archivo, no modificar estado.
- `CotizacionPagoCalculatorContractTests`: contratos del calculador.
- `CotizacionConversionApiTests`: endpoints de preview/conversion.

Integracion relevante:

- `CotizacionServicePersistenceTests`: persistencia, snapshots, descuento general, no crear venta ni tocar stock/caja.
- `CotizacionConversionServiceTests`: preview, bloqueos, advertencias, conversion, no stock/caja/factura, IVA, descuentos.
- `CotizacionConversionSecurityTests`: permiso `cotizaciones:convert`.
- `CotizacionCancelacionServiceTests` y `CotizacionCancelacionSecurityTests`.
- `CotizacionNumeracionTests`.
- `CotizacionVencimientoServiceTests` y `CotizacionVencimientoSecurityTests`.

Playwright:

- `e2e/cotizacion-simulador.spec.js`
  - T1 estructura inicial.
  - T2 simulacion genera cards.
  - T3 seleccion aplica card seleccionada y radio checked.
  - T4 mobile 390px sin overflow en resultados.
  - T5 agrupacion por medio de pago.
  - T6 descuentos por producto.
  - T7 guardar con descuento por producto.
  - T8 guardar con descuento general.
- `e2e/cotizacion-conversion.spec.js`
  - T9 conversion completa a `/Venta/Edit/{id}`.
  - T10 estado convertida visible.
  - T11 conversion con descuento por producto importe.
  - T12 panel ausente si ya fue convertida.
- `e2e/ui-4e-layout-visual.spec.js`
  - Capturas visuales de Cotizacion mobile y desktop.

## J. Hallazgos UX generales

1. La pantalla `/Cotizacion` tiene demasiadas tareas simultaneas.
   - Producto, cliente, medios, descuentos, vencimiento, observaciones, simulacion, seleccion y guardado compiten en una sola vista.

2. El orden visual no coincide del todo con el orden mental de venta.
   - El usuario necesita primero entender "cliente / productos / resumen / accion".
   - Actualmente los controles secundarios del aside pueden sentirse tan importantes como la accion principal.

3. Los datos comerciales principales no estan siempre visibles.
   - Total base, descuento, opcion seleccionada y accion guardar quedan lejos cuando el usuario trabaja arriba en productos o en mobile.

4. La tabla de productos funciona, pero es un punto de friccion.
   - Usa min-width grande.
   - Los descuentos por item estan mezclados en columnas angostas.
   - La accion eliminar queda al extremo derecho y se pierde en scroll horizontal.

5. La pantalla explica bien que no toca stock/caja, pero los chips ocupan espacio primario.
   - Es informacion valiosa, aunque deberia tener menos peso que productos, total y siguiente accion.

6. El listado es correcto pero basico.
   - Tiene filtros y badges, pero podria mejorar escaneo operativo con acciones por fila mas claras, estado mas prominente y resumen de resultados.

7. Detalles muestra mucha informacion util pero en bloques planos.
   - Conversion, totales, productos y opciones simuladas no tienen una jerarquia suficientemente orientada a la decision.

## K. Hallazgos UI / visuales

1. Hay mezcla de patrones.
   - Tailwind inline domina las vistas.
   - Tokens compartidos existen pero se aplican parcialmente.

2. Las cards de totales son repetitivas.
   - Subtotal, descuento y total base tienen mismo peso visual.
   - El total comercial y la opcion seleccionada deberian dominar mas.

3. Las etiquetas usan `text-[11px]`, uppercase y `text-slate-400`.
   - Es consistente con algunas pantallas viejas, pero puede ser cansador y chico para baja vision.

4. Faltan componentes ERP canonicos ya disponibles.
   - Inputs podrian migrar a `.input-erp`.
   - Alertas podrian migrar a `.alert-erp`.
   - Totales podrian usar `.total-breakdown-card`.
   - Acciones mobile podrian usar `.sticky-action-footer`.

5. Las acciones primarias no forman una barra de decision.
   - `Simular cotizacion` y `Guardar cotizacion` estan en la parte baja del aside.
   - En desktop es aceptable; en mobile se vuelve recorrido largo.

6. El modal de conversion tiene buen contenido, pero poco refinamiento visual.
   - El preview de diferencias podria priorizar riesgo, total, politica de precios y confirmacion.

## L. Hallazgos mobile

1. `/Cotizacion` se apila en una columna, pero conserva tabla de productos `min-w-[920px]`.
   - Esto obliga scroll horizontal para editar productos, cantidades, descuentos y eliminar.

2. `Detalles` conserva tablas `min-w-[760px]` y `min-w-[980px]`.
   - En mobile el detalle y las opciones simuladas requieren scroll horizontal.

3. Las acciones principales no son sticky.
   - Simular, guardar y total no quedan disponibles cuando el usuario revisa productos.

4. Los dropdowns absolutos pueden ser fragiles en viewports chicos.
   - Producto y cliente usan panel absoluto bajo el input; en mobile pueden competir con teclado y scroll.

5. Los filtros del listado son mobile-friendly por grid de una columna.
   - La tabla posterior sigue requiriendo scroll horizontal.

6. La vista imprimir tiene media query simple y razonable.
   - No es la prioridad del rework mobile operativo.

## M. Hallazgos accesibilidad / baja vision

1. Hay labels `for` en los campos principales de `Index`.
   - Punto positivo.

2. Los dropdowns usan `aria-expanded` y `aria-controls`, pero no parecen implementar combobox/listbox completo.
   - Falta rol/estado para resultados, opcion activa, navegacion con teclado y cierre anunciado.

3. El feedback principal tiene `aria-live="polite"`.
   - Punto positivo, pero el contenedor no define `role="status"` o `role="alert"` segun tono.

4. Los mensajes de error/advertencia dinamicos dependen de render JS.
   - Conviene normalizar semantica con `.alert-erp`, `role="alert"` para error y `role="status"` para exito/progreso.

5. El modal de conversion tiene `role="dialog"`, `aria-modal="true"` y `aria-labelledby`.
   - Punto positivo.
   - Falta auditar trampa de foco, retorno de foco y cierre con Escape como contrato Playwright.

6. Icon-only buttons existen en cerrar modal y eliminar producto.
   - Tienen o deberian tener `aria-label`; eliminar producto ya lo tiene.
   - Cerrar modal no tiene `aria-label` explicito, solo icono.

7. Mucho texto secundario usa `text-slate-500` o `text-slate-600`.
   - Puede ser bajo contraste en dark theme, especialmente labels, helpers y separadores.

8. Las tablas carecen de `scope="col"` en headers.
   - Recomendable para productos, listado, detalles, opciones e impresion.

9. Los radios de opciones se crean dentro de label.
   - Correcto como base, pero conviene revisar nombre accesible completo: medio, plan, total, estado.

## N. Hallazgos de claridad comercial

1. La diferencia entre cotizar, simular, guardar y convertir no siempre se traduce en jerarquia visual.
   - El copy lo explica, pero la interfaz deberia convertirlo en pasos operativos.

2. Cliente "opcional" es ambiguo.
   - Para simular puede ser opcional.
   - Para convertir, el servicio puede exigir cliente faltante segun preview.
   - Deberia comunicarse como "Cliente para recuperar / convertir" con regla visible.

3. Descuentos por producto y descuentos generales pueden sumarse.
   - Esta regla aparece como helper, pero deberia estar mas cerca del resumen comercial y con lenguaje de impacto.

4. Medio de pago seleccionado no domina la pantalla de alta.
   - Es una decision comercial central y deberia quedar en un resumen persistente.

5. Estados de opciones de pago son tecnicos.
   - "RequiereCliente", "RequiereEvaluacion", "BloqueadoPorProducto" necesitan labels de negocio mas claros sin cambiar payload.

## O. Hallazgos de simulador

1. El simulador ya mejoro con cards agrupadas por medio.
   - Es una base canonicamente util.

2. El vacio inicial es claro pero poco accionable.
   - Podria indicar el requisito inmediato: agregar producto y presionar simular.

3. El agrupamiento por medio ayuda, pero la comparacion entre opciones todavia puede ser pesada.
   - Cada card muestra varios datos; conviene destacar total, cuotas y estado.

4. Los medios a incluir estan separados de resultados.
   - En desktop viven en el aside; en mobile quedan antes/despues segun flujo y pueden sentirse como configuracion avanzada.

5. Auto-seleccion recomendada existe.
   - Debe preservarse y mostrarse con texto visible, no solo color.

## P. Hallazgos de productos / detalle

1. La tabla de productos es el principal cuello de botella mobile.
   - Contiene producto, codigo, cantidad, precio, dos descuentos, subtotal y accion.

2. Descuento por producto ocupa columnas operativas permanentes.
   - Para usuarios sin descuento, agrega ruido.
   - Podria pasar a expansion por fila, popover, drawer o modo avanzado.

3. Producto seleccionado se comunica con texto bajo el buscador.
   - Correcto, pero podria integrarse como preview de producto con precio vigente y accion clara.

4. Alta por ID manual esta dentro de `details`.
   - Correcto como soporte/avanzado; no deberia ganar peso visual.

## Q. Hallazgos de descuentos

1. Hay descuento por producto en porcentaje e importe.
   - Los campos son funcionales y estan cubiertos por tests.

2. Hay descuento general en porcentaje e importe.
   - Ambos se suman si tienen valor.

3. Deuda comercial importante: porcentaje por producto no se propaga a Venta como descuento de detalle si no hay importe snapshot.
   - Docs previas lo registran.
   - La UI de conversion deberia advertirlo antes de confirmar.

4. Descuento general no se propaga a `Venta.Descuento`.
   - Esta documentado en tests y docs.
   - Debe aparecer como advertencia UX si el usuario espera continuidad exacta.

5. Edge case pendiente: importe mayor al subtotal.
   - Sin evidencia de bug en esta auditoria, pero conviene cubrirlo funcionalmente antes de prometer UX.

## R. Hallazgos de totales

1. En alta hay subtotal, descuento y total base.
   - No hay una tarjeta/resumen con jerarquia comercial completa.

2. En detalles hay cuatro cards: subtotal, descuento, total base, opcion seleccionada.
   - Opcion seleccionada tiene color, pero el conjunto podria condensarse con desglose.

3. En impresion/PDF el total esta mas orientado a documento comercial.
   - Tiene "Total base", "Total c/ plan" y "Total".

4. Falta persistencia visual del total en mobile.
   - Candidato claro para `.sticky-action-footer` o sticky summary.

## S. Hallazgos de conversion a venta

1. El panel de conversion esta bien separado y condicionado por estado/permiso.
   - Contrato critico a preservar.

2. Copy actual aclara que crea venta editable y no confirma ni descuenta stock.
   - Correcto y valioso.

3. Modal de preview es funcional pero deberia priorizar:
   - total cotizado;
   - si falta cliente;
   - advertencias bloqueantes;
   - cambios de precio;
   - descuentos que no se trasladan;
   - politica de precio;
   - accion final.

4. Confirmar conversion deberia sentirse como paso serio, no como accion secundaria de modal.
   - Sin hacerlo alarmista, necesita mas claridad.

5. El estado convertida y link a venta ya existen.
   - Deben conservarse y reforzarse visualmente.

## T. Hallazgos de impresion/PDF

1. Existen dos salidas:
   - `Imprimir_tw.cshtml`: HTML imprimible.
   - `CotizacionPdfService`: PDF real.

2. La vista imprimir usa CSS inline propio.
   - Es razonable por `Layout = null`.

3. La impresion tiene buena estructura documental.
   - Marca, numero, fecha, cliente, pago, productos, ajustes y totales.

4. No conviene redisenar impresion junto con alta/simulador.
   - Debe ser fase separada si se toca.

5. PDF tiene service propio con contratos unitarios.
   - No tocar en fases UX visuales salvo auditoria/ajuste documental especifico.

## U. Que no conviene cambiar

- Endpoints `/api/cotizacion/*`.
- Payloads de simular, guardar, preview y convertir.
- Calculos de precios, IVA, descuentos, intereses, cuotas y credito.
- Reglas de conversion a venta.
- Condiciones de no stock/no caja/no factura.
- Permisos `cotizaciones:view/create/convert/cancel/expire`.
- Estados de cotizacion.
- Snapshots de producto/precio/descuento.
- Numeracion de cotizacion.
- PDF service.
- Conversion a `Venta/Edit/{id}`.
- Selectores ya usados por Playwright.
- `data-*`, `id`, `name`, antiforgery y scripts existentes.

## V. Que conviene reformular

1. Jerarquia de `/Cotizacion`.
   - Dejar productos, resumen y acciones como eje principal.
   - Mover configuracion secundaria a panel lateral, acordeon o drawer.

2. Productos en mobile.
   - Reemplazar tabla ancha por cards/filas adaptativas sin romper DOM hooks o cubrir con tests nuevos.

3. Totales y accion.
   - Usar resumen comercial persistente con subtotal, descuento, total base, opcion seleccionada, simular y guardar.

4. Accesibilidad semantica.
   - Scope en tablas, roles de alertas, modal focus, dropdowns accesibles.

5. Conversion.
   - Mejorar preview, advertencias y confirmacion, preservando contratos.

6. Tokens visuales.
   - Migrar gradualmente a `.input-erp`, `.alert-erp`, `.total-breakdown-card`, `.sticky-action-footer`, `.payment-status-chip`.

7. Listado.
   - Mejorar densidad, acciones por fila y lectura de estado sin tocar filtros ni ruta.

## W. Riesgos

Riesgos altos:

- Romper selectores usados por Playwright.
- Cambiar payloads de simulacion/guardado/conversion.
- Alterar descuento, IVA, interes o precio vigente desde UX.
- Tocar conversion y venta en la misma fase visual.

Riesgos medios:

- Migrar tablas a cards sin cobertura de contrato HTML.
- Cambiar dropdowns de producto/cliente y perder busqueda o seleccion.
- Agregar sticky mobile que tape contenido o botones.
- Cambiar modal sin preservar permisos/estado.

Riesgos bajos:

- Mejoras de labels y `scope`.
- Normalizacion visual de inputs.
- Reordenamiento visual con mismos ids/hooks.

## X. Contratos criticos a preservar

DOM y JS:

- `[data-cotizacion-simulador]` y sus `data-*-url`.
- `[data-cotizacion-conversion]` y sus `data-*-url`.
- Todos los IDs `#cotizacion-*` usados por JS y Playwright.
- `input[name="cotizacion-opcion-pago"]`.
- `[data-cotizacion-medio]`.
- `[data-cotizacion-cantidad-index]`.
- `[data-cotizacion-desc-pct-index]`.
- `[data-cotizacion-desc-importe-index]`.
- `[data-cotizacion-eliminar-index]`.
- `[data-cotizacion-opcion-key]`.
- `[data-cotizacion-row-key]`.

Backend/API:

- `POST /api/cotizacion/simular`.
- `POST /api/cotizacion/guardar`.
- `POST /api/cotizacion/{id}/conversion/preview`.
- `POST /api/cotizacion/{id}/conversion/convertir`.
- `POST /api/cotizacion/{id}/cancelar`.
- `POST /api/cotizacion/vencer-expiradas`.
- `GET /Cotizacion`, `/Cotizacion/Listado`, `/Cotizacion/Detalles/{id}`, `/Cotizacion/Imprimir/{id}`, `/Cotizacion/DescargarPdf/{id}`.

Reglas:

- Cotizacion no toca stock/caja/factura.
- Conversion crea venta editable, no confirmada.
- Conversion no registra caja ni descuenta stock.
- Cotizacion convertida no vuelve a mostrar panel de conversion.
- Estados y permisos se mantienen en backend.

Tests:

- Mantener contratos de `cotizacion-simulador.spec.js`.
- Mantener contratos de `cotizacion-conversion.spec.js`.
- Mantener tests .NET filtro `Cotizacion`.
- Mantener visual suite para `/Cotizacion`.

## Y. Roadmap propuesto COTIZ-UX

### COTIZ-UX-1A - Accesibilidad semantica basica

Alcance:

- Labels, `scope="col"`, roles de alertas, `aria-label` faltantes, mejoras de `aria-live`.
- Revisar foco/cierre/retorno de foco del modal.
- No cambiar layout ni reglas.

Validacion sugerida:

- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "CotizacionControllerUiTests|LayoutUiContractTests"`
- `npx.cmd playwright test e2e/cotizacion-simulador.spec.js`
- `npx.cmd playwright test e2e/cotizacion-conversion.spec.js`

### COTIZ-UX-1B - Jerarquia visual del formulario principal

Alcance:

- Reordenar secciones de `/Cotizacion`.
- Reducir peso de chips informativos.
- Separar datos principales de configuracion secundaria.
- Aplicar `.input-erp`/`.select-erp` donde sea mecanico y seguro.

No tocar:

- JS, endpoints, payloads, calculos.

### COTIZ-UX-1C - Detalle de productos y acciones por fila

Alcance:

- Mejorar tabla/lista de productos.
- Hacer descuentos por item menos invasivos.
- Mejorar accion eliminar y edicion en mobile.

Riesgo:

- Alto si se cambian hooks dinamicos. Requiere tests de contrato y Playwright.

### COTIZ-UX-1D - Totales, descuentos y resumen comercial

Alcance:

- Usar resumen comercial claro.
- Jerarquizar total base, descuento y opcion seleccionada.
- Explicar suma de descuentos.
- Preparar base visual para sticky summary.

### COTIZ-UX-1E - Mobile y sticky summary / acciones principales

Alcance:

- Sticky mobile para total, simular, guardar.
- Evitar que tape contenido.
- Reducir scroll horizontal donde sea posible.

Validacion clave:

- Playwright mobile 390px.

### COTIZ-UX-1F - Simulador y claridad de condiciones

Alcance:

- Mejorar cards de opciones de pago.
- Labels de estados mas comerciales.
- Diferenciar recomendado, bloqueado, requiere cliente/evaluacion.
- Mejorar empty/loading/error states.

No tocar:

- Calculos ni estados backend.

### COTIZ-UX-1G - Conversion a venta

Alcance:

- Mejorar panel y modal de conversion.
- Hacer visible que crea venta editable.
- Advertir cambios de precio, falta de cliente y descuentos que no se trasladan.
- Consistencia visual con Ventas.

Validacion clave:

- `cotizacion-conversion.spec.js`.
- Tests .NET de conversion.

### COTIZ-UX-1H - Listado y detalles guardados

Alcance:

- Mejorar listado, filtros, acciones por fila, estado y empty state.
- Mejorar detalle guardado sin tocar conversion.

### COTIZ-UX-1I - Impresion/PDF auditada visualmente

Alcance:

- Solo si negocio lo requiere.
- Mantener HTML imprimible y PDF real separados.

### COTIZ-UX-QA - QA visual/contratos final

Alcance:

- Build.
- Tests filtro `Cotizacion`.
- Layout/UI contract tests.
- Playwright simulador.
- Playwright conversion.
- Playwright visual layout.
- Revision `innerHTML` residual.
- Revision mobile y baja vision.

## Z. Proximo prompt recomendado

```text
PROMPT - COTIZ-UX-1A - Accesibilidad semantica basica de Cotizacion

Segui AGENTS.md / CLAUDE.md.
Usar main actualizado.
Crear rama cotiz/ux-1a-accesibilidad.

Tipo: Razor/JS minimo, sin cambios funcionales.

Objetivo:
Aplicar mejoras semanticas y de accesibilidad de bajo riesgo en Cotizacion:
- scope="col" en tablas de Index/Listado/Detalles/Imprimir;
- aria-label faltante en cerrar modal;
- role="status"/"alert" para feedback y mensajes dinamicos si aplica;
- revisar labels/aria-live sin cambiar IDs, data-*, endpoints, payloads ni reglas;
- documentar cualquier deuda de combobox/listbox que no se resuelva en esta fase.

No tocar:
services, controllers, models, migrations, calculos, conversion, stock, caja, credito, tests productivos salvo que haga falta actualizar contratos.

Validar:
dotnet build --configuration Release
dotnet test --configuration Release --filter "CotizacionControllerUiTests|LayoutUiContractTests"
npx.cmd playwright test e2e/cotizacion-simulador.spec.js
npx.cmd playwright test e2e/cotizacion-conversion.spec.js
git diff --check
git status --short
```

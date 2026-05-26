# KIRA-VENTAS-INDEX-REWORK-1C - QA visual desktop/mobile de /Venta

## A. Objetivo

Validar `/Venta` contra el HTML objetivo completo del Centro de Ventas, con foco en desktop 1440x900, mobile 390x844, tabs, filtros, tabla/cards, accesos a Nueva Venta, recargo/descuento y devolucion.

## B. Rama y commits auditados

Rama auditada: `kira/ventas-index-rework-1a-visual-parity`.

Commits auditados:

- `103c221` Regularizar page wizard antes de index ventas (KIRA-VENTAS-INDEX-REWORK-1B2)
- `6eea304` Recrear index de ventas con visual parity objetivo (KIRA-VENTAS-INDEX-REWORK-1A)
- `ca8efd5` Completar HTML objetivo del index de ventas (KIRA-VENTAS-INDEX-REWORK-1B4)

## C. HTML objetivo usado

Se uso `docs/kira-ventas-index-html-objetivo.md` como referencia.

Puntos explicitos preservados del objetivo:

- No copiar `modal-nueva-venta`.
- No copiar `btn-abrir-modal-crear-venta`.
- No copiar `modal-crear-venta`.
- Nueva Venta debe navegar a `Venta/Create`.
- Recargo/Descuento debe abrir el modal productivo.
- Devolucion debe mantener `data-open-devolucion-modal`.

## D. URLs auditadas

- `http://localhost:5187/Venta`
- `http://localhost:5187/Venta/Create`

## E. Resultado desktop

Viewport: 1440x900.

Resultado:

- `/Venta` carga OK.
- Header `Centro de Ventas` visible.
- Estado de caja visible.
- 6 KPIs visibles.
- Tabs visibles: Operaciones, Pendientes, Cotizaciones y presupuestos, Devoluciones, Configuracion de pagos.
- Tab Operaciones activo por defecto.
- Filtros rapidos visibles como chips de resumen.
- Form de filtros visible.
- Tabla desktop visible.
- Columnas principales visibles: Numero, Fecha, Cliente, Estado, Autorizacion, Tipo de pago, Total, Acciones.
- No existe `btn-abrir-modal-crear-venta`.
- No existe `modal-nueva-venta`.
- No existe `modal-crear-venta`.
- No hubo errores JS criticos en consola.
- `documentElement.scrollWidth == clientWidth`: sin overflow horizontal de pagina.

Incumplimientos:

- Breadcrumb no visible.
- No existe bloque `details/summary` para filtros avanzados.

## F. Resultado mobile

Viewport: 390x844.

Resultado:

- `/Venta` carga OK.
- Header usable.
- 6 KPIs renderizan sin romper el ancho de pagina.
- Tabs scrolleables.
- Tabla desktop oculta.
- Cards mobile visibles.
- Cards muestran numero, cliente, estado, autorizacion, pago, total y acciones.
- Sticky mobile bar visible.
- Nueva Venta en sticky bar navega a `/Venta/Create`.
- No abre modal legacy.
- No existe `modal-nueva-venta`.
- Sin overflow horizontal de pagina.
- Acciones principales accesibles.

Incumplimientos:

- Breadcrumb no visible.
- No existe bloque `details/summary` para filtros avanzados.

## G. Resultado tabs

Tabs auditados con click:

- Operaciones
- Pendientes
- Cotizaciones y presupuestos
- Devoluciones
- Configuracion de pagos

Resultado:

- Cada click cambia `aria-selected` a `true` en el tab activo.
- Cada panel activo queda visible.
- Los otros paneles quedan ocultos.
- No se rompe el scroll de pagina.
- No se generan errores JS.

## H. Resultado filtros

Resultado:

- Form `#form-filtros` visible.
- Metodo GET preservado en Razor.
- Names preservados:
  - `Numero`
  - `FechaDesde`
  - `FechaHasta`
  - `Estado`
  - `TipoPago`
  - `EstadoAutorizacion`

Incumplimiento:

- No hay `details` ni `summary` para filtros avanzados, por lo tanto el punto "Filtros avanzados funcionan con details/summary" no puede validarse como OK.

## I. Resultado tabla/cards

Desktop:

- Tabla visible.
- Columnas requeridas visibles.
- Acciones por fila visibles.
- `data-open-devolucion-modal` existe en ventas elegibles.

Mobile:

- Tabla desktop oculta.
- Cards visibles.
- Cada card muestra numero, cliente, estado, autorizacion, tipo de pago, total y acciones.

## J. Resultado Nueva Venta

Resultado:

- Click en Nueva Venta navega a `http://localhost:5187/Venta/Create`.
- No se abre modal legacy.
- `modal-nueva-venta` no existe.
- `modal-crear-venta` no existe.

## K. Resultado recargo/devolucion

Recargo/Descuento:

- Boton disponible para el usuario auditado.
- Click abre `#modal-recargo`.
- No se registran errores JS en consola.

Devolucion:

- Se detectaron triggers `data-open-devolucion-modal`.
- Click en trigger visible abre `#modal-devolucion-venta`.
- No se registran errores JS en consola.

## L. Errores encontrados

Bloqueantes para merge segun el checklist de esta fase:

- Breadcrumb no visible en `/Venta`.
- No existe bloque `details/summary` para filtros avanzados.

No bloqueantes observados:

- El dataset local contiene nombres de cliente con payloads tipo XSS como texto visible. En esta prueba no ejecutaron JS ni generaron errores de consola.

## M. Correcciones aplicadas, si hubo

No se aplicaron correcciones al producto.

Solo se creo este documento de QA. El script temporal usado para Playwright fue removido al finalizar.

## N. Contratos preservados

- `#form-filtros`.
- Names `Numero`, `FechaDesde`, `FechaHasta`, `Estado`, `TipoPago`, `EstadoAutorizacion`.
- Link Nueva Venta a `/Venta/Create`.
- Ausencia de `btn-abrir-modal-crear-venta`.
- Ausencia de `modal-nueva-venta`.
- Ausencia de `modal-crear-venta`.
- `data-open-devolucion-modal`.
- Modal real `#modal-recargo`.
- Modal real `#modal-devolucion-venta`.
- Tabs con `role="tab"`, `aria-selected` y paneles asociados.

## O. Que no se toco

No se tocaron:

- Backend.
- Controllers.
- Services.
- Models.
- Migrations.
- Endpoints.
- Payloads.
- Stock.
- Caja.
- Credito.
- Cotizacion.
- Reglas de negocio.
- Archivos sensibles o ajenos indicados por el prompt.

## P. Validaciones tecnicas

Ejecutadas:

- `node --check wwwroot/js/venta-index-rework.js`
- `node --check wwwroot/js/venta-index.js`
- `node --check wwwroot/js/venta-module.js`
- `node --check wwwroot/js/venta-page-wizard.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "IndexView"`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check -- Views/Venta/Index_tw.cshtml Views/Venta/_VentaModuleStyles.cshtml wwwroot/css/venta-index-rework.css wwwroot/js/venta-index-rework.js docs/kira-ventas-index-rework-1a-visual-parity.md docs/kira-ventas-index-html-objetivo.md`
- QA Playwright/manual con Chromium en 1440x900 y 390x844.

## Q. Resultado build

`dotnet build --configuration Release`: OK.

Resultado exacto:

- 0 warnings.
- 0 errors.
- Tiempo: `00:02:24.66`.

## R. Resultado IndexView

`dotnet test --configuration Release --filter "IndexView"`: OK.

Resultado exacto:

- Failed: 0.
- Passed: 7.
- Skipped: 0.
- Total: 7.
- Duracion: 781 ms.

## S. Resultado VentaCreate

`dotnet test --configuration Release --filter "VentaCreate"`: OK.

Resultado exacto:

- Failed: 0.
- Passed: 116.
- Skipped: 0.
- Total: 116.
- Duracion: 1 s.

## T. Resultado Playwright/manual

Playwright/manual ejecutado con app real en `http://localhost:5187`.

Resultado:

- Desktop 1440x900: carga, tabs, tabla, filtros, modales reales y Nueva Venta OK; breadcrumb y `details/summary` ausentes.
- Mobile 390x844: carga, tabs scrolleables, cards, sticky bar, modales reales y Nueva Venta OK; breadcrumb y `details/summary` ausentes.
- Consola: sin errores ni page errors criticos.
- Overflow: sin overflow horizontal de pagina (`documentElement.scrollWidth == clientWidth`) en ambos viewports.

## U. Decision final

Decision C - requiere microfix antes de merge.

Motivo:

- El prompt exige breadcrumb visible.
- El prompt exige filtros avanzados con `details/summary`.
- Ambos puntos fallan en app real.

## V. Proximo paso

Proximo prompt recomendado:

`KIRA-VENTAS-INDEX-REWORK-1D - Microfix visual/funcional de /Venta: breadcrumb visible y filtros avanzados details/summary sin tocar backend.`

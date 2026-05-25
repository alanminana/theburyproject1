# KIRA-VENTAS-PAGE-REWORK-1D - Cliente, producto y totales en /Venta/Create

## A. Objetivo

Validar que `/Venta/Create` funciona como pagina wizard principal para el flujo basico de venta:

- cliente;
- producto;
- agregado de item;
- tabla de detalles;
- hidden inputs;
- totales;
- sidebar;
- mobile summary;
- validacion inicial.

La fase permite microfix solo si aparece un problema real. No se detecto un fallo productivo que requiera tocar Razor, JavaScript o CSS.

## B. Base y contexto

Base: `main` en `1bcb270` - `KIRA-VENTAS-PAGE-REWORK-1C` integrada.

Contexto heredado:

- `/Venta/Create` es el flujo principal de Nueva Venta.
- `/Venta` navega a `/Venta/Create`.
- La pagina usa `#venta-create-page`, no `#modal-crear-venta`.
- El submit final sigue siendo el POST nativo a `Venta/Create`.
- `venta-create.js` sigue siendo la fuente funcional de cliente, productos, detalles, hidden inputs y calculo de totales.
- `venta-modal-rework.js` solo sincroniza estado visual, wizard, sidebar, revision y mobile summary.

## C. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`
- `docs/kira-ventas-page-rework-1b-reemplazar-accesos-modal.md`
- `docs/kira-ventas-page-rework-1c-desacoplar-modal.md`
- `Views/Venta/Create_tw.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## D. Archivos modificados

- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-page-rework-1d-cliente-producto-totales.md`

No hubo cambios productivos en Razor, JavaScript ni CSS.

## E. QA cliente

Auditoria por codigo:

- `Create_tw.cshtml` conserva `#input-buscar-cliente`, `#dropdown-clientes`, `#hdn-cliente-id`, `#info-cliente`, `#info-cliente-nombre` y `#info-cliente-doc`.
- `venta-create.js` busca clientes desde `#input-buscar-cliente`.
- La seleccion completa `#hdn-cliente-id`, actualiza nombre/documento y muestra `#info-cliente`.
- `venta-modal-rework.js` observa `#info-cliente`, `#info-cliente-nombre` y `#info-cliente-doc` para refrescar estado visual.
- Sidebar y revision leen el cliente mediante `data-side-cliente`, `data-rev-cliente` y `data-conf-cliente`.

## F. QA producto

Auditoria por codigo:

- `Create_tw.cshtml` conserva `#input-buscar-producto`, `#dropdown-productos`, `#panel-agregar-producto`, `#hdn-producto-id`, `#txt-cantidad`, `#txt-descuento-item` y `#btn-agregar-producto`.
- `venta-create.js` mantiene busqueda de producto, seleccion, apertura de `#panel-agregar-producto` y validaciones de cantidad/unidad.
- No se reintrodujo pago por item ni endpoints legacy de condiciones por producto.

## G. QA agregado de item

Auditoria por codigo:

- `#btn-agregar-producto` agrega el producto al arreglo `detalles`.
- `renderDetalles()` repinta `#tbody-detalles`.
- `renderDetalles()` genera inputs dentro de `#detalles-hidden-inputs`.
- Los hidden generados conservan nombres `Detalles[i].ProductoId`, `Detalles[i].Cantidad`, `Detalles[i].PrecioUnitario`, `Detalles[i].Descuento`, `Detalles[i].Subtotal` y `Detalles[i].ProductoUnidadId`.
- `#detalle-items-badge` se actualiza desde `actualizarResumenOperacion()`.
- `venta-modal-rework.js` observa `#tbody-detalles` y `#detalles-hidden-inputs` para refrescar el paso Productos y los resumenes.

## H. QA totales

Auditoria por codigo:

- `recalcularTotales()` usa `/api/ventas/CalcularTotalesVenta`.
- Los totales visibles se escriben en `#total-subtotal`, `#total-descuento`, `#total-iva` y `#total-final`.
- Los hidden se escriben en `#hdn-subtotal`, `#hdn-descuento`, `#hdn-iva` y `#hdn-total`.
- `venta-modal-rework.js` lee los totales visibles y los espeja a `data-side-*`, `data-rev-*`, `data-mobile-total` y `data-conf-total`.
- No se cambio calculo ni serializacion.

## I. QA sidebar

Auditoria por codigo:

- El sidebar permanece en `aside.vm-sidebar`.
- Conserva `data-side-cliente`, `data-side-items`, `data-side-pago`, `data-side-subtotal`, `data-side-descuento`, `data-side-iva` y `data-side-total`.
- `syncSidebarSummary()` sincroniza cliente, items, pago y totales.
- El CSS mantiene sticky desktop para `#venta-create-page aside`.

## J. QA mobile summary

Auditoria por codigo:

- `Create_tw.cshtml` conserva `.vm-mobile-summary-bar`.
- El total mobile conserva `#vm-modal-sticky-total` y `data-mobile-total`.
- `initStickyTotalMirror()` copia `#total-final` hacia `#vm-modal-sticky-total`.
- `syncTotalsSummary()` tambien escribe `data-mobile-total`.

## K. Errores encontrados

No se encontraron errores productivos en los contratos auditados.

Limitacion: no se pudo ejecutar smoke manual/browser en `http://localhost:5187/Venta/Create` porque no habia servidor local escuchando.

## L. Correcciones aplicadas

No hubo correcciones productivas.

Se agregaron tests unitarios de contrato para:

- hooks `data-side-*`, `data-rev-*`, `data-conf-*` y `data-mobile-total` en `Create_tw.cshtml`;
- sincronizacion de cliente, productos, totales y hidden totals desde `venta-modal-rework.js`;
- prioridad de pagina por `pageWizardRoot || legacyModalRoot`.

## M. Contratos preservados

Preservados:

- `#venta-create-page`
- `#venta-form`
- `#btn-confirmar`
- `#input-buscar-cliente`
- `#dropdown-clientes`
- `#hdn-cliente-id`
- `#info-cliente`
- `#input-buscar-producto`
- `#dropdown-productos`
- `#panel-agregar-producto`
- `#hdn-producto-id`
- `#txt-cantidad`
- `#txt-descuento-item`
- `#btn-agregar-producto`
- `#tbody-detalles`
- `#detalles-hidden-inputs`
- `#select-tipo-pago`
- `#total-subtotal`
- `#total-descuento`
- `#total-iva`
- `#total-final`
- `#hdn-subtotal`
- `#hdn-descuento`
- `#hdn-iva`
- `#hdn-total`
- `#detalle-items-badge`
- `#VendedorUserId`
- `#Observaciones`
- `AplicarExcepcionDocumental`
- `MotivoExcepcionDocumentalCreate`
- `asp-action="Create"`
- antiforgery token

## N. Que no se toco

- Controllers
- Services
- Models
- Migrations
- Endpoints
- Payloads
- Stock
- Caja backend
- Credito backend
- Cotizacion backend
- `_VentaCrearModal.cshtml`
- `venta-crear-modal.js`
- Playwright specs
- Reglas de negocio
- Calculo backend

## O. Riesgo funcional

Riesgo bajo.

La fase solo agrega documentacion y tests de contrato. El flujo productivo auditado ya estaba cableado en la pagina.

## P. Validaciones

Validaciones previstas y ejecutadas:

- `node --check wwwroot/js/venta-modal-rework.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

## Q. Tests ejecutados

Se ejecuto `dotnet test --configuration Release --filter "VentaCreate"`.

Resultado exacto:

- 138 passed
- 0 failed
- 0 skipped
- Total: 138

## R. Playwright/manual ejecutado u omitido

Manual/browser omitido.

Motivo: `Invoke-WebRequest http://localhost:5187/Venta/Create` fallo con "No es posible conectar con el servidor remoto". No habia app local disponible para smoke desktop 1440x900 ni mobile 390x844.

No se ejecuto Playwright porque la consigna no lo exige como validacion obligatoria de esta fase y no habia servidor disponible.

## S. Deudas restantes

- Ejecutar smoke browser real de `/Venta/Create` cuando la app este levantada.
- Validar con datos reales: buscar cliente existente, seleccionar producto existente y confirmar que la tabla/totales se actualizan visualmente en desktop/mobile.
- Fase 1E: validar pago, credito, documentacion y excepcion.

## T. Proximo prompt recomendado

```text
PROMPT - KIRA-VENTAS-PAGE-REWORK-1E - Validar pago, credito y documentacion en /Venta/Create

Actua como Kira y segui AGENTS.md / CLAUDE.md.

Base esperada: main con KIRA-VENTAS-PAGE-REWORK-1D integrada.

Objetivo:
Validar que el wizard de /Venta/Create mantiene pago principal, tarjeta, cheque, credito personal,
verificacion crediticia, documentacion faltante y excepcion documental sin volver al modal legacy.

No tocar backend, endpoints, payloads, stock, caja ni reglas de negocio.
```

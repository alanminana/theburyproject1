# KIRA-VENTAS-PAGE-REWORK-1C - Desacoplar wizard de modal legacy

## A. Objetivo

Desacoplar el JS/CSS del wizard de Nueva Venta del root modal `#modal-crear-venta`, manteniendo `/Venta/Create` como superficie principal y conservando compatibilidad temporal con el modal legacy.

## B. Base y contexto

Base: `39d444d` - KIRA-VENTAS-PAGE-REWORK-1B integrada.

Estado heredado:

- `/Venta/Create` es el flujo principal.
- `/Venta` navega a `/Venta/Create`.
- `/Venta` ya no renderiza `_VentaCrearModal`.
- `/Venta` ya no carga `venta-crear-modal.js`.
- `_VentaCrearModal.cshtml` y `venta-crear-modal.js` siguen como legacy temporal.
- `venta-modal-rework.js` y `venta-modal-rework.css` conservan nombre de modal por compatibilidad.

## C. Deuda tomada desde 1B

- Naming fisico `venta-modal-rework.*` todavia menciona modal.
- Tests aun protegen contratos del partial legacy.
- `venta-create.js` aun escucha eventos `venta-crear-modal:open/close`, inofensivos si el modal no se carga.
- El CSS conserva selectores duales para `#venta-create-page` y `#modal-crear-venta`.

## D. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`
- `docs/kira-ventas-page-rework-1b-reemplazar-accesos-modal.md`
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-create.js`
- `wwwroot/css/venta-modal-rework.css`
- `wwwroot/css/venta-module.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## E. Archivos modificados

- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-page-rework-1c-desacoplar-modal.md`

## F. Referencias legacy encontradas

- `_VentaCrearModal.cshtml` conserva `#modal-crear-venta`, `aria-modal="true"`, `role="dialog"` y `VentaCrearModal.submit()` como partial legacy.
- `venta-crear-modal.js` conserva API `VentaCrearModal.open/close/submit` y hooks `#btn-abrir-modal-crear-venta`, `#modal-crear-venta-backdrop`.
- `venta-modal-rework.js` conservaba comentarios y reset legacy por evento `venta-crear-modal:open`.
- `venta-modal-rework.css` conserva selectores duales con `#modal-crear-venta` para no romper el partial legacy.
- `Create_tw.cshtml` solo conserva submodal real de documentacion crediticia `#modal-documentacion`.

## G. Cambios JS aplicados

- `venta-modal-rework.js` ahora declara roots separados:
  - `pageWizardRoot = document.getElementById('venta-create-page')`
  - `legacyModalRoot = document.getElementById('modal-crear-venta')`
  - `wizardRoot = pageWizardRoot || legacyModalRoot`
- La pagina queda priorizada sobre el modal legacy.
- Se reemplazo el guard implicito por un root dual explicito.
- El reset por `venta-crear-modal:open` quedo acotado a compatibilidad legacy mediante `legacyModalRoot`.
- El cierre de submodales ya no restaura overflow si el modal legacy sigue abierto.

## H. Cambios CSS aplicados

- Los selectores de pagina quedaron primero en los bloques duales.
- Se mantuvo compatibilidad con `#modal-crear-venta`.
- No se tocaron estilos de submodales reales como `#modal-documentacion`.

## I. Cambios Razor si hubo

No hubo cambios Razor.

`Create_tw.cshtml` ya contiene `#venta-create-page` y no incorpora root modal principal, overlay, backdrop, boton de cierre modal ni `aria-modal="true"` en la pagina.

## J. Compatibilidad temporal mantenida

- `_VentaCrearModal.cshtml` no fue borrado.
- `venta-crear-modal.js` no fue borrado.
- `venta-modal-rework.js` conserva soporte para `#modal-crear-venta`.
- `venta-modal-rework.css` conserva selectores legacy.

## K. Legacy pendiente

- Renombrar `venta-modal-rework.js` a `venta-page-wizard.js`.
- Renombrar `venta-modal-rework.css` a `venta-page-wizard.css`.
- Eliminar o archivar `_VentaCrearModal.cshtml` cuando tests y QA confirmen que no queda contrato activo.
- Eliminar o archivar `venta-crear-modal.js` cuando no haya referencias reales.
- Limpiar listeners legacy `venta-crear-modal:open/close` en `venta-create.js` en una fase separada.

## L. Contratos preservados

Preservados en `/Venta/Create`:

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
- `#panel-alerta-mora`
- `#panel-cupo-insuficiente`
- `#panel-documentacion-faltante`
- `#VendedorUserId`
- `#Observaciones`
- `AplicarExcepcionDocumental`
- `MotivoExcepcionDocumentalCreate`
- antiforgery token
- `asp-action="Create"`

## M. Que no se toco

- Controllers
- Services
- Models
- Migrations
- Endpoints
- Payloads
- Stock
- Caja
- Credito backend
- Cotizacion backend
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/Create_tw.cshtml`
- Playwright specs

## N. Riesgo funcional

Riesgo bajo-medio.

El cambio es de inicializacion JS/CSS y tests de contrato. El submit real de `/Venta/Create` sigue siendo nativo hacia `Create`, y `venta-create.js` sigue siendo la fuente funcional para cliente, productos, pagos, totales y validaciones.

## O. Validaciones

Validaciones previstas para esta fase:

- `node --check wwwroot/js/venta-modal-rework.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

## P. Tests ejecutados

- `dotnet test --configuration Release --filter "VentaCreate"` - OK.
- Resultado: 136/136 passed, 0 failed, 0 skipped.

## Q. Playwright ejecutado u omitido

Omitido.

Motivo exacto: no habia proceso de app `TheBuryProyect`, `TheBuryProyect.dll` ni `dotnet run` disponible para abrir `/Venta` y `/Venta/Create`. Se detectaron procesos Playwright MCP/test-server e IDE preexistentes, pero no una app del ERP escuchando para smoke visual.

No se afirma validacion visual OK.

## R. Deudas restantes

- QA visual desktop/mobile pendiente si la app no esta disponible.
- Renombre de archivos pendiente para una fase posterior.
- Limpieza final de partial y JS legacy pendiente.

## S. Proximo prompt recomendado

KIRA-VENTAS-PAGE-REWORK-1D - Validar flujo cliente/producto/totales en `/Venta/Create` como pagina wizard.

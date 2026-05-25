# KIRA-VENTAS-PAGE-REWORK-0 - Arquitectura Nueva Venta como pagina

## A. Objetivo

Definir como desmontar el flujo principal de Nueva Venta basado en modal y reencuadrarlo como una pagina fullscreen tipo wizard, sin revertir commits anteriores, sin tirar trabajo util y sin duplicar dos flujos activos.

Esta fase es doc-only. No modifica Razor productivo, JavaScript, CSS, backend, tests ni specs Playwright.

## B. Cambio de criterio del usuario

El criterio confirmado es:

- Nueva Venta no debe seguir como modal.
- Nueva Venta debe ser una pagina web nueva, fullscreen y operable como wizard.
- No se deben revertir commits anteriores del rework modal.
- Lo ya construido se debe reutilizar donde aporte valor.
- No se deben agregar features nuevas al modal.
- No debe aparecer un submodal extra de confirmacion salvo pedido explicito posterior.

## C. Estado actual del rework modal

El rework reciente dejo un wizard visual dentro de `Views/Venta/_VentaCrearModal.cshtml`.

Piezas actuales:

- `Views/Venta/_VentaCrearModal.cshtml`: contiene el root `#modal-crear-venta`, form `#venta-form`, step tabs, paneles de wizard, sidebar de totales, sticky mobile y submodales internos.
- `Views/Venta/Index_tw.cshtml`: renderiza el partial del modal cuando `puedeCrear` es true y carga `venta-create.js`, `venta-crear-modal.js` y `venta-modal-rework.js`.
- `wwwroot/js/venta-crear-modal.js`: expone `VentaCrearModal.open()`, `VentaCrearModal.close()` y `VentaCrearModal.submit()`. Abre/cierra el overlay y hace POST AJAX a `/Venta/CreateAjax`.
- `wwwroot/js/venta-modal-rework.js`: agrega navegacion wizard, estado visual, resumenes y sincronizacion. Tiene guard duro `if (!document.getElementById('modal-crear-venta')) return;`.
- `wwwroot/css/venta-modal-rework.css`: estilos especificos del wizard, pero varios selectores estan acoplados a `#modal-crear-venta`.
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`: tiene muchos contratos que esperan el modal y el JS del wizard modal.

El trabajo no esta perdido: el skeleton, clases `vm-*`, tabs, resumenes, estado por pasos y sincronizacion visual son reutilizables. Lo que debe cambiar es la raiz arquitectonica: de overlay/modal a pagina.

## D. Por que se debe pasar a pagina

El flujo actual mantiene la operacion critica dentro de `#modal-crear-venta`, aunque visualmente sea fullscreen. Eso genera deuda:

- El usuario espera una pagina, no un overlay.
- El boton principal de `Index_tw` abre un modal en vez de navegar.
- La confirmacion final depende de `VentaCrearModal.submit()` y de `/Venta/CreateAjax`, no del submit natural de `/Venta/Create`.
- `venta-modal-rework.js` solo se ejecuta si existe `#modal-crear-venta`.
- `venta-modal-rework.css` contiene selectores especificos del root modal.
- Los tests protegen el modal como superficie principal.
- Existen dos superficies de Nueva Venta: `Create_tw.cshtml` standalone y `_VentaCrearModal.cshtml` modal. Esa duplicacion ya fue documentada como fuente de inconsistencias.

El objetivo de las proximas fases debe ser dejar una sola superficie canonica: `/Venta/Create`.

## E. Archivos auditados

Auditados en esta fase:

- `Views/Venta/_VentaCrearModal.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-crear-modal.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `wwwroot/css/venta-module.css`
- `Controllers/VentaController.cs`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `e2e/venta-pago-por-item.spec.js`
- `docs/kira-ventas-modal-rework-*`
- `docs/ventas-ux-*`

Observacion: `Views/Venta/_VentaModuleScripts.cshtml` fue solicitado para auditoria, pero no existe en el arbol actual. Los scripts de venta se cargan directamente en las secciones `@section Scripts` de `Index_tw.cshtml` y `Create_tw.cshtml`.

Graphify: no existe `graphify-out/graph.json` en este checkout, por lo que la auditoria se hizo por lectura directa.

## F. Mapa del modal actual

Render:

- `Views/Venta/Index_tw.cshtml` renderiza `<partial name="_VentaCrearModal" />` dentro de `@if (puedeCrear)`.
- `puedeCrear` depende de `ViewBag.PuedeCrearVenta`, que en `VentaController.Index` se setea segun caja abierta.

Apertura:

- En `Index_tw.cshtml`, el boton `#btn-abrir-modal-crear-venta` es `type="button"` y muestra "Nueva Venta".
- `wwwroot/js/venta-crear-modal.js` busca `#btn-abrir-modal-crear-venta` en `DOMContentLoaded` y le agrega `click -> open`.
- `open()` limpia errores, emite `venta-crear-modal:open`, remueve `hidden` de `#modal-crear-venta` y agrega `overflow-hidden` al body.

Cierre:

- `#btn-cerrar-modal-crear-venta` llama `close()`.
- `#modal-crear-venta-backdrop` llama `close()`.
- Escape cierra si el modal esta visible.
- Un boton del banner de caja cerrada llama inline a `document.getElementById('btn-cerrar-modal-crear-venta').click()`.

Submit:

- El form del modal es `<form id="venta-form" method="post" action="/Venta/CreateAjax" novalidate>`.
- El boton final `#btn-confirmar` es `type="button"` y llama `VentaCrearModal.submit()`.
- La barra mobile `.vm-btn-confirm-sm` tambien llama `VentaCrearModal.submit()`.
- `VentaCrearModal.submit()` serializa el form y hace `fetch('/Venta/CreateAjax', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body })`.
- El resultado JSON redirige a Details o a Credito/ConfigurarVenta cuando corresponde.

Dependencias directas de `#modal-crear-venta`:

- `wwwroot/js/venta-modal-rework.js`: guard inicial, query de tablist, reset al evento `venta-crear-modal:open`.
- `wwwroot/css/venta-modal-rework.css`: layout, tablist, sticky aside, details animation y mobile padding acoplados a `#modal-crear-venta`.
- Tests de contrato: esperan root modal, `onclick="VentaCrearModal.submit()"`, guard del JS, step panels y hooks del wizard.

Submodales existentes dentro del modal:

- `#modal-pago-item`
- `#modal-documentacion`

`#modal-confirmar-operacion` no existe en codigo productivo. Solo aparece en documentos anteriores como idea/riesgo y la fase 1F ya documento que no fue implementado.

## G. Mapa de la pagina actual `/Venta/Create`

Ruta:

- `GET /Venta/Create` esta implementado en `VentaController.Create()`.
- Verifica caja abierta con `RedirigirSiCajaCerradaAsync`.
- Carga ViewBags con `CargarViewBags(vendedorUserIdSeleccionado: _currentUser.GetUserId())`.
- Retorna `View("Create_tw", CrearVentaInicial(EstadoVenta.Presupuesto))`.

Post:

- `POST /Venta/Create` recibe `VentaViewModel viewModel, string? DatosCreditoPersonallJson`.
- Limpia ModelState segun tipo de pago.
- Valida vendedor delegado cuando corresponde.
- Valida detalles con `ValidarDetalles(viewModel)`.
- Crea con `_ventaService.CreateAsync(viewModel)`.
- Para `CreditoPersonal` redirige a `Credito/ConfigurarVenta`.
- Para otros medios redirige a `Venta/Details/{id}`.
- En errores vuelve a `Create_tw` con ViewBags recargados.

Vista:

- `Views/Venta/Create_tw.cshtml` ya es pagina standalone de Nueva Venta.
- El form es `<form id="venta-form" asp-action="Create" method="post">`.
- Carga `_VentaModuleStyles`.
- Carga `horizontal-scroll-affordance.js`, `venta-module.js` y `venta-create.js`.
- No carga `venta-crear-modal.js`.
- No carga `venta-modal-rework.js`.

Estructura actual:

- Hero con metricas en vivo.
- Seccion 1: datos generales.
- Seccion 2: seleccion de productos.
- Seccion 3: detalle de cobro.
- Seccion 4: verificacion crediticia.
- Seccion 5: vendedor y observaciones.
- Seccion 6: totales y confirmacion.
- Sticky footer mobile que delega a `document.getElementById('btn-confirmar').click()`.

Conclusiones:

- `/Venta/Create` ya es la ruta natural del ERP para Nueva Venta.
- `Create_tw.cshtml` ya contiene los contratos principales que necesita `venta-create.js`.
- La ruta recomendada no requiere crear endpoint nuevo.
- La pagina puede transformarse en wizard sin cambiar backend, payloads ni endpoints.

## H. Puntos donde se abre el modal

Puntos productivos:

- `Views/Venta/Index_tw.cshtml`: boton `#btn-abrir-modal-crear-venta`.
- `wwwroot/js/venta-crear-modal.js`: listener del boton y API `VentaCrearModal.open()`.

Puntos indirectos:

- `venta-modal-rework.js` escucha `venta-crear-modal:open` para resetear el wizard al paso cliente.
- `venta-create.js` escucha `venta-crear-modal:open` y `venta-crear-modal:close` para invalidar verificacion crediticia.

No se encontraron otros botones productivos que llamen `VentaCrearModal.open()` directamente.

## I. Contratos criticos

La pagina wizard debe preservar estos contratos, porque `venta-create.js`, tests o specs los usan:

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

Contratos adicionales observados:

- Antiforgery token.
- `asp-for` y `name` de campos del `VentaViewModel`.
- `DatosTarjeta.*`, `DatosCheque.*` y campos de credito/documentacion.
- `#panel-verificacion-crediticia`.
- `#btn-verificar-elegibilidad`.
- `#hdn-aplicar-excepcion`.
- `#btn-aplicar-excepcion`, `#btn-confirmar-excepcion`, `#btn-cancelar-excepcion`.
- `#sticky-create-total` en `Create_tw`.
- Hooks del wizard actual: `#step-btn-*`, `#step-panel-*`, `#vm-estado-global`, `data-side-*`, `data-rev-*`, `data-pago-summary`.

El contrato a retirar gradualmente es `#modal-crear-venta` como root principal.

## J. JS/CSS reutilizable

Reutilizable de `venta-modal-rework.js`:

- Definicion de pasos: cliente, productos, pago, credito, revision.
- `activateStep`.
- `initWizardTabs`.
- `evaluateStepStates`.
- `refreshState`.
- `goToFirstInvalidStep`.
- Observers sobre cliente, productos, totales y pago.
- Sincronizacion de resumenes visuales.
- Sincronizacion de estado global.
- Navegacion al primer paso invalido al confirmar.

Debe adaptarse:

- Guard inicial: cambiar de `#modal-crear-venta` a un root de pagina, por ejemplo `#venta-page-wizard`.
- Query de tablist: no debe depender de `#modal-crear-venta`.
- Reset: debe ejecutarse en carga de pagina y no depender solo de `venta-crear-modal:open`.
- Submit: debe convivir con submit nativo del form de `/Venta/Create`, sin `VentaCrearModal.submit()` como requisito.
- Nombres: mantener `venta-modal-rework.js` durante la primera migracion para reducir riesgo; renombrar a `venta-page-wizard.js` en una fase separada si ya no hay dependencias.

Reutilizable de `venta-modal-rework.css`:

- Clases `.vm-step-tab`, `.vm-step-tab--active`, `.vm-step-tab--complete`, `.vm-step-tab--warning`.
- `.vm-step-panel-active`.
- Estado visual `#vm-estado-global` con clases `vm-estado--listo`, `vm-estado--alerta`, `vm-estado--error`.
- Ajustes de sticky/resumen si se desacoplan del root modal.

Debe adaptarse:

- Selectores `#modal-crear-venta > main`.
- Selectores `#modal-crear-venta form > div.grid`.
- Selectores `#modal-crear-venta [role="tablist"]`.
- Selectores `#modal-crear-venta aside`.
- Selectores `#modal-crear-venta details > div`.
- Comentarios y nombres que presentan el componente como modal.

Reutilizable de `venta-module.css`:

- Clases `vm-*` de alta visibilidad.
- `vm-btn-confirm`, `vm-btn-confirm-sm`, `vm-mobile-summary-bar` si se decide mantener esa semantica visual.
- Clases actuales `venta-*` de la pagina standalone.

## K. Que se debe eliminar o desactivar

En fases de implementacion, no en esta:

1. Reemplazar el boton `Nueva Venta` de `Index_tw.cshtml`.
   - Actual: `button#btn-abrir-modal-crear-venta`.
   - Futuro recomendado: link `asp-action="Create"` o `href="/Venta/Create"`.

2. Dejar de renderizar `<partial name="_VentaCrearModal" />` en `Index_tw.cshtml`.
   - Riesgo de borrarlo directamente: alto mientras tests y scripts sigan esperando modal.
   - Recomendacion: primero redirigir accesos a pagina y adaptar tests; luego remover render.

3. Dejar de cargar `venta-crear-modal.js` desde `Index_tw.cshtml` cuando el modal ya no se renderice.

4. Dejar de cargar `venta-modal-rework.js` desde `Index_tw.cshtml` y cargarlo desde `Create_tw.cshtml` cuando este adaptado a pagina.

5. Eliminar o dejar legacy `_VentaCrearModal.cshtml`.
   - Recomendacion: no borrarlo en 1A. Dejarlo sin uso temporalmente hasta que tests y QA protejan la pagina.

6. No implementar `#modal-confirmar-operacion`.
   - No existe contrato real.
   - El usuario no lo pidio.
   - La confirmacion real debe ser el submit final de la pagina.

## L. Que se debe migrar

Debe migrarse del modal a pagina:

- Skeleton wizard de pasos.
- Header/estado del wizard.
- Step tabs y panels.
- Sidebar/resumen de operacion.
- Resumen de pago y revision.
- Estado visual listo/alerta/error.
- Navegacion al primer paso invalido.
- Sincronizacion de totales, cliente, productos y pago.

Debe mantenerse en `venta-create.js`:

- Busqueda y seleccion de cliente.
- Busqueda y seleccion de productos.
- Render de detalles.
- Calculo y escritura de totales/hidden inputs.
- Validacion de trazabilidad/unidad.
- Configuracion global de pagos.
- Paneles de tarjeta, cheque, credito.
- Verificacion crediticia.
- Excepcion documental.
- Submit guard del form.

No debe migrarse como dependencia de pagina:

- `VentaCrearModal.open()`.
- `VentaCrearModal.close()`.
- `VentaCrearModal.submit()` como confirmacion principal.
- Overlay root `#modal-crear-venta`.
- Backdrop y cierre por Escape del modal principal.

## M. Ruta recomendada

Ruta recomendada: usar `/Venta/Create` como pagina wizard principal.

Motivos:

- Ya existe `GET /Venta/Create`.
- Ya existe `POST /Venta/Create`.
- Ya usa `Create_tw.cshtml`.
- Ya valida caja abierta.
- Ya carga ViewBags necesarios.
- Ya esta protegida por permisos.
- Ya redirige correctamente a Details o ConfigurarVenta.
- Ya esta usada por e2e `venta-pago-por-item.spec.js`.
- Evita crear `/Venta/Nueva` como ruta paralela y duplicar deuda.

No se recomienda crear `/Venta/Nueva` en esta etapa, salvo que haya una decision de producto de URL publica. Si se crea, deberia ser alias o redirect hacia `Create`, no una segunda action con logica propia.

## N. Plan de implementacion

### KIRA-VENTAS-PAGE-REWORK-1A

Crear pagina wizard Nueva Venta usando `/Venta/Create`.

Alcance:

- Adaptar `Create_tw.cshtml` para usar el skeleton de wizard.
- Mantener `<form id="venta-form" asp-action="Create" method="post">`.
- Agregar root de pagina, sugerido: `#venta-page-wizard`.
- Reutilizar estructura visual del modal sin overlay.
- Preservar todos los IDs, names, `asp-for`, antiforgery y data hooks.
- Cargar temporalmente `venta-modal-rework.css` y `venta-modal-rework.js` si se adaptan con guard dual.

No tocar:

- Controller.
- Services.
- Entidades.
- Migraciones.
- Endpoints.
- Payloads.

### KIRA-VENTAS-PAGE-REWORK-1B

Reemplazar accesos al modal por navegacion a pagina.

Alcance:

- En `Index_tw.cshtml`, convertir `Nueva Venta` en link a `/Venta/Create`.
- Mantener comportamiento bloqueado cuando no hay caja abierta.
- Remover o desactivar render del partial `_VentaCrearModal` solo cuando la pagina ya este protegida por tests.
- Remover carga de `venta-crear-modal.js` del index si ya no hay modal.

### KIRA-VENTAS-PAGE-REWORK-1C

Adaptar JS de modal a pagina.

Alcance:

- Hacer que `venta-modal-rework.js` funcione con `#venta-page-wizard` y no requiera `#modal-crear-venta`.
- Ejecutar reset inicial en carga de pagina.
- Mantener compatibilidad temporal si el modal sigue existiendo en tests legacy.
- Evaluar renombre futuro a `venta-page-wizard.js` en fase separada.
- Preservar `venta-create.js` como fuente funcional.

### KIRA-VENTAS-PAGE-REWORK-1D

Validar flujo cliente/producto/totales.

Alcance:

- Confirmar busqueda de cliente.
- Confirmar busqueda y agregado de producto.
- Confirmar render de detalles.
- Confirmar hidden inputs.
- Confirmar totales visibles y submit habilitado.

### KIRA-VENTAS-PAGE-REWORK-1E

Validar pago, credito, documentacion y excepcion.

Alcance:

- Pago principal.
- Tarjeta, cheque, credito personal.
- Verificacion crediticia.
- Mora, cupo y documentacion.
- Excepcion documental.

### KIRA-VENTAS-PAGE-REWORK-1F

Confirmacion real sin submodal extra.

Alcance:

- Confirmar que el boton final usa submit real de `#venta-form`.
- Confirmar que no existe `#modal-confirmar-operacion`.
- Confirmar redirecciones a Details o ConfigurarVenta.
- Confirmar errores de ModelState visibles en pagina.

### KIRA-VENTAS-PAGE-REWORK-QA

QA desktop/mobile/regresion.

Alcance:

- Playwright desktop y mobile.
- Contratos UI.
- VentaCreate.
- Regresion de e2e `venta-pago-por-item.spec.js` si sigue vigente.
- Smoke manual con caja abierta.

## O. Riesgos

- `venta-create.js` comparte logica entre modal y pagina, pero tiene listeners de evento `venta-crear-modal:open/close`; en pagina deben quedar inofensivos o reemplazarse por init de pagina.
- `venta-modal-rework.js` hoy no corre en `Create_tw` por el guard de `#modal-crear-venta`.
- `venta-modal-rework.css` tiene selectores acoplados al root modal.
- Tests de contrato esperan el modal como superficie activa.
- Borrar `_VentaCrearModal.cshtml` directamente romperia tests y referencias documentadas.
- Dejar modal y pagina activos duplica el flujo y reintroduce deuda.
- `CreateAjax` y `Create` tienen respuestas distintas: JSON vs redireccion/vista. La pagina debe preferir `Create` salvo decision explicita de AJAX.
- Cotizacion usa `Create_tw.cshtml` con `EstadoVenta.Cotizacion`; cambios de skeleton deben preservar `esCotizacion`.
- Caja, stock, credito, documentacion y autorizacion no deben cambiar.
- El submodal de pago por item aparece en modal/tests legacy; en `Create_tw` actual los tests de contrato afirman que no se muestra pago por item en tabla. No reactivar pago por producto en Nueva Venta sin decision funcional explicita.
- `#modal-confirmar-operacion` no existe; implementarlo ahora seria agregar un flujo no pedido.
- Uso de `innerHTML` en `venta-create.js` existe en varias zonas; no ampliarlo al migrar.

## P. Roadmap por fases

1. `KIRA-VENTAS-PAGE-REWORK-1A`: pagina wizard en `/Venta/Create`, skeleton sin overlay.
2. `KIRA-VENTAS-PAGE-REWORK-1B`: boton Nueva Venta navega a pagina; modal deja de ser entrada principal.
3. `KIRA-VENTAS-PAGE-REWORK-1C`: JS wizard desacoplado de `#modal-crear-venta`.
4. `KIRA-VENTAS-PAGE-REWORK-1D`: cliente, productos, detalles y totales.
5. `KIRA-VENTAS-PAGE-REWORK-1E`: pago, credito, documentacion y excepcion.
6. `KIRA-VENTAS-PAGE-REWORK-1F`: confirmacion real sin submodal extra.
7. `KIRA-VENTAS-PAGE-REWORK-QA`: Playwright desktop/mobile y regresion.
8. Fase posterior opcional: renombrar `venta-modal-rework.*` a `venta-page-wizard.*` cuando no haya dependencias legacy.
9. Fase posterior opcional: eliminar `_VentaCrearModal.cshtml` cuando tests y QA confirmen que ya no es contrato activo.

## Q. Validaciones necesarias

Para esta fase doc-only:

- `git diff --check`
- `git status --short`

Para fases que toquen Razor/JS/CSS:

- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `dotnet test --configuration Release --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard"`
- Revisar `git diff --check`

Para fases que toquen confirmacion o flujo funcional:

- Tests de venta afectados.
- Tests de credito/documentacion si se toca ese flujo.
- No tocar stock/caja/credito salvo validacion explicita.

## R. Playwright necesario

Cuando haya implementacion:

- `npx.cmd playwright test e2e/venta-pago-por-item.spec.js`
- Smoke visual desktop de `/Venta/Create`.
- Smoke visual mobile de `/Venta/Create`.
- Verificar que `/Venta` boton Nueva Venta navega a `/Venta/Create`.
- Verificar que no se abre `#modal-crear-venta`.
- Verificar que no existe ni se abre `#modal-confirmar-operacion`.

Si se toca cotizacion o conversion por impacto en `Create_tw.cshtml`:

- `npx.cmd playwright test e2e/cotizacion-simulador.spec.js`
- `npx.cmd playwright test e2e/cotizacion-conversion.spec.js`

## S. Proximo prompt recomendado

```text
PROMPT - KIRA-VENTAS-PAGE-REWORK-1A - Crear pagina wizard Nueva Venta en /Venta/Create

Actua como Kira y segui AGENTS.md / CLAUDE.md.

Base esperada: main con docs/kira-ventas-page-rework-0-arquitectura.md integrado.

Objetivo:
Adaptar Views/Venta/Create_tw.cshtml para que /Venta/Create sea la pagina fullscreen tipo wizard de Nueva Venta, reutilizando el skeleton util del rework modal pero sin overlay ni dependencia de #modal-crear-venta.

Alcance:
- Tocar solo Razor/CSS/JS necesarios para el skeleton de pagina.
- Mantener form #venta-form con asp-action="Create" method="post".
- Preservar antiforgery, asp-for, name, id, data-* y contratos JS.
- No tocar controllers, services, models, migrations, endpoints ni payloads.
- No implementar #modal-confirmar-operacion.
- No borrar _VentaCrearModal.cshtml todavia.

Validar:
- dotnet build --configuration Release
- dotnet test --configuration Release --filter "VentaCreate"
- git diff --check
- git status --short
```

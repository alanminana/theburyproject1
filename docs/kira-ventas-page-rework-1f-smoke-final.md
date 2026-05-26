# KIRA-VENTAS-PAGE-REWORK-1F - Smoke final /Venta -> /Venta/Create

## A. Objetivo

Validar visual y funcionalmente que Nueva Venta usa `/Venta/Create` como pagina wizard principal, sin modal legacy, sin `CreateAjax` como flujo principal y sin submodal extra de confirmacion.

## B. Base y contexto

Base esperada: `main` en `1a72b4e` - `KIRA-VENTAS-PAGE-REWORK-1E` integrada.

La app local no estaba escuchando inicialmente en `http://localhost:5187`, por lo que se levanto una instancia propia con `dotnet run --project TheBuryProyect.csproj --launch-profile http`.

## C. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`
- `docs/kira-ventas-page-rework-1b-reemplazar-accesos-modal.md`
- `docs/kira-ventas-page-rework-1c-desacoplar-modal.md`
- `docs/kira-ventas-page-rework-1d-cliente-producto-totales.md`
- `docs/kira-ventas-page-rework-1e-pago-credito-documentacion.md`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/Create_tw.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## D. Archivos modificados

- `docs/kira-ventas-page-rework-1f-smoke-final.md`

No hubo cambios productivos en Razor, JavaScript, CSS ni tests.

## E. Resultado /Venta

`/Venta` cargo correctamente luego de login con usuario `Admin`.

El acceso visible `Nueva Venta` es un link (`A`) hacia `http://localhost:5187/Venta/Create`.

No se encontro `#btn-abrir-modal-crear-venta`.

No se encontro `#modal-crear-venta` renderizado en `/Venta`.

## F. Resultado navegacion a /Venta/Create

Click en `Nueva Venta` navego a:

```text
http://localhost:5187/Venta/Create
```

No se abrio modal, overlay ni backdrop.

## G. Resultado desktop

Viewport usado:

```text
1440x900
```

Resultado:

- `/Venta/Create` cargo como pagina.
- Existe `#venta-create-page`.
- El formulario mantiene `action="/Venta/Create"` y `method="post"`.
- `#btn-confirmar` mantiene `type="submit"`.
- Se ven los pasos Cliente, Productos, Pago, Credito y Revision.
- Existe sidebar `aside.vm-sidebar`.
- `#btn-confirmar` esta visible.
- No existe `#modal-crear-venta`.
- No existe `#modal-confirmar-operacion`.
- Consola sin errores ni warnings criticos.

## H. Resultado mobile

Viewport usado:

```text
390x844
```

Resultado:

- `/Venta` cargo y el link `Nueva Venta` navego a `/Venta/Create`.
- `/Venta/Create` cargo con `#venta-create-page`.
- Los tabs/pasos son visibles y scrolleables horizontalmente.
- El paso inicial Cliente queda usable.
- La barra mobile summary esta visible.
- `#btn-confirmar` existe y es accesible mediante scroll.
- `document.documentElement.scrollWidth` fue menor que el viewport, sin overflow horizontal critico.
- No existe `#modal-crear-venta`.
- No existe `#modal-confirmar-operacion`.
- Consola sin errores ni warnings criticos.

## I. Resultado confirmacion final

Se hizo click en `#btn-confirmar` con formulario incompleto para validar el comportamiento sin crear venta real.

Resultado:

- La pagina permanecio en `/Venta/Create`.
- Aparecio validacion de formulario.
- No se abrio modal extra.
- No se genero `#modal-confirmar-operacion`.
- El submit sigue siendo nativo del form.

No se confirmo una venta real para no impactar datos.

## J. Validacion de ausencia de modal

Validado por codigo y navegador:

- `/Venta` no renderiza `#modal-crear-venta`.
- `/Venta/Create` no renderiza `#modal-crear-venta`.
- No se abrio backdrop al navegar.
- El unico modal permitido observado en la pagina es `#modal-documentacion`, oculto por defecto y ajeno a la confirmacion principal.

## K. Validacion de ausencia de #modal-confirmar-operacion

Validado por codigo y navegador:

- `Create_tw.cshtml` no contiene `id="modal-confirmar-operacion"`.
- DOM runtime en desktop y mobile no contiene `#modal-confirmar-operacion`.
- Click en `#btn-confirmar` no lo crea ni lo abre.

## L. Errores encontrados

No se encontraron errores productivos que requieran microfix.

Observacion menor: el arranque inicial de la app tardo varios minutos en fase `Compilando...`, pero finalmente quedo disponible en `localhost:5187`.

## M. Correcciones aplicadas

No se aplicaron correcciones productivas.

La fase queda como cierre QA/documentacion.

## N. Contratos preservados

Preservados por auditoria y smoke:

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
- `#panel-tarjeta`
- `#panel-cheque`
- `#panel-mercadopago`
- `#panel-credito-personal`
- `#panel-documentacion-faltante`
- `#modal-documentacion`
- `#panel-excepcion-crediticia`
- `#VendedorUserId`
- `#Observaciones`
- `AplicarExcepcionDocumental`
- `MotivoExcepcionDocumentalCreate`
- `asp-action="Create"`
- antiforgery token

## O. Que no se toco

- Controllers.
- Services.
- Models.
- Migrations.
- Endpoints.
- Payloads.
- Stock.
- Caja backend.
- Credito backend.
- Cotizacion backend.
- `_VentaCrearModal.cshtml`.
- `venta-crear-modal.js`.
- Playwright specs.
- Reglas de negocio.
- Calculo backend.
- Rutas.

## P. Riesgo funcional

Riesgo bajo.

No hubo cambios productivos. La validacion confirma que el camino principal es pagina wizard con POST nativo a `Create`.

## Q. Validaciones ejecutadas

Previstas/ejecutadas:

- `node --check wwwroot/js/venta-modal-rework.js`
- `node --check wwwroot/js/venta-create.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

## R. Tests ejecutados

`dotnet test --configuration Release --filter "VentaCreate"`:

- Resultado: OK.
- Total esperado observado: 142 tests de VentaCreate.

## S. Playwright/manual ejecutado u omitido

Ejecutado con Playwright MCP y app local real en:

- `http://localhost:5187/Venta`
- `http://localhost:5187/Venta/Create`

Desktop:

- `1440x900`.

Mobile:

- `390x844`.

Flujo funcional minimo:

- Buscar y seleccionar cliente `jhon doe`.
- Buscar y seleccionar producto `televisor samsung 40 pulgadas`.
- Agregar 1 producto.
- Verificar detalle, hidden inputs, subtotal y total.
- Ir a Revision.
- No confirmar venta real.

## T. Deudas restantes

- La limpieza/renombre futuro de `venta-modal-rework.*` a naming de pagina sigue pendiente.
- `_VentaCrearModal.cshtml` y `venta-crear-modal.js` siguen existiendo como legacy no principal.
- QA final amplio puede correr una fase separada sobre flujos completos con datos controlados.

## U. Proximo prompt recomendado

```text
KIRA-VENTAS-PAGE-REWORK-QA - QA visual y funcional final de Nueva Venta pagina wizard
```

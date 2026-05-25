# KIRA-VENTAS-PAGE-REWORK-1E - Pago, credito y documentacion en /Venta/Create

## A. Objetivo

Validar que `/Venta/Create` mantiene el flujo de pago, credito, documentacion faltante y excepcion documental como pagina wizard principal, sin volver al modal legacy ni cambiar reglas de negocio.

## B. Base y contexto

Base: `main` en `8c501a6` - `KIRA-VENTAS-PAGE-REWORK-1D` integrada.

Contexto heredado:

- `/Venta/Create` es la superficie canonica de Nueva Venta.
- `/Venta` navega a `/Venta/Create`.
- El formulario conserva POST nativo con `asp-action="Create"` y antiforgery.
- `venta-create.js` conserva la logica funcional de pagos, credito, documentacion y submit guard.
- `venta-modal-rework.js` sincroniza estado visual, wizard, sidebar y revision.

## C. Archivos auditados

- `docs/kira-ventas-page-rework-0-arquitectura.md`
- `docs/kira-ventas-page-rework-1a-create-wizard.md`
- `docs/kira-ventas-page-rework-1b-reemplazar-accesos-modal.md`
- `docs/kira-ventas-page-rework-1c-desacoplar-modal.md`
- `docs/kira-ventas-page-rework-1d-cliente-producto-totales.md`
- `Views/Venta/Create_tw.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-modal-rework.js`
- `wwwroot/css/venta-modal-rework.css`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## D. Archivos modificados

- `Views/Venta/Create_tw.cshtml`
- `wwwroot/js/venta-create.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-page-rework-1e-pago-credito-documentacion.md`

No se modifico CSS.

## E. QA tipo de pago

Auditoria por codigo:

- `#select-tipo-pago` sigue visible en el paso Cliente.
- `venta-create.js` sigue escuchando `change` con `onTipoPagoChange`.
- Sidebar, revision y confirmacion siguen leyendo el medio seleccionado mediante `data-side-pago`, `data-rev-pago`, `data-pago-summary` y `data-conf-pago`.
- `venta-modal-rework.js` observa `#select-tipo-pago` y refresca estados visuales.

## F. QA tarjeta/cheque/mercado pago

Auditoria por codigo:

- Tarjeta conserva `#panel-tarjeta`, `#select-tarjeta`, `#select-cuotas-tarjeta`, `#hdn-configuracion-pago-plan-id`, snapshot `DatosTarjeta.NombreTarjeta` y `DatosTarjeta.TipoTarjeta`.
- Cheque conserva `#panel-cheque` y los campos `DatosCheque.*`.
- Planes conserva `#panel-planes-pago` y `#lista-planes-pago`.
- Se agrego `#panel-mercadopago` como panel visual liviano para el contrato de la pagina.
- `venta-create.js` muestra/oculta `#panel-mercadopago` cuando el tipo de pago es Mercado Pago y mantiene el snapshot existente de Mercado Pago sin cambiar payload.

## G. QA credito personal/cuenta corriente

Auditoria por codigo:

- `#panel-credito-personal`, `#panel-verificacion-crediticia` y `#btn-verificar-elegibilidad` se mantienen.
- `venta-modal-rework.js` ya marcaba como crediticios los tipos `5` y `7`.
- Se alineo `venta-create.js` para tratar Credito Personal y Cuenta Corriente como medios que muestran UI crediticia.
- La verificacion sigue usando el endpoint existente `/api/ventas/PrevalidarCredito` sin cambiar request ni respuesta.

## H. QA documentacion faltante

Auditoria por codigo:

- Se conserva `#panel-documentacion-faltante`, `#lista-docs-faltantes` y `#btn-cargar-documentacion`.
- Se conserva `#modal-documentacion` con `data-venta-modal="documentacion"`.
- La carga de documentacion sigue usando el flujo existente de `ventaModule.bindModal('documentacion')` y POST AJAX a `/DocumentoCliente/Upload`.
- No se cambiaron endpoints ni payloads de documentacion.

## I. QA excepcion documental

Auditoria por codigo:

- Se conserva `#hdn-aplicar-excepcion` con `asp-for="AplicarExcepcionDocumental"`.
- Se conserva `#txt-excepcion-documental` con `asp-for="MotivoExcepcionDocumentalCreate"`.
- Se conserva `#btn-aplicar-excepcion`, `#panel-excepcion-inactiva`, `#panel-excepcion-activa`, `#btn-confirmar-excepcion` y `#btn-cancelar-excepcion`.
- Se agrego el wrapper faltante `#panel-excepcion-crediticia`, requerido por `venta-create.js` para mostrar/ocultar la excepcion solo cuando la prevalidacion es exceptuable.

## J. QA revision/confirmacion

Auditoria por codigo:

- `#btn-confirmar` sigue siendo `type="submit"`.
- El form sigue siendo `<form id="venta-form" asp-action="Create" method="post">`.
- No existe `#modal-confirmar-operacion` en `Create_tw.cshtml`.
- No se llama `VentaCrearModal.submit()` desde la pagina.
- `venta-modal-rework.js` solo navega visualmente al primer paso invalido al clickear confirmar; no llama `preventDefault`.

## K. Errores encontrados

- `#panel-excepcion-crediticia` faltaba en `Create_tw.cshtml`, aunque `venta-create.js` lo referencia para activar/desactivar el bloque de excepcion.
- `#panel-mercadopago` faltaba en `Create_tw.cshtml`, aunque forma parte del contrato critico de esta fase.
- Cuenta Corriente estaba contemplada por el estado visual del wizard, pero `venta-create.js` solo mostraba el panel crediticio para Credito Personal.

## L. Correcciones aplicadas

- Agregado `#panel-mercadopago` como panel visual oculto por defecto.
- Agregado wrapper `#panel-excepcion-crediticia` oculto por defecto alrededor de la excepcion documental.
- Agregado `panelMercadoPago` y toggle visual en `onTipoPagoChange`.
- Agregado helper `esTipoPagoCredito` para Credito Personal y Cuenta Corriente.
- Ajustada la verificacion crediticia para permitir ambos tipos crediticios sin cambiar endpoint ni payload.
- Agregados tests de contrato para pago, credito, documentacion, excepcion, ausencia de modal de confirmacion y estados JS.

## M. Contratos preservados

Preservados:

- `#venta-create-page`, `#venta-form`, `#btn-confirmar`.
- `#select-tipo-pago`.
- `#panel-tarjeta`, `#panel-cheque`, `#panel-mercadopago`, `#panel-credito-personal`.
- `#panel-planes-pago`, `#lista-planes-pago`, `#configuracion-pagos-global-estado`, `#hdn-configuracion-pago-plan-id`.
- `#panel-diagnostico-condiciones-pago`, `#panel-verificacion-crediticia`, `#btn-verificar-elegibilidad`.
- `#panel-resultado-verificacion`, `#panel-cupo-suficiente`, `#panel-cupo-insuficiente`, `#panel-alerta-mora`.
- `#panel-documentacion-faltante`, `#lista-docs-faltantes`, `#btn-cargar-documentacion`, `#modal-documentacion`, `#btn-subir-documento`.
- `#panel-excepcion-crediticia`, `#hdn-aplicar-excepcion`, `#btn-aplicar-excepcion`, `#panel-excepcion-inactiva`, `#panel-excepcion-activa`, `#txt-excepcion-documental`, `#btn-cancelar-excepcion`, `#btn-confirmar-excepcion`.
- `#total-final`, `#hdn-total`, `#VendedorUserId`, `#Observaciones`.
- `asp-action="Create"`, antiforgery, `AplicarExcepcionDocumental`, `MotivoExcepcionDocumentalCreate`.

## N. Que no se toco

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

## O. Riesgo funcional

Riesgo bajo-medio.

Los cambios son de presencia de hooks/paneles y visibilidad UI. No se cambia el POST nativo, no se agregan endpoints y no se modifica la serializacion de venta.

## P. Validaciones

Validaciones previstas:

- `node --check wwwroot/js/venta-modal-rework.js`
- `node --check wwwroot/js/venta-create.js`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `git diff --check`
- `git status --short`

## Q. Tests ejecutados

- `node --check wwwroot/js/venta-modal-rework.js` - OK.
- `node --check wwwroot/js/venta-create.js` - OK adicional por tocar `venta-create.js`.
- `dotnet build --configuration Release` - imprimio compilacion correcta con 0 errores, pero excedio el timeout de la herramienta al final.
- `dotnet build --configuration Release --no-restore -nodeReuse:false` - OK, 0 warnings, 0 errores.
- `dotnet test --configuration Release --filter "VentaCreate"` - OK.

Resultado exacto de tests:

- 142 passed.
- 0 failed.
- 0 skipped.
- Total: 142.

## R. Playwright/manual ejecutado u omitido

Smoke manual/browser omitido.

Motivo: `Invoke-WebRequest http://localhost:5187/Venta/Create` fallo con "No es posible conectar con el servidor remoto". No habia app local disponible en `localhost:5187` para validar desktop 1440x900 ni mobile 390x844.

No se ejecuto Playwright porque la fase no lo exige como validacion obligatoria y no habia servidor disponible.

## S. Deudas restantes

- Smoke browser real desktop 1440x900 y mobile 390x844 si la app esta disponible.
- Confirmar con datos reales la verificacion crediticia y documentacion en ambiente con seed operativo.

## T. Proximo prompt recomendado

KIRA-VENTAS-PAGE-REWORK-1F - Confirmacion final y smoke visual `/Venta` -> `/Venta/Create`.

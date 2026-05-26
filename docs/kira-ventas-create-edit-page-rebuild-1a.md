# KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1A

## A. Objetivo

Recrear `Create_tw.cshtml` y `Edit_tw.cshtml` de ventas como páginas fullscreen tipo wizard, no como modal, preservando endpoints, payloads y reglas backend.

## B. Motivo del rebuild

El usuario eliminó manualmente las vistas porque no seguían la estética visual deseada. La fase reconstruye la experiencia como página operativa dark, compacta y navegable por pasos.

## C. HTML objetivo usado

Se buscó `Modal Nueva venta.html` dentro de `e:\theburyproject1`, pero no estaba disponible en el working tree. Se usó la descripción visual del prompt como referencia y `origin/main` como contrato funcional.

## D. Por qué NO es modal

Create/Edit quedan como páginas normales con `#venta-create-page` y `#venta-edit-page`. No se reintrodujo root `#modal-crear-venta`, backdrop principal, botón cerrar principal, `role="dialog"`, `aria-modal="true"`, `CreateAjax`, `VentaCrearModal.submit()` ni `#modal-confirmar-operacion`.

## E. Contrato Create recuperado desde origin/main

Se recuperó `Views/Venta/Create_tw.cshtml` desde `origin/main` como fuente de IDs, names, antiforgery, POST nativo `asp-action="Create"`, totales hidden, selector de cliente/producto, paneles de pago, documentación y excepción crediticia.

## F. Contrato Edit recuperado desde origin/main

Se recuperó `Views/Venta/Edit_tw.cshtml` desde `origin/main` preservando modelo `VentaViewModel`, POST `asp-action="Edit"`, antiforgery, `Id`, `Estado`, `RowVersion`, seed de detalles existentes, campos editables, validaciones y acciones de guardar/cancelar.

## G. Archivos auditados

- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/Edit_tw.cshtml`
- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/js/venta-create.js`
- `wwwroot/js/venta-module.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `TheBuryProyect.Tests/Unit/VentaEditUiContractTests.cs`

## H. Archivos modificados

- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/Edit_tw.cshtml`
- `wwwroot/css/venta-page-wizard.css`
- `wwwroot/js/venta-page-wizard.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

## I. Qué se adaptó visualmente

Header sticky compacto, contenedor máximo `1400px`, tabs Cliente/Productos/Pago/Crédito/Revisión, cards dark compactas, campos compactos, resumen mobile, métricas superiores y estados visuales del wizard.

## J. Qué se descartó del HTML objetivo

No se copiaron estructura `html/head/body`, CDN, estilos inline, scripts inline, comentarios de modal, backdrop principal, root modal, cerrar modal principal, `CreateAjax`, confirmación modal ni datos demo hardcodeados.

## K. Create: contratos preservados

Se preservaron `#venta-create-page`, `#venta-form`, `asp-action="Create"`, antiforgery, `#btn-confirmar`, cliente, producto, detalles, tipo de pago, totales hidden, paneles de tarjeta/cheque/MercadoPago/crédito, documentación, excepción, `VendedorUserId`, `Observaciones` y scripts funcionales existentes.

## L. Edit: contratos preservados

Se preservaron POST nativo a Edit, antiforgery, `Id`, `Estado`, `RowVersion`, seed de detalles con `ventaInicialJson`, campos de cliente/producto/pago/crédito, datos de tarjeta, vendedor, observaciones, totales hidden, cancelación y guardado.

## M. Qué no se tocó

No se tocaron controllers, services, models, migrations, data, endpoints, payloads, stock, caja backend, crédito backend, cotización backend, `Index_tw.cshtml`, `Views/Producto/Unidades.cshtml`, `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json` ni `skills-lock.json`.

## N. Riesgo funcional

Riesgo medio: se cambió estructura visible de Edit a tabs wizard y se agregó un JS defensivo para navegación/resumen. El backend y el JS funcional de venta se mantienen como autoridad.

## O. Validaciones ejecutadas

- `node --check wwwroot/js/venta-page-wizard.js`
- `node --check wwwroot/js/venta-create.js`
- `node --check wwwroot/js/venta-module.js`
- `dotnet build --configuration Release`
- `dotnet build --configuration Release --no-restore -nodeReuse:false`
- `dotnet test --configuration Release --filter "VentaCreate"`
- `dotnet test --configuration Release --filter "IndexView"`
- `dotnet test --configuration Release --filter "VentaEdit"`
- `dotnet test --configuration Release --no-restore -nodeReuse:false --filter "VentaEdit"`

## P. Resultado build

`dotnet build --configuration Release`: correcto, 0 warnings, 0 errores.

Build final con `--no-restore -nodeReuse:false`: correcto, 0 warnings, 0 errores.

## Q. Resultado tests

- `VentaCreate`: 143/143 passed.
- `IndexView`: 3/3 passed.
- `VentaEdit`: primer intento bloqueado por file-lock en `obj\Release\net8.0\rpswa.dswa.cache.json`; repetido con `--no-restore -nodeReuse:false`, 12/12 passed.

## R. Resultado Playwright/manual

No se ejecutó Playwright ni smoke manual en navegador porque no había app levantada durante esta fase. Queda para `KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1B`.

## S. Deudas restantes

- QA visual desktop/mobile real de `/Venta/Create`.
- QA visual desktop/mobile real de `/Venta/Edit/{id}` con venta existente.
- Confirmar visualmente que el HTML objetivo subido coincide con lo implementado cuando el archivo esté disponible en el repo o como adjunto accesible.

## T. Próximo prompt recomendado

`KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1B — QA visual desktop/mobile de Create/Edit como páginas.`

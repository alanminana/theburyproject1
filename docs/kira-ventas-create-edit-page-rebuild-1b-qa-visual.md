# KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1B - QA visual Create/Edit

## A. Objetivo

Validar `/Venta/Create` y `/Venta/Edit/{id}` como paginas wizard dark fullscreen, no modal, en desktop/mobile, con contratos funcionales preservados y sin confirmacion modal extra.

## B. Rama y commit auditado

- Rama: `kira/ventas-create-edit-page-rebuild-1a`.
- Commit base auditado: `64b1aff Recrear create y edit de ventas como paginas wizard (KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1A)`.

## C. HTML objetivo usado o disponibilidad

`Modal Nueva venta.html` no estuvo disponible en el workspace. Se mantuvo la aclaracion critica: usarlo como referencia de pagina fullscreen/wizard, no como modal. Se valido contra el contrato documentado en 1A y contra runtime local.

## D. URLs auditadas

- `http://localhost:5187/Venta/Create`
- `http://localhost:5187/Venta/Edit/5164`

`/Venta` no expuso links `Edit`; se uso ruta directa con una cotizacion editable existente: `COT-202605-000225`.

## E. Resultado Create desktop

OK con microfix. En 1440x900 carga como `#venta-create-page`, titulo `Nueva Venta`, header sticky, estado global `Incompleta`, tabs Cliente/Productos/Pago/Credito/Revision, Cliente activo, cards dark compactas, sidebar sticky en columna derecha y `#btn-confirmar` presente. Mobile bar no se muestra en desktop.

## F. Resultado Create mobile

OK. En 390x844 carga sin overflow horizontal critico (`overflowDelta: 0`), header usable, tabs con scroll horizontal, cards en columna, mobile total visible y sidebar pasa a flujo vertical sin romper layout.

## G. Resultado Create funcional

Parcial sin crear venta real. Se confirmo disponibilidad de datos por API (`BuscarClientes`: 3 resultados, `BuscarProductos`: 3 resultados), navegacion a Revision y ausencia de `#modal-confirmar-operacion` antes/despues. No se ejecuto submit final para no crear venta real.

## H. Resultado Edit desktop

OK con microfix. `/Venta/Edit/5164` carga como `#venta-edit-page`, titulo `Editar Cotizacion`, datos existentes con 1 producto y total `$ 121,00`, hidden inputs de detalle presentes, header/tabs/cards con estetica nueva y columna derecha sticky para verificacion/totales. Acciones Guardar cambios/Cancelar visibles en header.

## I. Resultado Edit mobile

OK. En 390x844 carga sin overflow horizontal critico (`overflowDelta: 0`), tabs scrolleables, mobile summary visible y columna derecha pasa a flujo vertical. Acciones principales accesibles desde header/mobile bar.

## J. Resultado Edit funcional

OK smoke sin guardar cambios. Se valido carga de `Id`, `RowVersion`, seed de venta inicial, 6 hidden inputs de detalle y total existente. No se ejecuto submit final.

## K. Validacion ausencia de modal principal

OK. Runtime Create/Edit:

- No existe `#modal-crear-venta`.
- No existe `#modal-crear-venta-backdrop`.
- No existe `#btn-cerrar-modal-crear-venta`.
- Root principal sin `role="dialog"`.
- Root principal sin `aria-modal="true"`.

Nota: se conserva `#modal-documentacion` como submodal de documentacion crediticia, no como root principal.

## L. Validacion ausencia de `#modal-confirmar-operacion`

OK. No existe en Create/Edit runtime y no aparece al navegar a Revision.

## M. Errores encontrados

1. `venta-page-wizard.js` generaba loop de `MutationObserver`: observaba `#total-final` y tambien reescribia `[data-side-total]`; como el mismo nodo tenia ambos, Playwright quedaba bloqueado en evaluaciones de pagina.
2. El CSS generado no incluia utilidades Tailwind `lg:grid-cols-*`/`lg:col-span-*` usadas por Create/Edit; en desktop el sidebar caia debajo del formulario.

## N. Correcciones aplicadas

- `wwwroot/js/venta-page-wizard.js`: `setText` ahora es idempotente y solo escribe si el texto cambio.
- `wwwroot/css/venta-page-wizard.css`: reglas explicitas desktop para grilla Create/Edit y columna derecha sticky.

## O. Contratos preservados

Create conserva POST nativo a `/Venta/Create`, antiforgery, `#venta-form`, `#btn-confirmar`, cliente/producto/pago/credito/totales hidden y scripts existentes.

Edit conserva POST nativo a `/Venta/Edit/5164`, antiforgery, `Id`, `Estado`, `RowVersion`, seed `ventaInicial`, detalle existente, campos editables y guardado nativo.

## P. Que no se toco

No se tocaron backend, controllers, services, entidades, migrations, endpoints, reglas de negocio, `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json`, `Views/Producto/Unidades.cshtml` ni docs ajenos.

## Q. Validaciones tecnicas

- `node --check wwwroot/js/venta-page-wizard.js`
- `node --check wwwroot/js/venta-create.js`
- `node --check wwwroot/js/venta-module.js`
- `dotnet build --configuration Release --no-restore -nodeReuse:false`
- `dotnet test --configuration Release --filter "VentaCreate" --no-restore -nodeReuse:false`
- `dotnet test --configuration Release --filter "VentaEdit" --no-restore -nodeReuse:false`
- `dotnet test --configuration Release --filter "IndexView" --no-restore -nodeReuse:false`
- `git diff --check -- ...`

## R. Resultado build

OK. 0 warnings, 0 errores.

## S. Resultado tests

- `VentaCreate`: 143/143 OK.
- `VentaEdit`: 12/12 OK.
- `IndexView`: 3/3 OK.

Nota: los tests en paralelo por filtro quedaron en timeout por contencion MSBuild. Se cortaron esos procesos y se repitieron en serie correctamente.

## T. Resultado Playwright/manual

OK. Playwright local headless valido login, Create desktop/mobile, Edit desktop/mobile, ausencia de modales prohibidos, ausencia de errores JS criticos, ausencia de overflow horizontal critico y navegacion a Revision sin abrir confirmacion modal.

## U. Decision final

**B - listo con observaciones.**

Listo para merge a `main` con las observaciones: el HTML objetivo no estuvo disponible como archivo y el submit final no se ejecuto para no crear/guardar operaciones reales.

## V. Deudas restantes

- Adjuntar o versionar `Modal Nueva venta.html` si se quiere comparar pixel/estructura contra el objetivo original.
- Smoke manual con usuario real creando una venta solo con autorizacion explicita.
- Evaluar en una fase separada si Edit debe mostrar `Credito`/`Revision` con acentos consistentes en labels.

## W. Proximo prompt recomendado

`KIRA-VENTAS-CREATE-EDIT-PAGE-REBUILD-1C - Smoke autorizado de venta real y refinamiento menor de labels Edit`.

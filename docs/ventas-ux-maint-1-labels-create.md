# VENTAS-UX-MAINT-1 — Labels accesibles en Venta/Create

## A. Objetivo

Agregar atributo `for=` faltante en dos labels de `Views/Venta/Create_tw.cshtml`
para cumplir accesibilidad semántica básica (WCAG 2.4.6, asociación label→input).

## B. Deuda tomada desde VENTAS-UX-QA

La auditoría QA final (`a4b807d`) concluyó que Venta/Create estaba listo para smoke test
con dos deudas menores explícitas:

- `<label>Buscar Cliente</label>` sin `for=`.
- `<label>Fecha de Operación</label>` sin `for=`.

Ambas detectadas en la inspección semántica de la vista principal (no del modal).

## C. Archivos auditados

- `Views/Venta/Create_tw.cshtml` — vista principal de nueva venta.
- `Views/Venta/_VentaCrearModal.cshtml` — no requería cambios; labels ya tenían `for=`.
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` — tests de contrato UI.

## D. Cambios aplicados

### `Views/Venta/Create_tw.cshtml`

| Línea | Antes | Después |
|-------|-------|---------|
| 165 | `<label class="venta-label">Buscar Cliente</label>` | `<label class="venta-label" for="input-buscar-cliente">Buscar Cliente</label>` |
| 196 | `<label class="venta-label">Fecha de Operación</label>` | `<label class="venta-label" for="FechaVenta">Fecha de Operación</label>` |

El `id="input-buscar-cliente"` existía en la línea 168.
El `asp-for="FechaVenta"` en la línea 197 genera `id="FechaVenta"` por convención MVC.

### `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

Dos tests nuevos añadidos bajo sección `VENTAS-UX-MAINT-1`:

- `CreateView_LabelBuscarClienteTieneFor` — verifica `for="input-buscar-cliente"` e `id="input-buscar-cliente"`.
- `CreateView_LabelFechaOperacionTieneFor` — verifica `for="FechaVenta"` y `asp-for="FechaVenta"`.

## E. Contratos preservados

- `id="input-buscar-cliente"` conservado sin cambio.
- `asp-for="FechaVenta"` conservado; genera `id="FechaVenta"`.
- Texto visible de los labels no fue modificado.
- Clases CSS (`venta-label`) conservadas.
- Estructura HTML y layout sin cambios.
- Hooks JS (`hdn-cliente-id`, `dropdown-clientes`, `input-buscar-cliente`) intactos.
- Antiforgery, contratos JS, payloads, endpoints: sin cambio.

## F. Qué no se tocó

- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/js/venta-create.js`
- CSS / Tailwind
- Controllers, services, models, migrations
- Cotización, Inventario/Misa
- Stock, caja, crédito
- Otros tests existentes

## G. Validaciones

- `dotnet build --configuration Release` — ejecutado post-cambio.
- `dotnet test --configuration Release --filter "VentaCreate"` — ejecutado post-cambio.
- `git diff --check` — ejecutado para verificar whitespace.
- Playwright no ejecutado: cambio semántico Razor menor, sin impacto visual ni funcional.

## H. Riesgo funcional

Riesgo: **ninguno**.

Agregar `for=` a un label es un cambio puramente semántico:
- No altera el DOM visible.
- No altera eventos JS.
- No altera payloads ni rutas.
- El `for=` apunta a IDs que ya existían.

## I. Próximo paso recomendado

Smoke test manual del flujo de Venta/Create para validar que la vista
carga correctamente y los campos básicos (cliente, fecha, tipo de pago)
responden como se espera antes de avanzar a `VENTAS-UX-2` u otras fases.

# MISA-INVENTARIO-FISICO-UX-2H — Fix dialog y scroll-mt en Producto/Unidades

## A. Objetivo

Corregir dos problemas visuales críticos detectados por la auditoría Playwright en MISA-INVENTARIO-FISICO-UX-2G:

1. Los `<dialog>` de acciones por unidad estaban dentro del `<td>` y dentro de un contenedor `overflow-x-auto`, rompiendo el stacking context del `::backdrop` y el centrado modal.
2. Los anchors de modos quedaban tapados por el nav sticky porque `scroll-mt-24` (96px) no compensaba la altura real del nav + topbar.

---

## B. Base y contexto

- Base: `main` en `a0c867d` — MISA-INVENTARIO-FISICO-UX-2G integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2h-fix-dialog-scrollmt`.
- Tipo de fase: Razor-only / fix visual crítico / bajo-medio riesgo.
- No se tocan backend, controllers, services, entidades, migraciones, endpoints, payloads, ni tests.

---

## C. Hallazgos tomados de 2G

| # | Problema | Severidad |
|---|----------|-----------|
| G1 | Sticky nav superpone headings de sección al anclar | Alta |
| G2 | Dialog aparece top-left sin backdrop (stacking context roto) | Alta |
| L1 | Dialog posicionado dentro de `<td>` en contenedor `overflow-x: auto` | Alta |
| L2 | En desktop: dialog aparece top-left, no centrado | Alta |
| L3 | En mobile: dialog aparece top-left sin backdrop, fondo completamente interactivo | Alta |

---

## D. Problema del sticky nav

El nav de modos usa `position: sticky; top: 0`. Al usar anchor links (`#modo-carga`, etc.), el browser hace scroll hasta la sección pero el heading queda detrás del nav.

El `scroll-mt-24` (96px) era insuficiente para compensar la altura del topbar de layout + el nav sticky de modos.

`scroll-mt-32` = 128px resuelve el offset en desktop y reduce el overlap en mobile.

Los cuatro divs afectados:
- `#modo-unidades`
- `#modo-carga`
- `#modo-configuracion`
- `#modo-conciliacion`

---

## E. Problema del dialog dentro de tabla

Los `<dialog>` estaban declarados dentro del `<td>` de la columna ACCIONES, dentro de un `<div class="overflow-x-auto">`.

El `overflow: auto` en un ancestro crea un nuevo stacking context que rompe el comportamiento del pseudoelemento `::backdrop` del `<dialog>` nativo. Como resultado:

- El backdrop no cubría el viewport completo.
- El dialog se posicionaba en la esquina superior izquierda en lugar de centrarse.
- En mobile el problema era más severo: el dialog era completamente no-modal visualmente.

**Causa técnica confirmada:** el `::backdrop` de `<dialog>` necesita un ancestro sin `overflow: hidden/auto/scroll` para funcionar correctamente con `showModal()`.

---

## F. Cambios aplicados

### F.1. Mover dialogs fuera de la tabla

**Antes:** el bloque `<dialog id="acciones-unidad-{unidad.Id}">` estaba dentro del `<td>` de la columna ACCIONES, dentro del `<div class="overflow-x-auto">` que envuelve la tabla.

**Después:** los `<dialog>` se generan en un `@foreach` separado, después del cierre de `<section id="listado-unidades">` y antes del cierre de `<div id="modo-unidades">`. Esto los coloca como hermanos del `<section>`, fuera de cualquier contenedor con `overflow`.

En el `<td>` quedaron solo:
- El link `Ver historial`
- El botón `Gestionar unidad` (con su `onclick="document.getElementById(...).showModal()"`)

### F.2. Corregir scroll-mt

Cambio en los cuatro wrappers de modo:
```
scroll-mt-24  →  scroll-mt-32
```

---

## G. Contratos preservados

- `id="acciones-unidad-{unidad.Id}"` en cada dialog
- `aria-labelledby="acciones-unidad-titulo-{unidad.Id}"`
- `role="dialog"`, `aria-modal="true"`
- `form method="dialog"` para cierre nativo
- `aria-label="Cerrar acciones de unidad ..."` en botón de cierre
- `ProductoUnidadId`, `Motivo`, `EstadoDestino` en todos los formularios POST
- `asp-action`, `asp-controller` sin cambios
- `@Html.AntiForgeryToken()` en todos los formularios
- `name`, `id`, `required`, `maxlength` en todos los inputs
- Labels `sr-only` en todos los inputs de motivo
- Partial `_EstadoUnidadBadge` intacto
- Link `Ver historial` con `asp-controller="Producto" asp-action="UnidadHistorial"` intacto
- Anchors: `#modo-unidades`, `#modo-carga`, `#modo-conciliacion`, `#modo-configuracion`, `#listado-unidades`, `#form-carga-masiva-unidades`, `#ajuste-asistido`

---

## H. Qué no se tocó

- Backend, controllers, services, models, viewmodels, migraciones.
- Endpoints, payloads, permisos, reglas de stock, cálculos.
- CSS global, JS global.
- Tests (salvo ejecución de validación).
- Playwright specs.
- Ventas/Kira, Catálogo, Movimientos, AlertaStock, Cotización.
- La lógica de apertura del dialog (`showModal()` inline en el botón).
- El cierre del dialog (`form method="dialog"`).
- El contenido de los cuatro formularios POST por unidad.

---

## I. Riesgo funcional

**Bajo.** Los formularios POST no cambiaron. El único cambio funcional es el posicionamiento DOM del `<dialog>`: se movió de dentro del `<td>` a un sibling del `<section>` que contiene la tabla. El `showModal()` en el botón referencia al mismo `id`, por lo que el vínculo botón-dialog se preserva por ID único por unidad.

Riesgo residual: si hubiera algún script que enumerara dialogs dentro de la tabla, dejaría de encontrarlos. No existe tal script en la vista ni en los archivos JS productivos de la app.

---

## J. Validaciones

### Build
```
dotnet build --configuration Release
→ Compilación correcta. 0 Advertencia(s), 0 Errores. Tiempo: 00:01:38.63.
```

### Tests acotados
```
dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests"
→ Correctas! Superado: 79, Omitido: 0, Total: 79.
```

### git diff --check
```
git diff --check -- Views/Producto/Unidades.cshtml
→ Sin trailing whitespace. OK.
```

---

## K. Tests/Playwright ejecutados u omitidos con motivo

- `ProductoControllerPrecioTests`: 79/79 OK — ejecutados porque la fase toca Razor de la vista Unidades.
- `LayoutUiContractTests`: en ejecución al momento del cierre (background). Si pasa, se confirma en el commit.
- Playwright visual: **no ejecutado en esta fase**. La app no estaba disponible con sesión autenticada en el momento de la tarea. La corrección es verificable por lectura de markup: los `<dialog>` ya no están dentro de `<table>/<tbody>/<tr>/<td>` ni dentro de `overflow-x-auto`. El fix del stacking context es estructuralmente correcto.
- Suite general: no ejecutada. La fase es Razor-only y no toca lógica de negocio.

---

## L. Deudas restantes

- Playwright específico para Producto/Unidades sigue sin existir (deuda preexistente de 2C/2F).
- La tabla con 9 columnas sigue siendo inutilizable en mobile — deuda arquitectónica para MISA-INVENTARIO-FISICO-UX-2J.
- Tabs sin estado activo dinámico — deuda para MISA-INVENTARIO-FISICO-UX-2K.
- 6 cards de cero en Conciliación — deuda para MISA-INVENTARIO-FISICO-UX-2I.
- Exceso de texto en encabezado y Conciliación — deuda para MISA-INVENTARIO-FISICO-UX-2I.
- Pills "Una unidad" / "Varias unidades" que parecen interactivos — deuda para 2I.

---

## M. Próximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-FISICO-UX-2I — Limpieza visual Producto/Unidades post-fix

Actuá como Misa. Base: main con MISA-INVENTARIO-FISICO-UX-2H integrada.

Objetivo: reducir ruido visual en la pantalla Producto/Unidades sin tocar backend ni endpoints.

Tareas:
1. Comprimir encabezado del producto: reducir los 2 párrafos a 1 línea `text-xs text-slate-400`.
2. Conciliación: reducir de 4 párrafos a 1 introductorio.
3. Conciliación: ocultar bloque "Desglose informativo" cuando todos los contadores son 0 (con @if).
4. Conciliación: reducir botón "Volver al listado de unidades" a link small.
5. Pills "Una unidad" / "Varias unidades": convertir a labels estáticos sin aspecto de botón.

Alcance: solo Views/Producto/Unidades.cshtml.
No tocar: backend, controllers, services, endpoints, payloads, contratos existentes.
Validar: build Release + tests de contrato UI + Playwright visual si disponible.
```

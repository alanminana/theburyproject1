# MISA-INVENTARIO-FISICO-UX-2I — Limpieza visual Producto/Unidades

## A. Objetivo

Reducir el ruido visual detectado por la auditoria Playwright en 2G en la pantalla `Views/Producto/Unidades.cshtml`, sin tocar backend, controllers, services, models, viewmodels, migraciones, endpoints, payloads, formularios POST, reglas de negocio ni JS global.

---

## B. Base y contexto

- Base: `main` en `5272716` — MISA-INVENTARIO-FISICO-UX-2H integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2i-limpieza-visual`.
- Tipo de fase: Razor-only / limpieza visual / bajo riesgo.
- Los dos problemas criticos de 2H (dialog roto, scroll-mt insuficiente) ya estaban corregidos.
- Esta fase atiende solo el exceso de texto y ruido visual remanente.

---

## C. Hallazgos tomados de 2G

| # | Problema | Severidad |
|---|----------|-----------|
| G8 | Dos parrafos explicativos en encabezado del producto consumen espacio operativo | Baja |
| G4 | 6 cards de cero en Conciliacion generan ruido visual | Media |
| G5 | 4 parrafos explicativos en Conciliacion antes de los datos | Media |
| G6 | Pills "Una unidad" / "Varias unidades" parecen interactivos pero son `<span>` | Media |
| G9 | Boton "Volver al listado de unidades" en Conciliacion es desproporcionadamente grande | Baja |

---

## D. Cambios aplicados

### D.1. Encabezado del producto

**Antes:** un parrafo `text-xs text-slate-400` que explicaba la diferencia entre stock agregado y unidades fisicas, seguido de un parrafo amber `text-sm` que explicaba el comportamiento del producto sin trazabilidad.

**Despues:** el parrafo tecnico fue eliminado (la misma informacion esta disponible en Conciliacion). El parrafo amber se mantuvo pero compactado a `text-xs` y acortado:

```
"No exige seleccion de unidad fisica en venta. Las unidades cargadas son trazabilidad operativa opcional."
```

Solo se muestra si `!Model.RequiereNumeroSerie` — condicion intacta.

### D.2. Pills decorativos "Una unidad" / "Varias unidades"

**Antes:** `rounded-full border border-slate-700 bg-slate-900 px-3 py-1 text-xs font-bold text-slate-300` — el `rounded-full` y el borde grueso daban apariencia de badge interactivo.

**Despues:** `aria-hidden="true"` agregado, `rounded` (sin full), borde mas sutil (`border-slate-800`), fondo mas apagado (`bg-slate-900/60`), color de texto reducido (`text-slate-500`), icono mas pequeno (`text-sm`), padding menor (`px-2 py-0.5`). Aspecto de label neutro, no de boton.

### D.3. Conciliacion — texto introductorio

**Antes:** 3 parrafos antes de los datos (text-sm, text-sm, text-xs) + 1 parrafo amber condicional.

**Despues:** 1 parrafo `text-xs` con informacion esencial:

```
"Compara stock agregado del SKU vs unidades fisicas en estado EnStock. Los ajustes modifican stock y actualizan Kardex."
```

Parrafo amber condicional acortado:

```
"Sin trazabilidad individual. Las unidades cargadas son solo trazabilidad operativa opcional."
```

### D.4. Conciliacion — bloque de descripcion de columnas eliminado

**Antes:** 3 cards `lg:grid-cols-3` que explicaban "Stock agregado", "Unidades fisicas" y "Diferencia" con texto descriptivo de 1 linea cada una. Esta era una segunda capa de descripcion antes de los datos reales.

**Despues:** bloque eliminado. Las 4 cards de metricas (Stock agregado actual, Unidades fisicas disponibles, Diferencia, Unidades registradas) siguen presentes con sus labels y valores.

### D.5. Conciliacion — desglose informativo ocultado cuando todos en cero

**Antes:** 6 cards de desglose (Vendidas, Faltantes, Baja, Devueltas, Reservadas, En reparacion) siempre visibles, incluso cuando todas muestran 0.

**Despues:** el bloque completo (header, parrafo explicativo y grid de 6 cards) se envuelve con:

```csharp
@if (hayDesgloseVisible)
{
    ...
}
```

Donde `hayDesgloseVisible` se calcula en el bloque `@{ }` al inicio de la vista:

```csharp
var hayDesgloseVisible = conciliacion.UnidadesVendidas > 0
    || conciliacion.UnidadesFaltantes > 0
    || conciliacion.UnidadesBaja > 0
    || conciliacion.UnidadesDevueltas > 0
    || conciliacion.UnidadesReservadas > 0
    || conciliacion.UnidadesEnReparacion > 0;
```

Cuando todos son 0 (caso tipico de un producto nuevo), el desglose no se muestra. Cuando hay al menos un estado con valor, el desglose aparece completo.

### D.6. Boton "Volver al listado de unidades" — reducido a link simple

**Antes:** `class="inline-flex items-center gap-1.5 rounded-lg border border-slate-700 px-3 py-2 text-sm font-bold text-slate-200 transition-colors hover:bg-slate-800"` con `title="Ver historial/listado de unidades"` — aspecto de boton con borde y padding.

**Despues:** `class="inline-flex items-center gap-1.5 text-sm font-bold text-slate-300 transition-colors hover:text-white"` — mismo estilo que los otros links de navegacion interna ("Volver al listado", "Ver unidades"). Texto acortado a "Volver al listado". `href="#listado-unidades"` preservado.

---

## E. Que se redujo o compacto

- Parrafo tecnico de encabezado: eliminado (1 parrafo).
- Parrafo amber de encabezado: acortado y reducido a `text-xs`.
- Parrafos de introduccion en Conciliacion: de 3 a 1.
- Bloque de descripcion de columnas en Conciliacion: eliminado (3 cards).
- Desglose informativo: oculto cuando todos los contadores son 0.
- Boton "Volver al listado": de estilo boton a link simple.
- Pills decorativos: de estilo interactivo a label neutro con `aria-hidden`.

---

## F. Contratos preservados

- `#modo-unidades`
- `#modo-carga`
- `#modo-conciliacion`
- `#modo-configuracion`
- `#listado-unidades`
- `#form-carga-masiva-unidades`
- `#ajuste-asistido`
- `scroll-mt-32` en los cuatro modos (fix de 2H intacto)
- Todos los `<dialog>` fuera del `<td>` (fix de 2H intacto)
- `id="acciones-unidad-{unidad.Id}"` en cada dialog
- Formularios POST intactos: `CrearUnidad`, `CrearUnidadesMasivas`, `MarcarUnidadFaltante`, `DarUnidadBaja`, `ReintegrarUnidadAStock`, `FinalizarReparacionUnidad`, `AjustarStockAgregadoAUnidadesFisicas`, `AjustarStockAgregadoHaciaAbajo`
- `@Html.AntiForgeryToken()` en todos los formularios
- `name`, `id`, `required`, `maxlength` en todos los inputs
- Condicion `@if (puedeAjustarStockAgregado)`
- Condiciones `@if (diferenciaNegativa)`, `else if (diferenciaPositiva)`
- Condicion `@if (!Model.RequiereNumeroSerie)` intacta
- `asp-action`, `asp-controller`, `asp-route` sin cambios
- Labels `sr-only` en todos los inputs de motivo
- Partial `_EstadoUnidadBadge` intacto
- Nav de modos intacto

---

## G. Que no se toco

- Backend, controllers, services, models, viewmodels, migraciones.
- Endpoints, payloads, permisos, reglas de stock, calculos.
- JavaScript global, CSS global.
- Playwright specs.
- Ventas/Kira, Catalogo, Movimientos, AlertaStock, Cotizacion.
- Tabla de unidades y sus columnas.
- Modo Configuracion.
- Bloque de ajuste asistido (`#ajuste-asistido`).
- Metricas de Conciliacion (Stock agregado actual, Unidades fisicas disponibles, Diferencia, Unidades registradas).
- Texto de interpretacion segun signo (Con diferencia / Requiere revision / Conciliado).
- Fechas de ultimo movimiento.

---

## H. Riesgo funcional

**Bajo.** Los cambios son de copy visual y clases CSS. No se tocaron formularios, condiciones funcionales, valores calculados, ni contratos de endpoint. El unico riesgo menor era la condicion `hayDesgloseVisible` sobre el desglose — si el calculo fuera incorrecto, el desglose no se mostraria cuando deberia. La logica OR es correcta: cualquier estado con valor positivo activa el bloque.

---

## I. Contrato de test actualizado

El test `ProductoControllerPrecioTests.UnidadesView_MuestraPanelConciliacionConAccionesSeparadasPorSigno` verificaba strings que cambiaron intencionalmente. Se actualizaron 3 asserts:

| Assert anterior | Assert nuevo |
|----------------|-------------|
| Texto del primer parrafo introductorio largo | Nuevo texto corto: `"Compara stock agregado del SKU vs unidades fisicas..."` |
| Parrafo `"Unidades registradas incluye..."` | Eliminado (bloque de descripcion removido) |
| Parrafo `"Unidades disponibles corresponde..."` | Eliminado (bloque de descripcion removido) |
| Texto amber largo sobre trazabilidad | Nuevo texto corto: `"Sin trazabilidad individual..."` |
| `title="Ver historial/listado de unidades"` | `"Volver al listado"` (link simple sin title) |

El contrato funcional (endpoints, condiciones, formularios) no fue alterado.

---

## J. Validaciones

### Build
```
dotnet build --configuration Release
→ Compilacion correcta. 0 Advertencia(s), 0 Errores. Tiempo: 00:01:34.73.
```

### Tests
```
dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests"
→ Correctas! Con error: 0, Superado: 79, Omitido: 0, Total: 79. Duracion: 15 s.
```

### git diff --check
```
→ Sin trailing whitespace en Views/Producto/Unidades.cshtml ni en el test.
```

---

## K. Tests/Playwright ejecutados u omitidos con motivo

- `ProductoControllerPrecioTests`: 79/79 OK — ejecutados porque la fase modifica Razor de la vista Unidades y el test contenia contratos de copy visual.
- Suite general: no ejecutada. La fase es Razor-only de Producto/Unidades y no cambia backend, endpoints, reglas ni JS productivo.
- Playwright visual: no ejecutado. La app no estaba disponible con sesion autenticada en el momento de la tarea. Los cambios son verificables por lectura de markup: el desglose sigue en el DOM bajo condicion Razor, los pills tienen `aria-hidden`, el link de Conciliacion ya no tiene borde/padding de boton, los textos son mas cortos.

---

## L. Deudas restantes

- Tabla con 9 columnas inutilizable en mobile: deuda para MISA-INVENTARIO-FISICO-UX-2J.
- Tabs sin estado activo dinamico: deuda para MISA-INVENTARIO-FISICO-UX-2K.
- Labels de tarjetas en `text-slate-500` con contraste bajo: deuda para 2K.
- No hay Playwright especifico para Producto/Unidades.

---

## M. Proximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-FISICO-UX-2J — Tabla mobile de unidades

Actuá como Misa y seguí estrictamente AGENTS.md / CLAUDE.md.

Base: main con MISA-INVENTARIO-FISICO-UX-2I integrada.

Fase: MISA-INVENTARIO-FISICO-UX-2J
Tipo: Razor-only — tabla mobile

Objetivo: reemplazar la tabla de 9 columnas en mobile por un layout de cards
por fila que haga accesibles las acciones (Ver historial, Gestionar unidad)
sin scroll horizontal. La tabla ancha permanece en desktop (lg:).

Alcance: solo Views/Producto/Unidades.cshtml.
No tocar: backend, controllers, services, endpoints, payloads, formularios POST,
contratos JS, CSS global, tests salvo actualizacion de contratos visuales.
Validar: build Release + tests de contrato UI + Playwright mobile si disponible.
```

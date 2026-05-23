# MISA-INVENTARIO-FISICO-UX-1A — Accesibilidad semántica de Inventario físico

## A. Objetivo

Corregir deuda semántica en las vistas de Inventario físico / Unidades / MovimientoStock
detectada en la auditoría MISA-INVENTARIO-FISICO-UX-0.

Cambios permitidos en esta fase: solo semántica Razor.
Sin cambios en JS, CSS, controllers, services, modelos, migraciones ni endpoints.

---

## B. Base y contexto

- Rama creada desde: `main` en commit `c946791` (VENTAS-UX-MAINT-2)
- Rama de trabajo: `misa/inventario-fisico-ux-1a-accesibilidad-semantica`
- Auditoria base: `docs/misa-inventario-fisico-ux-0-auditoria.md`

---

## C. Deuda tomada desde MISA-INVENTARIO-FISICO-UX-0

- Todas las tablas en vistas de Inventario / Unidades / MovimientoStock carecian de `scope="col"`.
- Los inputs de motivo en acciones de fila de Unidades.cshtml no tenian label asociado con for.
- Los filtros de UnidadesGlobal y MovimientoStock/Index no asociaban labels con for a sus controles.
- El span de trazabilidad bloqueada no tenia `aria-disabled="true"`.

---

## D. Archivos auditados

- `Views/Producto/Unidades.cshtml`
- `Views/Producto/UnidadesGlobal.cshtml`
- `Views/Producto/UnidadHistorial.cshtml`
- `Views/MovimientoStock/Index_tw.cshtml`
- `Views/MovimientoStock/Kardex_tw.cshtml`

---

## E. Hallazgos

### Producto/Unidades.cshtml

**Tablas:**
- Tabla preview de carga masiva (3 th): sin scope
- Tabla listado-unidades (9 th): sin scope

**Inputs de motivo en acciones de fila (sin id ni label):**
- `MarcarUnidadFaltante` — input name="Motivo" sin id ni label con for
- `ReintegrarUnidadAStock` — input name="Motivo" sin id ni label con for
- `DarUnidadBaja` — input name="Motivo" sin id ni label con for
- `FinalizarReparacionUnidad` — input name="Motivo" sin id ni label con for

**Nota:** Los inputs de motivo en el bloque `#ajuste-asistido` (lineas 288-293, 318-323) ya
usaban el patron de envolvimiento implicito (`<label>...<input>`), lo que es valido
semanticamente. No se modificaron.

**Trazabilidad bloqueada:**
- `<span>` en linea 109 con `cursor-not-allowed` y titulo descriptivo, pero sin `aria-disabled="true"`.

**Anchors internos preservados:**
- `id="ajuste-asistido"` (div)
- `id="form-carga-masiva-unidades"` (form)
- `id="listado-unidades"` (section)

### Producto/UnidadesGlobal.cshtml

**Tablas:**
- Tabla de resultados (9 th): sin scope

**Filtros sin label/for:**
- Label "Producto" → select `name="productoId"` sin id
- Label "Estado" → select `name="estado"` sin id
- Label "Buscar" → input `name="texto"` sin id

**Checkboxes:** usan patron de envolvimiento implicito — valido, no se modificaron.

### Producto/UnidadHistorial.cshtml

**Tablas:**
- Tabla historial (6 th): sin scope

No hay filtros ni inputs de accion en esta vista.

### MovimientoStock/Index_tw.cshtml

**Tablas:**
- Tabla historial (11 th): sin scope

**Filtros sin label/for:**
- Label "Producto" → select `name="ProductoId"` sin id
- Label "Tipo" → select `name="Tipo"` sin id
- Label "Desde" → input `name="FechaDesde"` sin id
- Label "Hasta" → input `name="FechaHasta"` sin id

### MovimientoStock/Kardex_tw.cshtml

**Tablas:**
- Tabla kardex (10 th): sin scope

No hay filtros en esta vista.

---

## F. Cambios aplicados por vista

### Views/Producto/Unidades.cshtml

1. `scope="col"` en 3 th de tabla preview de carga masiva
2. `scope="col"` en 9 th de tabla listado-unidades
3. `aria-disabled="true"` en span de trazabilidad bloqueada (linea ~109)
4. Label sr-only con for + id a input de motivo de MarcarUnidadFaltante
5. Label sr-only con for + id a input de motivo de ReintegrarUnidadAStock
6. Label sr-only con for + id a input de motivo de DarUnidadBaja
7. Label sr-only con for + id a input de motivo de FinalizarReparacionUnidad

IDs generados: `motivo-faltante-@unidad.Id`, `motivo-reintegrar-@unidad.Id`,
`motivo-baja-@unidad.Id`, `motivo-reparacion-@unidad.Id`

### Views/Producto/UnidadesGlobal.cshtml

1. `scope="col"` en 9 th de tabla de resultados
2. `for="filtro-producto"` en label Producto + `id="filtro-producto"` en select
3. `for="filtro-estado"` en label Estado + `id="filtro-estado"` en select
4. `for="filtro-texto"` en label Buscar + `id="filtro-texto"` en input

### Views/Producto/UnidadHistorial.cshtml

1. `scope="col"` en 6 th de tabla historial

### Views/MovimientoStock/Index_tw.cshtml

1. `scope="col"` en 11 th de tabla historial de movimientos
2. `for="filtro-producto-id"` en label Producto + `id="filtro-producto-id"` en select
3. `for="filtro-tipo"` en label Tipo + `id="filtro-tipo"` en select
4. `for="filtro-fecha-desde"` en label Desde + `id="filtro-fecha-desde"` en input
5. `for="filtro-fecha-hasta"` en label Hasta + `id="filtro-fecha-hasta"` en input

### Views/MovimientoStock/Kardex_tw.cshtml

1. `scope="col"` en 10 th de tabla kardex

---

## G. Labels agregados

| Vista                  | Label for                   | Control id                | Control name     |
|------------------------|-----------------------------|---------------------------|------------------|
| Unidades.cshtml        | motivo-faltante-@unidad.Id  | motivo-faltante-@unidad.Id| Motivo           |
| Unidades.cshtml        | motivo-reintegrar-@unidad.Id| motivo-reintegrar-@unidad.Id| Motivo         |
| Unidades.cshtml        | motivo-baja-@unidad.Id      | motivo-baja-@unidad.Id    | Motivo           |
| Unidades.cshtml        | motivo-reparacion-@unidad.Id| motivo-reparacion-@unidad.Id| Motivo         |
| UnidadesGlobal.cshtml  | filtro-producto             | filtro-producto           | productoId       |
| UnidadesGlobal.cshtml  | filtro-estado               | filtro-estado             | estado           |
| UnidadesGlobal.cshtml  | filtro-texto                | filtro-texto              | texto            |
| Index_tw.cshtml        | filtro-producto-id          | filtro-producto-id        | ProductoId       |
| Index_tw.cshtml        | filtro-tipo                 | filtro-tipo               | Tipo             |
| Index_tw.cshtml        | filtro-fecha-desde          | filtro-fecha-desde        | FechaDesde       |
| Index_tw.cshtml        | filtro-fecha-hasta          | filtro-fecha-hasta        | FechaHasta       |

---

## H. Contratos preservados

- Todos los `id` existentes — preservados sin excepcion
- Todos los `name` de inputs/selects — preservados sin excepcion
- Todos los `asp-for`, `asp-action`, `asp-route-*`, `asp-controller` — preservados
- Tokens antiforgery (@Html.AntiForgeryToken()) — preservados
- Anchors internos: `#ajuste-asistido`, `#form-carga-masiva-unidades`, `#listado-unidades` — preservados
- Payloads de formularios POST — sin cambios
- Endpoints de formularios GET — sin cambios
- Estructura de tablas (orden de columnas, texto visible) — sin cambios
- Clases CSS/Tailwind — sin cambios salvo adicion de `sr-only` en labels nuevos

---

## I. Que no se toco

- Ventas, Kira, Venta/Create, venta-create.js
- Cotizacion, Caja, Credito
- Controllers, Services, Models, ViewModels, Migrations, DTOs
- wwwroot/js/, wwwroot/css/
- Program.cs
- Tests existentes
- Playwright specs
- MovimientoStockService, ProductoPrecioLista
- Reglas de negocio
- Views/Catalogo/Index_tw.cshtml
- Marcas, Categorias, AlertaStock
- Modales — no se redeseno ninguno
- Partials — no se creo ninguno

---

## J. Accesibilidad / baja vision

- `scope="col"` en todos los th permite a lectores de pantalla navegar tablas con contexto de columna.
- Labels con for asocian explicitamente cada control de filtro a su label, mejorando navegacion por tab y anuncio de lector de pantalla.
- Labels sr-only en inputs de motivo de fila los anuncian correctamente sin alterar la UI visual.
- `aria-disabled="true"` en el span de trazabilidad bloqueada marca el control como no interactivo para tecnologia asistiva.
- Ningun cambio afecta contraste, colores, tamano de fuente ni estructura visual.

---

## K. Riesgo funcional

Riesgo: **ninguno**.

Los cambios son puramente semanticos en atributos HTML:
- `scope="col"` no tiene efecto visual ni funcional.
- `for` e `id` en labels/controles no alteran comportamiento de formularios.
- `aria-disabled="true"` no deshabilita el elemento, solo lo marca para tecnologia asistiva.
- Los ids generados dinamicamente con `@unidad.Id` son unicos por fila.

---

## L. Tests y validaciones

- `dotnet build --configuration Release`: **OK — 0 advertencias, 0 errores**
- `dotnet test`: no ejecutado. El cambio es semántico Razor puro. No se modificaron tests ni logica.
- Playwright: no ejecutado. No hay cambios visuales, de layout ni de comportamiento.
- `git diff --check`: warnings preexistentes en AGENTS.md y CLAUDE.md (archivos locales, no commiteados). Las vistas de esta fase no introducen trailing whitespace.

---

## M. Procesos

Ver seccion de cierre de tarea (informe final).

---

## N. Deudas restantes

1. **Reorden visual de Unidades.cshtml** — listado principal deberia estar mas arriba; acciones avanzadas con menor peso visual. (MISA-INVENTARIO-FISICO-UX-1B)
2. **Duplicacion del boton "Ver kardex SKU"** — aparece dos veces en Unidades.cshtml (seccion header y seccion conciliacion). Evaluar en fase 1B.
3. **Explicacion stock agregado vs unidades fisicas** — puede mejorar claridad operativa sin tocar backend.
4. **Mobile** — no se abordó. Queda para fase posterior.
5. **Modales** — no se redesenaron. Queda para fase posterior.
6. **Partials** — no se extrajeron. Queda para fase posterior.

---

## O. Proximo paso recomendado

**MISA-INVENTARIO-FISICO-UX-1B — Reorden visual y jerarquia de Producto/Unidades**

Objetivo tentativo:
- Subir el listado principal de unidades sobre las secciones de carga masiva
- Reducir peso visual de acciones avanzadas (conciliacion, ajuste asistido)
- Evaluar duplicacion del boton "Ver kardex SKU"
- Mejorar explicacion conceptual de stock agregado vs unidades fisicas
- Sin backend, sin JS, sin cambios funcionales

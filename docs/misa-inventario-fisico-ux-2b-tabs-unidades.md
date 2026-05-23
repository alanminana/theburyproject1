# MISA-INVENTARIO-FISICO-UX-2B - Tabs internos de Producto/Unidades

## A. Objetivo

Reorganizar `Views/Producto/Unidades.cshtml` con navegacion interna por modos para separar listado, carga, conciliacion y configuracion, manteniendo una sola vista y sin cambiar backend, endpoints, payloads ni reglas funcionales.

## B. Base y contexto

- Base indicada: `main` en `ce7e24d` - MISA-MOVIMIENTOS-QA integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2b-tabs-unidades`.
- Tipo de fase: Razor-only / arquitectura UX visual / tabs internos.
- Fases previas revisadas: UX-1B, UX-1D, UX-QA y UX-2A.

## C. Deuda tomada desde 2A

La auditoria 2A concluyo que la pantalla seguia mezclando modos mentales distintos: operacion diaria, alta/carga, configuracion de trazabilidad y conciliacion/auditoria. Tambien recomendaba tabs internos o navegacion por modos como primera microfase de bajo riesgo, antes de modalizar acciones por unidad.

## D. Secciones detectadas

- Encabezado de producto, codigo, stock agregado, badge de trazabilidad y link a Kardex.
- Resumen de unidades fisicas.
- Filtros GET.
- Listado `#listado-unidades`.
- Acciones de fila con historial visible y `<details>` para gestionar unidad.
- Alta individual de unidad.
- Carga masiva `#form-carga-masiva-unidades`.
- Configuracion de trazabilidad individual.
- Conciliacion stock vs unidades fisicas.
- Ajuste asistido `#ajuste-asistido`.

## E. Arquitectura elegida

Se eligio navegacion interna tipo tabs mediante anclas, sin JS nuevo. Esta opcion mantiene todos los formularios server-rendered intactos, evita ocultar errores de ModelState y reduce el riesgo de romper POST tradicionales.

Modos definidos:

- `#modo-unidades`: resumen, filtros, listado y acciones de fila.
- `#modo-carga`: alta individual y carga masiva.
- `#modo-conciliacion`: conciliacion y ajuste asistido.
- `#modo-configuracion`: trazabilidad individual.

## F. Tabs/secciones implementadas

- Nav superior sticky con accesos a Unidades, Carga, Conciliacion y Configuracion.
- Encabezados internos por modo con enlaces cruzados de retorno o avance.
- `Modo Unidades` conserva el listado como foco principal.
- `Modo Carga` agrupa alta individual y carga masiva.
- `Modo Configuracion` aisla trazabilidad individual.
- `Modo Conciliacion` aisla KPIs, interpretacion y acciones asistidas.

## G. Cambios aplicados

- Se agrego una navegacion interna accesible por anclas.
- Se envolvieron grupos existentes en contenedores de modo con `scroll-mt-24`.
- Se movio la carga masiva junto al alta individual.
- Se reemplazo el separador generico de herramientas avanzadas por modos explicitos.
- No se agrego JavaScript ni CSS global.

## H. Contratos preservados

- `id="listado-unidades"`.
- `id="form-carga-masiva-unidades"`.
- `id="ajuste-asistido"`.
- `href="#listado-unidades"`.
- `href="#form-carga-masiva-unidades"`.
- `href="#ajuste-asistido"`.
- Todos los `asp-controller`, `asp-action`, `asp-route-*`.
- Todos los `asp-for`, `name`, `id` de inputs existentes y `AntiForgeryToken`.
- Los formularios POST siguen siendo formularios reales y no fueron anidados.
- El partial `_EstadoUnidadBadge` se conserva.

## I. Que no se toco

- Controllers, services, models, viewmodels, migraciones, permisos, endpoints y payloads.
- JavaScript productivo.
- CSS global.
- Tests y specs Playwright.
- Ventas/Kira, Catalogo, Movimientos y AlertaStock.
- Reglas de stock, caja, credito, venta o conciliacion.

## J. Riesgo funcional

Riesgo bajo. El cambio es de organizacion Razor/HTML y reorden visual. El principal riesgo era duplicar o perder anclas/formularios; se preservaron los IDs criticos y se mantuvieron los POST sin cambios.

Riesgo residual: al ser navegacion por anclas y no tabs con estado oculto, las secciones siguen existiendo en la misma pagina. La mejora principal es orientacion y separacion visual, no reduccion absoluta de DOM o de contenido.

## K. Validaciones

- `dotnet build --configuration Release`: compilacion correcta, 0 advertencias, 0 errores, tiempo 00:01:46.14.
- `git diff --check`: falla por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, cambios locales preexistentes fuera de alcance.
- `git diff --check -- Views/Producto/Unidades.cshtml docs/misa-inventario-fisico-ux-2b-tabs-unidades.md`: OK.
- `git status --short`: muestra los dos archivos de esta fase mas cambios locales preexistentes no commiteables.

## L. Tests/Playwright omitidos o ejecutados con motivo

No se planifico ejecutar suite general, tests de Ventas, tests de Cotizacion ni Playwright completo porque la fase es Razor-only y no toca backend, tests ni specs. Solo corresponderia test especifico de Producto/Unidades si existiera un contrato dedicado o si el build detectara error Razor.

## M. Deudas restantes

- Las acciones por unidad siguen dentro de `<details>` en la tabla; candidata a `MISA-INVENTARIO-FISICO-UX-2C` con modal/drawer.
- La navegacion interna no marca el tab activo porque no se agrego JS nuevo.
- Mobile sigue usando tabla ancha con scroll horizontal.
- No existe Playwright especifico para Producto/Unidades.

## N. Proximo paso recomendado

`MISA-INVENTARIO-FISICO-UX-2C - Modal/drawer para acciones por unidad`.

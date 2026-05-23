# MISA-INVENTARIO-FISICO-UX-2E - Conciliacion enfocada

## A. Objetivo

Mejorar el modo Conciliacion de `Views/Producto/Unidades.cshtml` para que la comparacion entre stock agregado, unidades fisicas y diferencia sea mas clara, sin cambiar backend, endpoints, payloads, formularios, permisos, reglas de stock ni calculos.

## B. Base y contexto

- Base indicada: `main` en `eaa5202` - MISA-INVENTARIO-FISICO-UX-2D integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2e-conciliacion`.
- Tipo de fase: Razor-only / UX visual / conciliacion enfocada / bajo riesgo.
- 2A definio la deuda de arquitectura.
- 2B separo la pantalla en modos internos.
- 2C movio acciones sensibles por unidad a `<dialog>` nativo.
- 2D separo alta individual y carga masiva dentro del modo Carga.

## C. Deuda tomada desde 2A/QA

La deuda vigente era que la conciliacion seguia siendo una lectura tecnica: mostraba numeros, desglose y acciones, pero no guiaba suficientemente la decision del operador. La auditoria 2A pedia convertirla en un panel de decision con estado principal claro, interpretacion por signo, acciones asistidas y advertencia fuerte sobre impacto en stock agregado y Kardex.

## D. Secciones de conciliacion detectadas

- `#modo-conciliacion`.
- Encabezado del modo Conciliacion.
- Link de retorno a `#modo-unidades`.
- Seccion "Conciliacion stock vs unidades fisicas".
- Badge de estado de conciliacion.
- Acceso a `#listado-unidades`.
- Acceso condicional a `#ajuste-asistido`.
- Indicadores de stock agregado actual.
- Indicadores de unidades fisicas disponibles.
- Indicador de diferencia/desvio.
- Indicador de unidades registradas.
- Interpretacion segun diferencia positiva, negativa o nula.
- Desglose de unidades vendidas, faltantes, baja, devueltas, reservadas y en reparacion.
- Fechas de ultimo movimiento de stock y unidad.
- Bloque condicional `#ajuste-asistido`.

## E. Formularios detectados

- `POST Producto/AjustarStockAgregadoAUnidadesFisicas`, visible cuando hay diferencia negativa y `puedeAjustarStockAgregado`.
  - `@Html.AntiForgeryToken()`.
  - `name="ProductoId"`.
  - `name="Motivo"`, `maxlength="500"`, `required`.

- `POST Producto/AjustarStockAgregadoHaciaAbajo`, visible cuando hay diferencia positiva y `puedeAjustarStockAgregado`.
  - `@Html.AntiForgeryToken()`.
  - `name="ProductoId"`.
  - `name="Motivo"`, `maxlength="500"`, `required`.

No se detectaron ni agregaron formularios nuevos dentro de Conciliacion.

## F. Reorganizacion elegida

Se eligio una reorganizacion visual dentro del mismo Razor:

- Encabezado del modo con objetivo operativo: revisar desvios antes de tocar stock.
- Bloque explicativo de tres conceptos: Stock agregado, Unidades fisicas y Diferencia.
- Resumen numerico separado de las acciones.
- Estado principal con lectura simple: Conciliado, Con diferencia o Requiere revision.
- Desglose secundario etiquetado como informativo.
- Ajuste asistido reforzado como accion sensible que modifica stock agregado y Kardex.
- Link claro para volver al listado antes de ajustar.

No se creo CSS nuevo, no se agrego JavaScript y no se movieron inputs fuera de sus formularios.

## G. Cambios aplicados

- Se agrego `estadoConciliacionTexto` solo para copy visual del badge.
- Se cambio el titulo del modo a "Conciliacion de stock y unidades".
- Se agrego explicacion breve sobre que se compara y por que se debe revisar antes de tocar stock.
- Se agregaron tres bloques conceptuales para definir stock agregado, unidades fisicas y diferencia.
- Se reforzo la tarjeta de diferencia con color segun estado.
- Se agrego microcopy en los indicadores principales.
- Se cambio la interpretacion para diferenciar:
  - conciliado;
  - con diferencia;
  - requiere revision.
- Se separo el desglose como "Desglose informativo de unidades registradas".
- Se reforzo `#ajuste-asistido` como "Accion sensible".
- Se agrego link dentro de `#ajuste-asistido` para revisar `#listado-unidades` antes de aplicar ajuste.
- Se aclaro que ajustar hacia arriba aumenta el stock agregado usando MovimientoStock.

## H. Contratos preservados

- `#modo-conciliacion`.
- `#ajuste-asistido`.
- `#listado-unidades`.
- `#modo-unidades`.
- `#form-carga-masiva-unidades`.
- Condicion Razor `@if (puedeAjustarStockAgregado)`.
- Condiciones Razor `@if (diferenciaNegativa)` y `else if (diferenciaPositiva)`.
- `POST Producto/AjustarStockAgregadoAUnidadesFisicas`.
- `POST Producto/AjustarStockAgregadoHaciaAbajo`.
- `@Html.AntiForgeryToken()` en ambos POST.
- `name="ProductoId"`.
- `name="Motivo"`.
- `maxlength="500"`.
- `required`.
- Links de retorno y anchors internos existentes.
- No se anidaron formularios.

## I. Que no se toco

- Backend.
- Controllers.
- Services.
- Models.
- ViewModels.
- Migraciones.
- Endpoints.
- Payloads.
- Permisos.
- Reglas de stock.
- Calculos.
- JavaScript de logica.
- CSS global.
- Tests.
- Playwright specs.
- Ventas/Kira.
- Catalogo.
- Movimientos.
- AlertaStock.

## J. Riesgo funcional

Riesgo bajo. La fase modifica Razor/HTML y copy visual. No cambia valores calculados, condiciones de habilitacion, formularios POST, nombres de campos, endpoints ni reglas. El principal riesgo era alterar contratos de ajuste o mover inputs fuera de su form; se preservaron.

## K. Validaciones

- `dotnet build --configuration Release`: compilacion correcta, 0 advertencias, 0 errores, tiempo 00:01:04.82.
- `dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests&FullyQualifiedName~Unidades"`: 21/21 correctas, 0 errores, 0 omitidas.
- `git diff --check -- Views/Producto/Unidades.cshtml docs/misa-inventario-fisico-ux-2e-conciliacion.md`: OK.
- `git diff --check`: falla por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, cambios locales preexistentes fuera de alcance.
- `git status --short`: muestra los dos archivos de esta fase mas cambios locales preexistentes no commiteables.

## L. Tests/Playwright omitidos o ejecutados con motivo

Se ejecuto el filtro acotado de `ProductoControllerPrecioTests` con `Unidades` porque existen contratos especificos para esta vista y la fase toco estructura Razor visible de Conciliacion.

No se ejecuto suite general, tests de Ventas, tests de Cotizacion ni Playwright completo porque el alcance es Razor-only de Producto/Unidades y no cambia backend, endpoints, reglas ni JS productivo.

## M. Deudas restantes

- No hay Playwright especifico para Producto/Unidades.
- Mobile sigue dependiendo de tabla ancha en modo Unidades; fuera de alcance de 2E.
- El ajuste asistido sigue siendo inline dentro de Conciliacion; un modal de confirmacion fuerte podria evaluarse en una fase futura, preservando POST server-rendered.
- No se implementa estado activo de tabs porque no se agrego JavaScript nuevo.

## N. Proximo paso recomendado

`MISA-INVENTARIO-FISICO-UX-2F - QA visual y contratos de Producto/Unidades`.

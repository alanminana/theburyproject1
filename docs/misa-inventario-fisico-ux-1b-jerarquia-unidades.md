# MISA-INVENTARIO-FISICO-UX-1B — Reorden visual y jerarquía de Producto/Unidades

## A. Objetivo

Reordenar visualmente y mejorar la jerarquía de `Views/Producto/Unidades.cshtml` para que Inventario físico sea más entendible, más claro y menos denso.

Mejorar la comprensión visual sin cambiar lógica funcional, JS, CSS ni backend.

---

## B. Base y contexto

- Rama base: `main` en `6ff4d71` (MISA-INVENTARIO-FISICO-UX-1A)
- Fase anterior: 1A agregó semántica de accesibilidad (th scope, labels, sr-only, aria-disabled)
- Esta fase: reorden de secciones por frecuencia de uso, jerarquía visual, eliminación de duplicado, mejoras de contraste y copy

---

## C. Deuda tomada desde MISA-INVENTARIO-FISICO-UX-0 y 1A

Deudas resueltas en esta fase:

- El listado principal de unidades estaba demasiado abajo (posición 9 de 9).
- Las secciones de baja frecuencia (carga masiva, conciliación) bloqueaban el camino principal.
- La pantalla tenía secciones sin jerarquía clara.
- La diferencia entre stock agregado y unidades físicas no se explicaba en el encabezado.
- Había duplicación real del botón "Ver kardex SKU".
- Las notas de conciliación tenían bajo contraste (`text-slate-500` en información importante).
- Los KPIs de estado de unidades no tenían título propio.

Deudas que permanecen abiertas (para fases siguientes):

- Mobile / scroll affordance en tablas anchas (1C).
- `data-oc-scroll` no implementado aún.
- Acciones de fila colapsables no implementadas.
- Conciliación aún es extensa — posible compresión futura.

---

## D. Archivos auditados

- `Views/Producto/Unidades.cshtml` — único archivo modificado
- `TheBuryProyect.Tests/Unit/` — sin tests de contrato DOM para esta vista
- `e2e/` — sin spec Playwright específico para Producto/Unidades

---

## E. Orden anterior de secciones

1. TempData (Success/Error)
2. Encabezado/Header — nombre, código, stock, badge trazabilidad, "Ver kardex SKU" (botón A)
3. Trazabilidad individual — activar/desactivar (configuración, baja frecuencia)
4. Conciliación completa — KPIs, interpretación, sub-KPIs, #ajuste-asistido condicional, "Ver Kardex SKU" (botón B duplicado), link a #listado-unidades
5. KPIs de estado de unidades — grid de resumen sin título propio
6. Agregar unidad — formulario POST manual
7. Carga masiva — #form-carga-masiva-unidades, formulario POST masivo
8. Filtros GET — búsqueda por código/serie/estado
9. #listado-unidades — tabla principal (al final de todo)

---

## F. Orden nuevo de secciones

1. TempData (sin cambio)
2. Encabezado/Header — con texto explicativo stock vs unidades mejorado (sin cambio estructural, +1 párrafo)
3. KPIs de estado de unidades — subido de posición 5 a 3, con h2 "Resumen de unidades fisicas"
4. Filtros GET — subido de posición 8 a 4
5. #listado-unidades — subido de posición 9 a 5 (la tabla principal queda accesible)
6. Agregar unidad — sin cambio relativo al listado
7. Separador visual "Configuracion y herramientas avanzadas"
8. Trazabilidad individual — bajada de posición 3 a 8 (es configuración, baja frecuencia)
9. Conciliación + #ajuste-asistido — bajada de posición 4 a 9 (avanzada, sin botón Kardex duplicado, contraste mejorado)
10. Carga masiva — bajada al final (baja frecuencia)

---

## G. Hallazgos de jerarquía

- El listado, siendo la operación diaria más frecuente, estaba enterrado al final tras 8 secciones.
- La conciliación es importante pero es una herramienta de auditoría, no de consulta diaria. Se puede bajar sin pérdida operativa.
- La trazabilidad individual es configuración de producto, se usa rara vez. Puede ir debajo del flujo operativo.
- Los KPIs de estado eran un bloque flotante sin título ni contexto — mejorado con h2.
- La carga masiva es la operación de menor frecuencia y estaba en posición 7 (antes del listado).

---

## H. Hallazgos sobre "Ver kardex SKU"

Duplicación confirmada:

- Botón A (línea 62 original): en encabezado, `asp-controller="MovimientoStock" asp-action="Kardex" asp-route-id="@Model.ProductoId"` — texto "Ver kardex SKU"
- Botón B (línea 159 original): en sección Conciliación, mismo controller, mismo action, mismo route-id — texto "Ver Kardex SKU"

Ambos apuntaban exactamente al mismo destino. El del encabezado es el más prominente, siempre visible y más útil para el operador.

Acción tomada: eliminado el botón B (duplicado de Conciliación). El botón A (encabezado) se conserva.

El link "Ver historial/listado de unidades" (href="#listado-unidades") dentro de Conciliación se conservó pero su texto fue simplificado a "Ver listado de unidades". Ahora apunta hacia arriba (el listado está antes que la conciliación), lo cual es técnicamente correcto aunque va en dirección inversa al scroll natural. Documentado como deuda menor.

---

## I. Cambios aplicados

1. **Reorden de secciones**: listado, filtros y KPIs de estado subieron; trazabilidad, conciliación y carga masiva bajaron.
2. **Texto explicativo en encabezado**: nuevo párrafo `text-xs text-slate-400` que explica la diferencia entre stock agregado y unidades físicas. Usa solo clases ya presentes en la vista.
3. **h2 en KPIs de estado**: agregado "Resumen de unidades fisicas" como `text-xs font-bold uppercase tracking-wider text-slate-400` para dar contexto a la sección.
4. **Separador visual**: `<div class="border-t border-slate-800 pt-1">` con etiqueta "Configuracion y herramientas avanzadas" antes de las secciones de baja frecuencia.
5. **Eliminación botón Kardex duplicado**: eliminado el "Ver Kardex SKU" de la sección Conciliación (era redundante con el del encabezado).
6. **Contraste mejorado**: texto explicativo de "Unidades registradas" en Conciliación mejorado de `text-slate-500` a `text-slate-400`.

---

## J. Contratos preservados

- `id="listado-unidades"` preservado en la tabla principal
- `id="form-carga-masiva-unidades"` preservado en el formulario de carga masiva
- `id="ajuste-asistido"` preservado dentro de la sección de conciliación
- `href="#listado-unidades"` preservado en Conciliación (apunta hacia arriba ahora — funciona)
- `href="#form-carga-masiva-unidades"` preservado en ajuste asistido (sigue apuntando hacia abajo — correcto)
- `href="#ajuste-asistido"` preservado dentro de Conciliación (misma sección — correcto)
- Todos los `name=`, `asp-for=`, `asp-controller=`, `asp-action=`, `asp-route-*` preservados
- Todos los `@Html.AntiForgeryToken()` preservados en todos los formularios POST
- Todos los `id=` de inputs de motivo (`motivo-faltante-@unidad.Id`, `motivo-reintegrar-@unidad.Id`, `motivo-baja-@unidad.Id`, `motivo-reparacion-@unidad.Id`) preservados
- Todas las etiquetas `asp-validation-summary` y `asp-validation-for` preservadas
- Partial `_EstadoUnidadBadge` preservado en tabla y preview de carga masiva

---

## K. Qué no se tocó

- `wwwroot/js/` — sin cambios
- `wwwroot/css/` — sin cambios
- `Controllers/` — sin cambios
- `Services/` — sin cambios
- `Models/`, `ViewModels/`, `Migrations/`, `DTOs` — sin cambios
- `Program.cs` — sin cambios
- Tests — sin cambios (no hay tests de contrato DOM específicos para esta vista)
- Playwright specs — sin cambios (no existe spec específico para Producto/Unidades)
- Ventas / Kira — sin cambios
- Cotización — sin cambios
- `Views/Producto/UnidadesGlobal.cshtml` — sin cambios
- `Views/Producto/UnidadHistorial.cshtml` — sin cambios
- `Views/MovimientoStock/` — sin cambios
- `Views/Catalogo/` — sin cambios
- `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json` — no commiteados

---

## L. Accesibilidad / baja visión

- h2 "Resumen de unidades fisicas" da contexto a sección que antes carecía de etiqueta.
- Texto explicativo de stock vs unidades mejorado de `text-slate-500` a `text-slate-400`.
- El separador visual "Configuracion y herramientas avanzadas" está en `text-slate-500` — es etiqueta secundaria, contraste aceptable.
- Todos los atributos de accesibilidad de 1A conservados intactos (th scope, labels sr-only, aria-disabled).

---

## M. Riesgo funcional

**Bajo.** Solo se reordenaron secciones existentes y se eliminó un botón duplicado. No se tocaron:

- Formularios POST ni sus actions
- Antiforgery tokens
- Parámetros de ruta
- Lógica Razor (variables C#)
- Estado condicional de botones
- Contratos JS

Riesgo residual menor: el `href="#listado-unidades"` en Conciliación ahora lleva hacia arriba. Es funcionalmente correcto pero puede resultar anti-intuitivo para el usuario. Documentado para 1C.

---

## N. Validaciones

- `dotnet build --configuration Release` — OK, 0 errores, 0 advertencias
- `git diff --check` — warnings en AGENTS.md y CLAUDE.md son preexistentes (trailing whitespace en markdown). La vista no tiene trailing whitespace propio.
- No se ejecutaron tests de la suite general porque no se modificaron tests ni lógica funcional.
- No se ejecutó Playwright porque no existe spec específico para Producto/Unidades.

---

## O. Procesos

No se iniciaron procesos de larga duración. Build finalizó y liberó recursos.

Procesos preexistentes del IDE (VS Code, C# DevKit, MCP servers) documentados pero no cerrados.

---

## P. Deudas restantes

- `href="#listado-unidades"` en Conciliación ahora apunta hacia arriba — puede mejorarse en 1C eliminando o reemplazando ese link.
- Mobile / scroll affordance en tablas anchas — para 1C.
- `data-oc-scroll` — para 1C.
- Acciones de fila colapsables — para fase posterior.
- La sección de Conciliación sigue siendo extensa. Posible compresión futura o colapso progresivo.

---

## Q. Próximo paso recomendado

**MISA-INVENTARIO-FISICO-UX-1C — Mobile / scroll affordance en Producto/Unidades**

Objetivo tentativo:
- Revisar tablas anchas en mobile
- Agregar `data-oc-scroll` si corresponde
- Mejorar layout híbrido tabla/cards si hace falta
- Sin backend, sin cambios funcionales
- JS solo si el patrón data-oc-scroll lo requiere

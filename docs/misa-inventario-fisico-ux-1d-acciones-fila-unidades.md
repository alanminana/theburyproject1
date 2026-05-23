# MISA-INVENTARIO-FISICO-UX-1D — Acciones de fila colapsables / reducción de densidad en Producto/Unidades

## A. Objetivo

Reducir la densidad visual de la columna Acciones en `Views/Producto/Unidades.cshtml`, especialmente en mobile y en filas con varias acciones condicionales activas.

Mejorar UX sin cambiar lógica funcional, backend, JS, CSS, endpoints, payloads ni reglas de negocio.

---

## B. Base y contexto

- Rama base: `main` en `59f0716` (MISA-INVENTARIO-FISICO-UX-1C integrada)
- Rama de trabajo: `misa/inventario-fisico-ux-1d-acciones-fila-unidades`
- Especialista: Misa (Inventario / Catálogo / Movimientos / Marcas / Categorías / Inventario físico)

---

## C. Deuda tomada desde MISA-INVENTARIO-FISICO-UX-1C

Deudas abiertas heredadas:

- Columna Acciones usaba `min-w-[18rem]`, haciendo la tabla muy ancha.
- Cada fila podía mostrar muchas acciones/formularios en vertical.
- Las filas podían alcanzar 150–200px de alto cuando había múltiples acciones activas.
- Las acciones frecuentes (Ver historial) y operativas (Marcar faltante, Reintegrar, Baja, Finalizar reparación) no estaban diferenciadas visualmente.
- En mobile, las acciones de fila ocupaban mucho ancho y alto sin jerarquía.

---

## D. Archivos auditados

- `Views/Producto/Unidades.cshtml` — archivo principal modificado
- `Views/Caja/DetallesApertura_tw.cshtml` — referencia para patrón `<details>/<summary>`
- `Views/Cotizacion/Index_tw.cshtml` — referencia para patrón `<details>/<summary>` compacto
- `Views/PlantillaContratoCredito/Index.cshtml` — referencia para `<details>` simple
- `wwwroot/css/cliente-module.css` — referencia para `.cliente-row-menu` (patrón de menú por fila)
- `wwwroot/css/shared-components.css` — patrón `.row-action` documentado

---

## E. Hallazgos de densidad

### Estado previo (líneas 184–266)

```html
<div class="flex min-w-[18rem] flex-col items-stretch gap-2">
    <a>Ver historial</a>
    @if (hayAccionesOperativas)
    {
        <div class="grid gap-2">
            <!-- hasta 4 formularios completos con input + button cada uno -->
        </div>
    }
</div>
```

Problemas identificados:
- `min-w-[18rem]` = 288px de columna como mínimo, siempre, aunque no hubiera acciones.
- Cuando las 4 acciones estaban activas, la columna crecía verticalmente 150–200px.
- Sin jerarquía: Ver historial (lectura) y Marcar faltante/Dar de baja (operativas/destructivas) en el mismo nivel visual.
- Mobile: el scroll horizontal necesario se amplificaba por el ancho mínimo forzado.

---

## F. Acciones frecuentes vs operativas

| Acción | Tipo | Condición | Criticidad |
|--------|------|-----------|------------|
| Ver historial | Consulta | Siempre visible | Baja — lectura |
| Marcar faltante | Operativa | `PuedeMarcarFaltante` | Media — cambia estado |
| Reintegrar a stock | Operativa | `PuedeReintegrarAStock` | Media — cambia estado |
| Dar de baja | Operativa/destructiva | `PuedeDarBaja` | Alta — acción irreversible |
| Finalizar reparación | Operativa | `PuedeFinalizarReparacion` | Alta — requiere decisión (destino) |

Decisión de jerarquía:
- **Ver historial** → siempre visible, nivel primario.
- **Acciones operativas** → agrupadas bajo `<details>` colapsable, nivel secundario.

Esta jerarquía respeta la frecuencia real de uso: el historial se consulta frecuentemente; las acciones de cambio de estado son menos frecuentes y requieren intención deliberada.

---

## G. Alternativas evaluadas

### Alternativa A: `<details>/<summary>` nativo (elegida)
- Sin JS.
- Accesible por teclado (Tab + Enter/Space).
- Semántica HTML nativa.
- Patrón ya existente en el proyecto (`Caja/DetallesApertura_tw.cshtml`, `Cotizacion/Index_tw.cshtml`).
- Forms, IDs, labels, AntiForgeryToken, inputs y buttons sin cambios.
- Reducción del min-width del wrapper.

### Alternativa B: Dropdown con JS
- Descartada — esta fase prohíbe JS.
- Mayor complejidad de mantenimiento.

### Alternativa C: Convertir tabla a cards en mobile
- Descartada — esta fase prohíbe cambiar la estructura de la tabla.
- Impacto mucho mayor, requiere fase dedicada.

### Alternativa D: Reducir solo el min-width, sin colapsar
- Evaluada como mejora parcial insuficiente.
- No resuelve el problema de densidad vertical con múltiples acciones activas.

---

## H. Cambios aplicados

**Archivo modificado:** `Views/Producto/Unidades.cshtml`

### Cambio 1: Reducción de min-width del wrapper

```diff
- <div class="flex min-w-[18rem] flex-col items-stretch gap-2">
+ <div class="flex min-w-[14rem] flex-col items-stretch gap-2">
```

Reducción: 18rem → 14rem (≈64px menos de ancho mínimo en la columna).

### Cambio 2: Agrupación de acciones operativas bajo `<details>`

```diff
- <div class="grid gap-2">
-     @if (unidad.PuedeMarcarFaltante) { <form>...</form> }
-     @if (unidad.PuedeReintegrarAStock) { <form>...</form> }
-     @if (unidad.PuedeDarBaja) { <form>...</form> }
-     @if (unidad.PuedeFinalizarReparacion) { <form>...</form> }
- </div>
+ <details class="rounded-lg border border-slate-700">
+     <summary class="flex cursor-pointer select-none items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-400 transition-colors hover:bg-slate-800 hover:text-slate-200">
+         <span class="material-symbols-outlined text-base">settings</span>
+         Gestionar unidad
+     </summary>
+     <div class="grid gap-2 border-t border-slate-800 p-2">
+         @if (unidad.PuedeMarcarFaltante) { <form>...</form> }
+         @if (unidad.PuedeReintegrarAStock) { <form>...</form> }
+         @if (unidad.PuedeDarBaja) { <form>...</form> }
+         @if (unidad.PuedeFinalizarReparacion) { <form>...</form> }
+     </div>
+ </details>
```

El `<details>` reemplaza el `<div class="grid gap-2">` que agrupaba las acciones condicionales. El bloque `@if (hayAccionesOperativas)` del Razor permanece idéntico como condición de render.

---

## I. Tratamiento de formularios POST

Todos los formularios POST permanecen **exactamente idénticos** — solo cambia el contenedor HTML que los envuelve. En particular:

- `<form method="post" asp-controller="..." asp-action="...">` — sin cambios
- `@Html.AntiForgeryToken()` — sin cambios
- `<input type="hidden" name="ProductoUnidadId" value="@unidad.Id" />` — sin cambios
- `<label class="sr-only">` — sin cambios
- `<input id="motivo-*-@unidad.Id" name="Motivo" maxlength="500" required ...>` — sin cambios
- `<button type="submit" ...>` — sin cambios
- `<select name="EstadoDestino" required>` — sin cambios (solo en FinalizarReparacion)

Los forms dentro de `<details>` funcionan igual que fuera: al expandirse el `<details>`, los inputs son accesibles; al hacer submit, el form POST se envía normalmente. No existe restricción HTML que impida forms dentro de `<details>`.

---

## J. Contratos preservados

- `asp-controller`, `asp-action`, `asp-route-*` — todos sin cambios
- `name="ProductoUnidadId"`, `name="Motivo"`, `name="EstadoDestino"` — sin cambios
- `id="motivo-faltante-@unidad.Id"`, `id="motivo-reintegrar-@unidad.Id"`, `id="motivo-baja-@unidad.Id"`, `id="motivo-reparacion-@unidad.Id"` — sin cambios
- Labels `for="motivo-*-@unidad.Id"` con clase `sr-only` (agregados en 1A) — sin cambios
- `AntiForgeryToken` en cada form — sin cambios
- `required` en inputs y select — sin cambios
- `maxlength="500"` — sin cambios
- Colores semánticos de botones (amber/faltante, emerald/reintegrar, red/baja, blue/reparacion) — sin cambios
- Hint mobile de scroll (agregado en 1C) — sin cambios
- `scope="col"` en encabezados de tabla (agregado en 1A) — sin cambios
- `aria-disabled` en trazabilidad bloqueada (agregado en 1A) — sin cambios

---

## K. Qué no se tocó

- Backend — ningún controller, service, entidad, migración o DTO
- JS (`wwwroot/js/`) — ningún archivo
- CSS (`wwwroot/css/`) — ningún archivo
- Endpoints, payloads, forms — ningún cambio funcional
- `Views/Producto/UnidadesGlobal.cshtml`
- `Views/Producto/UnidadHistorial.cshtml`
- `Views/MovimientoStock/`
- `Views/Catalogo/`
- `Views/AlertaStock/`
- Ventas / Kira
- Cotización
- Caja, Crédito, stock funcional
- Conciliación de stock (sección inferior de la misma vista)
- Carga masiva de unidades (sección inferior de la misma vista)
- Filtros de la vista
- Encabezado y breadcrumb

---

## L. Cambios que debería notar el usuario

- **Columna Acciones más estrecha** por defecto (−64px de ancho mínimo). La tabla es menos ancha horizontalmente, especialmente en mobile.
- **"Ver historial" siempre visible** como acción primaria, sin cambio de comportamiento.
- **Acciones operativas ocultas por defecto** detrás de un toggle "Gestionar unidad" con ícono de engranaje. Se expanden haciendo clic o con teclado (Tab + Enter/Space).
- **Filas más compactas** cuando la unidad tiene acciones operativas disponibles (no se expanden verticalmente hasta que el usuario lo solicita deliberadamente).
- **Sin cambio en el comportamiento funcional**: al expandir y completar un form, el POST funciona igual que antes.

---

## M. Riesgo funcional

**Riesgo: Bajo.**

- `<details>/<summary>` es HTML nativo, sin dependencias.
- Los forms dentro de `<details>` envían POST normalmente.
- No se tocaron endpoints ni contratos.
- El patrón ya existe en el proyecto (Caja, Cotizacion).
- Accesible por teclado sin JS adicional.

Riesgo residual menor:
- Si un usuario no nota el toggle "Gestionar unidad", puede no acceder a las acciones operativas. El texto es claro y el ícono de engranaje es reconocible. Mitigado por el QA visual previsto en 1D-QA.

---

## N. Tests y validaciones

### Build

```
dotnet build --configuration Release
→ Compilación correcta. 0 Advertencias. 0 Errores.
```

### Tests unitarios

No se modificaron tests. No se ejecutó `dotnet test` — el cambio es Razor/HTML puro sin lógica de negocio.

### Playwright

No existe spec específico para Producto/Unidades, Inventario físico, acciones de fila ni mobile de esta pantalla.

No se ejecutó Playwright — el cambio no altera flujos funcionales ni contratos de selectors usados por tests existentes.

---

## O. Playwright

No ejecutado. Motivo: no hay cobertura específica para esta pantalla y el cambio es Razor/HTML localizado que no altera flujos funcionales.

---

## P. Procesos

Al finalizar la tarea no quedaron procesos iniciados por ella.

Build ejecutado con `dotnet build --configuration Release` (proceso terminado).

---

## Q. Deudas restantes

- **QA visual/manual pendiente** (MISA-INVENTARIO-FISICO-UX-QA): verificar desktop y mobile, confirmar que el toggle "Gestionar unidad" es visible y usable, revisar listado, filtros, acciones, carga masiva y conciliación.
- **Sin Playwright específico**: no existe cobertura E2E para Producto/Unidades. Evaluar en QA si se necesita agregar spec mínima.
- **Carpeta `docs/`**: los documentos de fase crecen. Evaluar si hace falta un índice o limpieza periódica.

---

## R. Próximo paso recomendado

**MISA-INVENTARIO-FISICO-UX-QA**

Objetivo: QA visual y manual de Producto/Unidades post 1A–1D.

Verificar:
- Desktop y mobile.
- Listado principal, filtros y paginado.
- Columna Acciones: toggle visible, expandible, forms operativos funcionales.
- Carga masiva de unidades.
- Conciliación de stock.
- Confirmar que 1A–1D dejaron la pantalla clara.
- Decidir si hace falta MISA-INVENTARIO-FISICO-UX-2 o si se puede pasar a Catálogo/Movimientos general.

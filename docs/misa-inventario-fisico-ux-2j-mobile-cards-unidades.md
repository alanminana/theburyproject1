# MISA-INVENTARIO-FISICO-UX-2J — Tabla mobile de unidades de producto

## A. Objetivo

Mejorar la experiencia mobile del listado de unidades fisicas en `Producto/Unidades/{id}` reemplazando la tabla de 9 columnas (inutilizable en mobile) por un layout de cards por unidad, visible unicamente en mobile. La tabla de desktop permanece intacta.

---

## B. Base y contexto

- Base: `main` en `f17ed33` — MISA-INVENTARIO-FISICO-UX-2I integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2j-mobile-cards-unidades`.
- Tipo de fase: Razor-only / mobile UX / bajo-medio riesgo.
- Los dialogs ya estaban fuera del `<td>` desde 2H — condicion necesaria para que los cards puedan invocarlos sin duplicarlos.

---

## C. Hallazgos tomados de 2G

| # | Problema | Severidad |
|---|----------|-----------|
| G3 | Tabla inutilizable en mobile (9 columnas, ACCIONES off-screen) | Alta |
| I1 | ACCIONES completamente inaccesible sin scroll horizontal | Alta |
| I2 | Codigo de unidad en 4 lineas — baja legibilidad | Alta |
| I3 | Dialog roto en mobile — ya resuelto en 2H | Alta |

---

## D. Problema mobile

La `<section id="listado-unidades">` contenia una tabla de 9 columnas: Unidad, Serie, Estado, Ubicacion, Ingreso, Cliente, Venta, Observaciones y Acciones. En mobile (390px):

- La columna Unidad mostraba el codigo `tele-smart-sam-U-0001` en 4 lineas.
- Las columnas Ingreso, Cliente, Venta, Observaciones y **Acciones quedaban completamente fuera de pantalla**.
- El usuario no podia acceder a "Gestionar unidad" ni "Ver historial" sin scroll horizontal.
- Cuando scrolleaba para llegar a ACCIONES, perdia el contexto de la fila.
- El hint "Deslizá para ver más columnas →" no resolvía la inutilidad operativa.

---

## E. Solucion elegida

**Vista dual por breakpoint** con clases Tailwind:

- Mobile/tablet (`< lg`): cards por unidad con `class="divide-y divide-slate-800 lg:hidden"`.
- Desktop (`>= lg`): tabla existente con `class="hidden lg:block"`.

Los `<dialog>` ya estaban fuera de la tabla (fix de 2H). Los cards referencian los mismos `id="acciones-unidad-{unidad.Id}"` via `showModal()` desde botones nuevos. No se duplicaron dialogs ni forms POST.

Esta opcion fue elegida sobre alternativas porque:
- No requiere JS adicional.
- No duplica contratos criticos.
- Es reversible sin impacto funcional.
- Reutiliza el mismo patron `lg:hidden` / `hidden lg:block` usado en otras partes del proyecto.

---

## F. Cambios aplicados

### F.1. Estructura en `<section id="listado-unidades">`

**Antes:**
```
<section id="listado-unidades">
    <p class="... lg:hidden">Deslizá para ver más columnas →</p>
    <div class="overflow-x-auto">
        <table> ... 9 columnas ... </table>
    </div>
</section>
```

**Despues:**
```
<section id="listado-unidades">
    <div class="divide-y divide-slate-800 lg:hidden">
        @foreach (var unidad in Model.Unidades) { ... cards ... }
    </div>
    <div class="hidden lg:block">
        <div class="overflow-x-auto">
            <table> ... misma tabla de 9 columnas ... </table>
        </div>
    </div>
</section>
```

### F.2. Contenido de cada card mobile

Cada card muestra:
- Codigo de unidad: `font-mono text-sm font-bold text-white` con `break-all`.
- Badge de estado: partial `_EstadoUnidadBadge` (intacto).
- Ver historial: link con mismo `asp-controller`, `asp-action`, `asp-route-unidadId`.
- Gestionar: boton que invoca `document.getElementById('acciones-unidad-{id}').showModal()` — mismo ID del dialog existente fuera de la tabla.
- `<dl>` con grid 2 columnas para Serie, Ubicacion, Ingreso, Cliente, Venta (condicionales por `!= null` o `.HasValue`).
- Observaciones: `line-clamp-2 text-xs text-slate-400` si no es null.

### F.3. Eliminado

- `<p class="px-4 pt-3 text-xs text-slate-500 lg:hidden" aria-hidden="true">Deslizá para ver más columnas →</p>` — no es necesario porque en mobile los usuarios ven cards (no la tabla).

---

## G. Contratos preservados

- `id="listado-unidades"` en el `<section>` — anchor intacto.
- `id="acciones-unidad-{unidad.Id}"` en cada dialog — sin cambios.
- `aria-labelledby`, `role="dialog"`, `aria-modal` en dialogs — sin cambios.
- `form method="dialog"` para cierre nativo — sin cambios.
- Formularios POST: `MarcarUnidadFaltante`, `DarUnidadBaja`, `ReintegrarUnidadAStock`, `FinalizarReparacionUnidad` — sin cambios.
- `@Html.AntiForgeryToken()` en todos los forms — sin cambios.
- `ProductoUnidadId`, `Motivo`, `EstadoDestino` en inputs — sin cambios.
- `asp-controller`, `asp-action`, `asp-route-unidadId` en links — sin cambios.
- `asp-for` en todos los campos de formulario — sin cambios.
- Partial `_EstadoUnidadBadge` — intacto.
- `scroll-mt-32` en `#modo-unidades` — intacto.
- Dialogs fuera del `<td>` (fix de 2H) — intactos.

---

## H. Que no se toco

- Backend, controllers, services, models, viewmodels, migraciones.
- Endpoints, payloads, permisos, reglas de stock, calculos.
- CSS global, JS global.
- Tests (ningun assert del archivo de tests fue afectado — los cards agregan HTML nuevo, no eliminan strings existentes).
- Playwright specs.
- Ventas/Kira, Catalogo, Movimientos, AlertaStock, Cotizacion.
- Modo Carga, Modo Conciliacion, Modo Configuracion.
- Dialogs y formularios de acciones.
- Tabla desktop — queda igual, solo envuelta en `hidden lg:block`.

---

## I. Riesgo funcional

**Bajo.** Los cambios son exclusivamente de estructura HTML y clases responsive. Los dialogs no fueron duplicados ni movidos. Los botones "Gestionar" en los cards usan `showModal()` con el mismo ID que usaba el boton en la tabla — el vinculo boton-dialog es identico. El unico riesgo residual: si algun script externo recorriera los hijos directos del `<section id="listado-unidades">` para encontrar botones, encontraria botones en ambas zonas (cards + tabla). No existe tal script en la codebase.

---

## J. Validaciones

### Build
```
dotnet build --configuration Release
→ Compilacion correcta. 0 Advertencia(s), 0 Errores. Tiempo: 00:01:35.92.
```

### Tests
```
dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests"
→ Correctas! Con error: 0, Superado: 79, Omitido: 0, Total: 79. Duracion: 15 s.
```

### git diff --check
```
→ Sin trailing whitespace en Views/Producto/Unidades.cshtml.
```

---

## K. Tests/Playwright ejecutados u omitidos con motivo

- `ProductoControllerPrecioTests`: 79/79 OK — ejecutados porque la fase modifica el archivo de vista Unidades.cshtml. Los tests verifican strings con `Assert.Contains` — todos los strings existentes siguen presentes; los cards agregan nuevo HTML sin eliminar nada.
- Suite general: no ejecutada. La fase es Razor-only y no toca logica de negocio ni otros controladores.
- Playwright visual: no ejecutado. La app no estaba disponible con sesion autenticada. El cambio es verificable estructuralmente: la seccion `lg:hidden` contiene los cards con acciones accesibles sin scroll horizontal, y la seccion `hidden lg:block` preserva la tabla de 9 columnas para desktop.

---

## L. Deudas restantes

- Tabs sin estado activo dinamico: deuda para MISA-INVENTARIO-FISICO-UX-2K.
- Labels de tarjetas de resumen en `text-slate-500` con contraste bajo: deuda para 2K.
- No hay Playwright especifico para Producto/Unidades (deuda preexistente de 2C/2F).
- La fecha en las cards mobile usa formato abreviado `dd/MM/yy HH:mm` — en desktop sigue `dd/MM/yyyy HH:mm`. Diferencia intencional para ahorrar espacio mobile; se puede revisar en 2K si el usuario lo prefiere unificado.

---

## M. Proximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-FISICO-UX-2K — Estado activo de tabs y contraste menor

Actuá como Misa y seguí estrictamente AGENTS.md / CLAUDE.md.

Base: main con MISA-INVENTARIO-FISICO-UX-2J integrada.

Fase: MISA-INVENTARIO-FISICO-UX-2K
Tipo: Razor-only / JS minimo / polish

Objetivos:
1. Agregar estado activo dinamico a los 4 tabs de modos usando Intersection Observer
   (JS minimo inline o en bloque @section Scripts — sin archivo nuevo).
   El tab activo debe reflejar la seccion visible en pantalla.
2. Mejorar contraste de labels de tarjetas de resumen de text-slate-500 a text-slate-400.
3. Separador visual mas fuerte entre modos: border-t-2 border-slate-700 en lugar de
   border-t border-slate-800.

Alcance: solo Views/Producto/Unidades.cshtml.
No tocar: backend, controllers, services, endpoints, payloads, formularios POST,
contratos JS externos, CSS global, tests salvo actualizacion de contratos visuales.
Validar: build Release + tests de contrato UI.
```

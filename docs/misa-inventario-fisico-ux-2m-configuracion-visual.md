# MISA-INVENTARIO-FISICO-UX-2M — Revision visual modo Configuracion

## A. URL exacta auditada

```
http://localhost:5187/Producto/Unidades/21#modo-configuracion
```

## B. Producto / id usado

- Producto id: 21 (reportado por el usuario como caso real problemático)
- QA anterior (fase 2L) auditó producto id: 20, lo que explica por qué el problema no fue detectado entonces

## C. Resultado desktop antes del fix

El problema era estructural, no visual puro. Al navegar a `#modo-configuracion`:

- El scroll cae correctamente sobre el heading "Modo Configuracion"
- Pero inmediatamente debajo de la pequeña sección Configuracion (solo un card de toggle de trazabilidad) aparecía toda la sección Conciliacion — una sección muy larga con grillas de datos, desglose de estados y acciones de ajuste
- El usuario veía el contenido de Configuracion mezclado visualmente con el inicio de Conciliacion, sin separación clara entre modos
- El tab "Configuracion" en el nav quedaba activo (JS correcto), pero el contenido debajo del heading era confuso: la sección parecía continuarse con datos de otro modo

## D. Resultado mobile 390x844 antes del fix

- El mismo problema estructural: Configuracion visible en la parte superior, Conciliacion sangrando debajo
- En mobile el efecto era peor: el usuario que llega por hash solo ve el label "Modo Configuracion" y el card de trazabilidad, pero al hacer scroll mínimo cae en el header de Conciliacion, creando confusión de contexto

## E. Problema real detectado

**Bug estructural: DOM order incorrecto.**

El orden en el DOM no coincidía con el orden de los tabs de navegación:

| Posición | Nav (correcto) | DOM antes del fix (incorrecto) |
|----------|---------------|-------------------------------|
| 1 | Unidades | Unidades |
| 2 | Carga | Carga |
| 3 | **Conciliacion** | **Configuracion** ← invertido |
| 4 | **Configuracion** | **Conciliacion** ← invertido |

Cuando el usuario abría `#modo-configuracion`:
- Scroll correcto al anchor (el JS y el hash funcionaban bien)
- Pero el contenido que seguía hacia abajo era la enorme sección Conciliacion
- Visualmente: Configuracion (pequeño) + Conciliacion (grande) = sangrado masivo, aspecto de sección rota

El QA de 2L fue válido para producto 20 pero el bug estructural era el mismo en todos los productos. El reporte del usuario con producto 21 reveló el problema real.

## F. Fix aplicado

Intercambio del orden DOM de las dos secciones para que coincida con el orden de los tabs.

**Antes:**
```
#modo-unidades   (línea 103)
#modo-carga      (línea 453)
#modo-configuracion  (línea 627) ← INCORRECTO
#modo-conciliacion   (línea 705) ← INCORRECTO
```

**Después:**
```
#modo-unidades   (línea 103)
#modo-carga      (línea 453)
#modo-conciliacion   (línea 627) ← CORRECTO
#modo-configuracion  (línea 861) ← CORRECTO
```

Cambio limitado a orden estructural en `Views/Producto/Unidades.cshtml`. Sin cambios en:
- JS de tabs (sigue funcionando igual)
- CSS / clases / scroll-mt-32
- Contenido de las secciones
- Backend, controllers, services, modelos
- Links internos (todos siguen siendo válidos)

## G. Resultado desktop después del fix

Al navegar a `#modo-configuracion`:
- Scroll cae sobre el heading "Modo Configuracion"
- Debajo: solo el card de trazabilidad (Activada/Desactivada + botón toggle)
- No hay contenido adicional que sangre: Configuracion es la última sección de la página
- Visual limpio, sección autocontenida y cerrada

## H. Resultado mobile después del fix

- Al llegar por hash: el usuario ve el label "Modo Configuracion", el card de trazabilidad, y el pie de página
- No hay confusión con otra sección debajo
- El scroll termina naturalmente en Configuracion

## I. Contratos preservados

- `id` de todas las secciones: preservados (`modo-unidades`, `modo-carga`, `modo-conciliacion`, `modo-configuracion`)
- `id` internos: `listado-unidades`, `form-carga-masiva-unidades`, `ajuste-asistido` — sin cambios
- JS de tabs: sin modificar — sigue leyendo `location.hash` y activando el link correcto
- `asp-*`, antiforgery, `name`, `data-*`: preservados en su totalidad
- Links cruzados: todos válidos en el nuevo orden
  - `#modo-conciliacion` desde Configuracion → scroll hacia arriba (ahora Conciliacion está antes) — comportamiento correcto
  - `#listado-unidades`, `#form-carga-masiva-unidades` desde Conciliacion → sin cambio de sección, solo diferente posición en página
- Modales de acciones de unidad: sin cambios

## J. Validaciones ejecutadas

- `dotnet build --configuration Release` → OK (0 errores, 0 advertencias)
- `dotnet test --filter "FullyQualifiedName~ProductoControllerPrecioTests"` → 79/79 OK
- `git diff --check` → Views/Producto/Unidades.cshtml sin trailing whitespace

Playwright MCP no disponible (instancias paralelas bloquean el perfil Chrome compartido). La auditoría visual se realizó por análisis de código y verificación estructural del DOM.

## K. Decisión final

Fix aplicado. El bug era estructural (orden DOM ≠ orden nav), no visual puro. El QA anterior (2L) fue correcto para lo que auditó (producto 20, viewport visual), pero no detectó el bug de orden entre secciones porque Conciliacion y Configuracion tienen IDs distintos y el problema solo se hace evidente al navegar específicamente a `#modo-configuracion` y observar qué sección aparece debajo.

## L. Commit

```
ad618b3 Corregir visual de configuracion en unidades de producto (MISA-INVENTARIO-FISICO-UX-2M)
```

Rama: `misa/inventario-fisico-ux-2m-configuracion-visual`

## M. Procesos

- Build y test ejecutados y terminados normalmente
- No quedan procesos activos iniciados por esta tarea
- Playwright MCP preexistente: múltiples instancias node del IDE, no iniciadas por esta tarea, no cerradas

## N. Working tree final (rama)

```
M  Views/Producto/Unidades.cshtml  (commiteado)
 M .claude/settings.local.json     (pre-existente, no commiteado)
 M AGENTS.md                       (pre-existente, no commiteado)
 M CLAUDE.md                       (pre-existente, no commiteado)
 M docs/misa-catalogo-ux-1g-aria-live-modales.md (pre-existente, no commiteado)
 D skills-lock.json                (pre-existente, no commiteado)
```

## O. Próximo paso recomendado

Merge fast-forward a main y push:

```powershell
git checkout main
git merge --ff-only misa/inventario-fisico-ux-2m-configuracion-visual
git push
```

Luego el usuario puede verificar en el navegador:
- `http://localhost:5187/Producto/Unidades/21#modo-configuracion`
- Confirmar que Configuracion aparece limpia, sin sangrado de Conciliacion debajo

# MISA-INVENTARIO-FISICO-UX-2K — Estado activo de tabs y contraste menor

## A. Objetivo

Mejorar el estado visual de los tabs internos y corregir detalles menores de contraste en `Producto/Unidades`, preservando toda la funcionalidad existente.

---

## B. Base y contexto

- Base: `main` en `edce4cf` — MISA-INVENTARIO-FISICO-UX-2J integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2k-tabs-contraste`.
- Tipo de fase: Razor + JS minimo inline / UX visual menor / accesibilidad / bajo riesgo.
- Los cards mobile (2J), dialogs fuera del `<td>` (2H) y limpieza visual (2I) ya estaban integrados.

---

## C. Deudas tomadas desde 2J

| Deuda | Fuente | Resolucion |
|-------|--------|------------|
| Tabs sin estado activo dinamico | 2G, 2I, 2J | Resuelta — JS hash-based activo |
| Labels `text-slate-500` con contraste bajo | 2G (G10), 2I, 2J | Resuelta — cambiados a `text-slate-400` |
| Separador entre modos demasiado sutil | 2G (K4) | Resuelta — `border-t-2 border-slate-700` |
| Fecha mobile `dd/MM/yy` vs desktop `dd/MM/yyyy` | 2J | Documentada como intencional — no cambiada |

---

## D. Hallazgos en tabs

El nav de modos (`aria-label="Modos de trabajo de unidades"`) tenia 4 anchor links:

- Tab "Unidades": siempre con `border-primary/40 bg-primary/15 text-white` (activo permanente hardcodeado en Razor).
- Tabs "Carga", "Conciliacion", "Configuracion": siempre con `border-slate-700 text-slate-300` (inactivo permanente).

Consecuencias:
- El usuario no sabia visualmente en que seccion estaba al scrollear o navegar directamente via hash.
- Al hacer clic en "Carga" y scrollear a esa seccion, el tab "Unidades" seguia marcado como activo.
- Confirmado originalmente en auditoria 2G (problema G7 / K2 / K3).

---

## E. Hallazgos de contraste

Labels con `text-slate-500` sobre `bg-slate-950` daban contraste aproximado de 4.5:1, borderline para texto `text-xs` uppercase bold.

Instancias relevantes auditadas:

| Zona | Labels afectados |
|------|-----------------|
| Resumen unidades fisicas | "Unidades listadas", labels de estado por tipo |
| Filtros de busqueda | "Codigo interno o serie", "Estado" |
| Cards mobile (DT) | "Serie", "Ubicacion", "Ingreso", "Cliente", "Venta" |
| Conciliacion — 4 cards principales | "Stock agregado actual", "Unidades fisicas disponibles", "Diferencia", "Unidades registradas" |
| Conciliacion — desglose | "Vendidas", "Faltantes", "Baja", "Devueltas", "Reservadas", "En reparacion" |

No cambiados (texto verdaderamente secundario):
- "Valor numerico del producto." y "Unidades en estado EnStock." dentro de las cards (ayudas contextuales, no labels de datos).
- "Estos estados ayudan a explicar el desvio..." en el desglose.
- Labels de los formularios CrearUnidad y CargaMasiva (fuera del alcance minimo).

---

## F. Decision sobre tab activo

**Opcion B — JS minimo inline** — elegida porque la implementacion resulto simple y segura.

Mecanismo:
- IIFE en bloque `@section Scripts` al final de la vista.
- Busca el nav via `querySelector('[aria-label="Modos de trabajo de unidades"]')`.
- En cada tab, reemplaza el `className` completo con la cadena ACTIVE o INACTIVE predefinida.
- Ejecuta `setActive(location.hash)` en carga de pagina (si no hay hash, activa `#modo-unidades`).
- Escucha `click` en cada tab anchor para actualizar el estado visualmente antes del scroll.
- Escucha `hashchange` para actualizar en navegacion browser (atras/adelante).

Fallback sin JS: el tab "Unidades" sigue renderizado como activo por Razor (comportamiento actual). Sin regresion funcional.

La cadena de clases ACTIVE y INACTIVE usa exactamente las mismas clases ya presentes en el HTML Razor, por lo que estan garantizadas en el CSS Tailwind compilado.

---

## G. Cambios aplicados

### G.1. Separadores entre modos — `border-t border-slate-800` → `border-t-2 border-slate-700`

Cambiados en los 4 encabezados de modo:
- `<div id="modo-unidades">` header div
- `<div id="modo-carga">` header div
- `<div id="modo-configuracion">` header div
- `<div id="modo-conciliacion">` header div

Resultado: el separador es levemente mas prominente (2px vs 1px, gris un tono mas claro).

### G.2. Contraste — `text-slate-500` → `text-slate-400` en labels importantes

17 instancias cambiadas (lista en seccion E). Las instancias secundarias se mantuvieron en `text-slate-500`.

### G.3. Tabs activos — bloque JS inline

Agregado `@section Scripts { <script>...</script> }` al final de la vista.

---

## H. Decision sobre fecha mobile

**Mantener `dd/MM/yy HH:mm` en cards mobile.**

Motivo: formato intencional desde 2J para ahorrar espacio en el grid de 2 columnas (`dl.grid-cols-2`). El contexto es un ERP donde todas las fechas son recientes — no hay ambiguedad por el año abreviado. El formato `dd/MM/yyyy` agregaría 2 caracteres y podria forzar saltos de linea en mobile.

Si el usuario prefiere unificar, es un cambio de 1 linea en el formato de fecha del `<dd>` del DT "Ingreso".

---

## I. Contratos preservados

- `id="modo-unidades"`, `id="modo-carga"`, `id="modo-configuracion"`, `id="modo-conciliacion"` — intactos.
- `id="listado-unidades"`, `id="form-carga-masiva-unidades"`, `id="ajuste-asistido"` — intactos.
- `scroll-mt-32` en los cuatro wrappers de modo — intactos.
- `aria-label="Modos de trabajo de unidades"` en el nav — intacto (usado por el JS).
- `href="#modo-unidades"`, `href="#modo-carga"`, etc. en los anchors del nav — intactos (usados por el JS).
- Todos los `<dialog id="acciones-unidad-{id}">` — fuera del `<td>`, sin cambios.
- Formularios POST: `CrearUnidad`, `CrearUnidadesMasivas`, `MarcarUnidadFaltante`, `DarUnidadBaja`, `ReintegrarUnidadAStock`, `FinalizarReparacionUnidad`, `AjustarStockAgregadoAUnidadesFisicas`, `AjustarStockAgregadoHaciaAbajo` — sin cambios.
- `@Html.AntiForgeryToken()` en todos los formularios — sin cambios.
- `name`, `id`, `required`, `maxlength` en todos los inputs — sin cambios.
- `asp-action`, `asp-controller`, `asp-route` — sin cambios.
- `ProductoUnidadId`, `Motivo`, `EstadoDestino` en inputs ocultos y selects — sin cambios.
- Partial `_EstadoUnidadBadge` — intacto.
- Cards mobile (2J) — intactas.
- Tabla desktop (hidden lg:block) — intacta.

---

## J. Que no se toco

- Backend, controllers, services, models, viewmodels, migraciones.
- Endpoints, payloads, permisos, reglas de stock, calculos.
- CSS global, JS global.
- Tests (ninguno necesito actualizacion — los tests verifican texto funcional, no clases CSS).
- Playwright specs.
- Ventas/Kira, Catalogo, Movimientos, AlertaStock, Cotizacion.
- Modo Carga — logica, formularios y campos intactos.
- Modo Configuracion — logica, toggle y formularios intactos.
- Modo Conciliacion — logica, metricas, ajuste asistido intactos.
- Dialogs de acciones por unidad — contenido intacto.

---

## K. Riesgo funcional

**Bajo.**

- Los cambios de clase CSS no afectan logica ni contratos.
- El JS es un IIFE autocontenido que no declara variables globales. Si el nav no existe, retorna sin errores.
- El JS reemplaza `className` con strings identicos a los usados en Razor — no hay riesgo de crear clases ausentes en el CSS compilado.
- El `setActive` inicial en page load respeta el hash de la URL (si llegan a `#modo-carga`, ese tab se activa desde el primer render JS).
- Sin fallback se pierde el estado activo dinamico pero no se rompe nada funcional.

---

## L. Validaciones

### Build

```
dotnet build --configuration Release
→ Compilacion correcta. 0 Advertencia(s), 0 Errores. Tiempo: 00:01:39.97.
```

### Tests

```
dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests"
→ Correctas! Con error: 0, Superado: 79, Omitido: 0, Total: 79. Duracion: 14 s.
```

### git diff --check

```
git diff --check -- Views/Producto/Unidades.cshtml
→ Sin trailing whitespace.
```

---

## M. Tests/Playwright ejecutados u omitidos con motivo

- `ProductoControllerPrecioTests`: 79/79 OK — ejecutados porque la fase modifica Razor de la vista Unidades.
- Suite general: no ejecutada. La fase es Razor + JS inline de Producto/Unidades. No se tocan backend, endpoints, reglas ni logica de negocio.
- Playwright visual: no ejecutado. La app no estaba disponible con sesion autenticada al momento de la tarea. Los cambios son verificables por lectura de markup: el `@section Scripts` queda correctamente al final del archivo, las clases de tabs estan definidas en las constantes JS usando los mismos strings que el Razor original, y los cambios de contraste son de clase CSS pura.

---

## N. Deudas restantes

- No hay Playwright especifico para Producto/Unidades (deuda preexistente de 2C/2F, no abierta en esta fase).
- El JS de tabs actua sobre click/hashchange pero no sobre scroll (Intersection Observer). Si el usuario scrollea manualmente sin hacer clic en un tab, el estado activo no se actualiza. Esta es una limitacion documentada y aceptable — mejora respecto al estado anterior (siempre activo Unidades) pero no llega a un sistema de tabs completamente dinamico por scroll.
- Fecha mobile `dd/MM/yy` documentada como intencional; puede unificarse si el usuario lo prefiere.
- Labels de los formularios CrearUnidad y CargaMasiva siguen en `text-slate-500` — excluidos del alcance minimo de esta fase.

---

## O. Proximo prompt recomendado

```
PROMPT — MISA-INVENTARIO-FISICO-UX-2L — QA visual final Producto/Unidades

Actuá como Misa y seguí estrictamente AGENTS.md / CLAUDE.md.

Base: main con MISA-INVENTARIO-FISICO-UX-2K integrada.

Fase: MISA-INVENTARIO-FISICO-UX-2L
Tipo: QA visual / Playwright / audit-only

Objetivo: validar visualmente con Playwright la pantalla Producto/Unidades/{id}
en desktop y mobile tras las fases 2H-2K, confirmando que:
1. El tab activo se actualiza dinamicamente al hacer click.
2. El contraste de labels mejorado es perceptible.
3. Los separadores entre modos son mas visibles.
4. Las cards mobile funcionan igual que en 2J.
5. Los dialogs de acciones siguen centrados y con backdrop.
6. Los anchors de modos funcionan sin overlap del sticky nav.

Validaciones: Playwright desktop 1440x900 + mobile 390x844 en /Producto/Unidades/20.
No tocar backend, CSS global, formularios ni contratos.
```

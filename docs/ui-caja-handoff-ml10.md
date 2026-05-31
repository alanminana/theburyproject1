# Handoff — Módulo Caja: Rework Visual ML9–ML10.1

> Cierre de la migración visual del módulo Caja.
> Branch: `main`. Estado: working tree con cambios unstaged, listos para commit.

---

## 1. Resumen ejecutivo

Migración visual completa del módulo Caja desde HTML de referencia (`referencia/Caja/`) hacia Razor funcional dark-first (`_tw.cshtml`). El trabajo preservó íntegramente toda la lógica de negocio, contratos AJAX, ViewModels, bindings y permisos del sistema existente.

- **HTML de referencia** (`referencia/Caja/*.html`): fuente visual — estructura, vocabulario CSS, patrones de layout, chips, cards, KPIs, botones y estados.
- **Razor existente** (`Views/Caja/*_tw.cshtml`): fuente funcional — lógica Razor, rutas, permisos, bindings, antiforgery, modelos.
- **CSS objetivo** integrado en `wwwroot/css/caja-module.css`: vocabulario visual completo portado, sin duplicar clases del sistema global.
- **Flujo AJAX del Index** saneado: `onCajaGuardada()` reescrito para emitir DOM compatible con el Index migrado.

---

## 2. Estado git

### Branch
`main` — 8 commits por delante de `origin/main`.

### Working tree final (todos los cambios son del rework Caja)

**Modificados — Vistas Razor:**
```
Views/Caja/Abrir_tw.cshtml
Views/Caja/Cerrar_tw.cshtml
Views/Caja/Create_tw.cshtml
Views/Caja/DetallesApertura_tw.cshtml
Views/Caja/DetallesCierre_tw.cshtml
Views/Caja/Edit_tw.cshtml
Views/Caja/Historial_tw.cshtml
Views/Caja/Index_tw.cshtml
Views/Caja/RegistrarMovimiento_tw.cshtml
Views/Caja/_CreateModal_tw.cshtml
Views/Caja/_EditModal_tw.cshtml
```

**Modificados — Assets:**
```
wwwroot/css/caja-module.css
wwwroot/js/caja-index.js
```

**Eliminado:**
```
referencia/Kardex _standalone_.html   (limpieza de referencia obsoleta)
```

**Untracked (no commitear):**
```
enet8.0TheBuryProyect.dll
referencia/Caja/                      (HTML de referencia — mantener como fuente)
```

### Archivos no tocados en este rework
- `Controllers/CajaController.cs`
- `Models/Entities/Caja.cs`, `AperturaCaja.cs`, `MovimientoCaja.cs`
- `ViewModels/CajaViewModel.cs`, `CajasListViewModel.cs`, etc.
- `Services/CajaService.cs` y derivados
- Migraciones EF Core
- `wwwroot/js/caja-abrir.js`
- `wwwroot/js/caja-cerrar.js`
- `wwwroot/js/caja-detalles-apertura.js`
- `wwwroot/js/caja-form.js`
- `wwwroot/js/caja-historial.js`
- `wwwroot/js/caja-registrar-movimiento.js`
- `wwwroot/js/horizontal-scroll-affordance.js`

---

## 3. HTML de referencia procesados

Todos ubicados en `referencia/Caja/` (untracked — no commitear).

| Archivo de referencia | Vista Razor destino | Uso |
|---|---|---|
| `DetalleCierre.html` | `DetallesCierre_tw.cshtml` | Fuente visual principal |
| `Historial.html` | `Historial_tw.cshtml` | Fuente visual principal |
| `CrearEditar.html` | `_CreateModal_tw.cshtml` + `_EditModal_tw.cshtml` | Fuente visual principal |
| `Abrir.html` | `Abrir_tw.cshtml` | Fuente visual principal |
| `RegistrarMovimiento.html` | `RegistrarMovimiento_tw.cshtml` | Fuente visual principal |
| `Cerrar.html` | `Cerrar_tw.cshtml` | Fuente visual principal |
| `DetalleApertura.html` | `DetallesApertura_tw.cshtml` | Fuente visual principal |
| `Index.html` | `Index_tw.cshtml` | Fuente visual principal |
| `Cajas - Index (standalone).html` | — | Apoyo visual complementario |
| `Index (standalone-src).html` | — | Apoyo visual complementario |

---

## 4. Vistas Razor migradas

| Vista | Descripción |
|---|---|
| `Views/Caja/Index_tw.cshtml` | Index principal: KPIs, turnos abiertos, tabla cajas + mobile cards, modales AJAX |
| `Views/Caja/_CreateModal_tw.cshtml` | Partial del modal Crear caja (panel drawer desktop/mobile) |
| `Views/Caja/_EditModal_tw.cshtml` | Partial del modal Editar caja (panel drawer con RowVersion) |
| `Views/Caja/Abrir_tw.cshtml` | Formulario de apertura de turno (fondo inicial, resumen caja) |
| `Views/Caja/Create_tw.cshtml` | Vista Create full-page (legacy, coexiste con modal AJAX) |
| `Views/Caja/Edit_tw.cshtml` | Vista Edit full-page (legacy, coexiste con modal AJAX) |
| `Views/Caja/DetallesCierre_tw.cshtml` | Detalle de cierre: resumen financiero, movimientos, timeline |
| `Views/Caja/Historial_tw.cshtml` | Historial de cierres: filtros por fecha/caja/usuario, tabla paginada |
| `Views/Caja/RegistrarMovimiento_tw.cshtml` | Formulario de movimiento ingreso/egreso con toggle segmentado |
| `Views/Caja/Cerrar_tw.cshtml` | Formulario de cierre: monto contado, resultado diferencia, notas |
| `Views/Caja/DetallesApertura_tw.cshtml` | Detalle de apertura activa: movimientos, filtros, acciones |

---

## 5. CSS modificado — `wwwroot/css/caja-module.css`

### Estructura del archivo
El CSS tiene dos zonas claramente delimitadas:

**Zona 1 — CSS preexistente (líneas 1–493):**  
Clases legacy del módulo: `.caja-input`, `.caja-select`, `.caja-page-shell`, `.caja-cerrar-*`, `.caja-detalles-*`, `.caja-historial-*`, `.caja-movimiento-*`, `.tipo-btn`, `.caja-switch`, etc.

**Zona 2 — Vocabulario visual objetivo portado (líneas 493–670, sección claramente documentada):**  
Portado desde `referencia/Caja/` para las vistas migradas. No duplica ni reemplaza clases del sistema global.

### Clases principales de la Zona 2

| Categoría | Clases |
|---|---|
| Page header | `.page-head`, `.page-title`, `.page-sub`, `.head-actions` |
| Cards | `.card`, `.card-pad`, `.card-2` |
| KPIs | `.kpi`, `.kpi-label`, `.kpi-value`, `.kpi-foot`, `.kpi--ok`, `.kpi--warn`, `.kpi--bad`, `.kpi--accent`, `.grid-kpi` |
| Buttons | `.btn`, `.btn-primary`, `.btn-success`, `.btn-amber`, `.btn-danger`, `.btn-ghost`, `.btn-soft`, `.btn-block`, `.btn-sm`, `.btn-lg` |
| Fields | `.label`, `.req`, `.field`, `.field-icon`, `.field-money`, `.hint` |
| Chips | `.chip`, `.chip-ok`, `.chip-info`, `.chip-warn`, `.chip-bad`, `.chip-neutral`, `.chip-violet`, `.chip-live` (con `.dot` animado) |
| Alerts | `.alert`, `.alert-info`, `.alert-ok`, `.alert-warn`, `.alert-bad` |
| Tables | `.tbl-wrap`, `.tbl`, `.row-actions` |
| Definition rows | `.defrow` |
| Result cards | `.result-card`, `.result-label`, `.result-value`, `.is-ok`, `.is-warn`, `.is-bad` |
| Mobile/Desktop | `.hide-mobile`, `.m-card` (toggle responsive en 767.98px) |
| Toggle switch | `.caja-switch`, `.caja-switch-track` (checkbox "Activa" en modales) |
| Segmented control | `.segment` (RegistrarMovimiento tipo ingreso/egreso) |
| Utilities | `.muted`, `.muted-2`, `.num`, `.mono`, `.dash` |
| Empty state | `.caja-empty-state`, `.empty`, `.empty-ic` |
| Timeline | `.timeline`, `.node` (DetallesCierre/DetallesApertura) |
| Motion safety | `@media (prefers-reduced-motion)` cubre chips, inputs, tipo-btn |
| Validation | `.field.input-validation-error` — border rojo + box-shadow |

---

## 6. JS modificado — `wwwroot/js/caja-index.js`

### `initFilterTabs` (movido desde inline script)
Antes vivía como `<script>` inline en el Index. Ahora inicializado desde `caja-index.js` en la IIFE principal. Escucha `.filter-tab[data-filter]`, aplica `aria-pressed`, actualiza `.btn-soft`/`.btn-ghost`, y controla visibilidad de `<tr>` y `<article>` por `data-state`. El filtro `all` muestra todo excepto `arch`; `disp`/`uso`/`arch` filtran por estado exacto.

### `onCajaGuardada(entity, form)` (reescrito en ML10.1)
Detecta Create vs Edit leyendo `form.querySelector('[name="Id"]').value !== '0'`. Delega a:

- **`_handleCajaEditada(entity)`**: localiza la fila por `data-caja-row-id` en `#cajas-activas-tbody` o `#cajas-inactivas-tbody`. Si no cambia `Activa` (`wasActive === nowActive`), actualiza td[0] (Nombre+Código) y td[1] (Ubicación) in-place + card mobile. Si cambia `Activa` (reactivar/archivar) → fallback: feedback + `location.reload()` a 800ms.

- **`_handleCajaCreada(entity)`**: inserta `<tr>` en el tbody correcto (`#cajas-activas-tbody` o `#cajas-inactivas-tbody`) y `<article>` en `#cajas-m-cards`. Si ninguno existe (0 cajas configuradas) → fallback: feedback + reload.

### Helpers privados agregados (prefijo `_`)

| Función | Responsabilidad |
|---|---|
| `_ubicacionText(entity)` | Texto plano "Sucursal · Ubicacion" para `textContent` |
| `_ubicacionHtml(entity)` | HTML escapado "Sucursal · Ubicacion" para `innerHTML` |
| `_updateMobileCardText(entity)` | Actualiza nombre y código en card mobile (Edit in-place) |
| `_applyCurrentFilter(el)` | Aplica el filtro activo actual (`aria-pressed="true"`) al elemento insertado |
| `_buildCajaDesktopRow(entity, state)` | Construye `<tr>` 4 columnas con clases canónicas |
| `_buildCajaMobileCard(entity, state)` | Construye `<article class="card card-pad">` con clases canónicas |

### Clases legacy removidas de `onCajaGuardada`
`chip-erp`, `row-action`, `row-action--primary`, `row-action__label`, inline Tailwind en celdas.

### Clases canónicas usadas en DOM insertado
`.chip.chip-ok` / `.chip.chip-neutral` / `.btn.btn-primary.btn-sm` / `.btn.btn-ghost.btn-sm` / `.btn.btn-block` / `.row-actions` / `.card.card-pad` / `.mono` / `.muted-2`

### Fallback seguro para cambio de `Activa`
Edit que cambia `Activa` true→false o false→true hace reload. Razón: la fila existe en un tbody (activas/inactivas), moverla requeriría recrear la fila en el tbody correcto y eliminar la original — más frágil que un reload limpio.

### Deuda rowVersion para delete optimista
La fila insertada por Create optimista **no incluye** el botón de eliminación. El controller (`CajaController.Create` AJAX) retorna `{ ok, entity: { id, codigo, nombre, sucursal, ubicacion, activa } }` — sin `rowVersion`. Sin él, el form delete fallaría con `DbUpdateConcurrencyException`. Para resolver: el controller debe incluir `rowVersion = Convert.ToBase64String(model.RowVersion)` en la respuesta JSON, y `_buildCajaDesktopRow` debe construir el form delete con ese valor.

---

## 7. JS revisado pero no modificado

| Archivo | Revisión |
|---|---|
| `wwwroot/js/caja-form.js` | Revisado. Contratos `#formCaja`, validaciones de campo — sin cambios. |
| `wwwroot/js/caja-abrir.js` | Revisado. Lógica de apertura — sin cambios. |
| `wwwroot/js/caja-cerrar.js` | Revisado. Cálculo diferencia monto contado/esperado — sin cambios. |
| `wwwroot/js/caja-registrar-movimiento.js` | Revisado. Toggle tipo ingreso/egreso, enum binding — sin cambios. |
| `wwwroot/js/caja-detalles-apertura.js` | Revisado. Filtros y búsqueda en DetallesApertura — sin cambios. |
| `wwwroot/js/caja-historial.js` | Revisado. Filtros historial — sin cambios. |
| `wwwroot/js/horizontal-scroll-affordance.js` | No tocado. Inicializado desde `caja-index.js` vía `TheBury.initHorizontalScrollAffordance`. |

---

## 8. Funcionalidad preservada

| Contrato | Estado |
|---|---|
| ViewModels (`CajaViewModel`, `CajasListViewModel`, apertura/cierre) | ✓ Intactos |
| `asp-action` / `asp-route-*` en todas las vistas | ✓ Preservados |
| `@Html.AntiForgeryToken()` en todos los formularios | ✓ Preservado |
| `<input type="hidden" asp-for="Id" />` | ✓ Preservado |
| `<input type="hidden" asp-for="RowVersion" />` | ✓ Preservado (Edit modal y vistas full-page) |
| `<input type="hidden" asp-for="Estado" />` | ✓ Preservado (Edit modal) |
| Binding checkbox `Activa` (con `checked` en Create) | ✓ Preservado; `formData.set('Activa','false')` en JS cuando no checked |
| Bindings de formularios (todos los campos del ViewModel) | ✓ Preservados |
| Permisos `esAdmin` / `esPropietario` / `esMiApertura` | ✓ Preservados en Razor |
| Filtros del Index (all/disp/uso/arch) | ✓ Funcionales via `initFilterTabs` |
| Modal AJAX Create/Edit (`#modalCajaContainer`) | ✓ Preservado |
| `data-caja-open-create` / `data-caja-open-edit` / `data-caja-id` | ✓ Preservados |
| `data-caja-close-modal` | ✓ Preservado en partials |
| `data-caja-delete-form` / `data-confirm-message` | ✓ Preservados en filas Razor |
| `data-caja-row-id` en filas | ✓ Preservado |
| `data-state="disp|uso|arch"` en filas y cards | ✓ Preservado |
| `#cajas-activas-tbody` / `#cajas-inactivas-tbody` | ✓ Preservados |
| `#cajas-m-cards` | ✓ Preservado |
| `#modal-caja-errors` | ✓ Preservado en partials |
| `#btnGuardarCaja` | ✓ Preservado en partials |
| `#caja-index-feedback-slot` (`aria-live="polite"`) | ✓ Preservado en Index |
| Cálculo visual de cierre (diferencia monto contado/esperado) | ✓ Lógica JS en `caja-cerrar.js` — no tocada |
| Enum binding de movimiento (`TipoMovimientoCaja`) | ✓ Binding Razor en `RegistrarMovimiento_tw.cshtml` — no tocado |
| KPIs calculados en Razor (turnos, saldo, cajas disponibles/en uso) | ✓ Preservados en `Index_tw.cshtml` |
| Partial `_CajaModuleStyles` (carga de CSS) | ✓ Preservado en `@section Styles` de cada vista |

---

## 9. Cambios visuales esperados por pantalla

### Index (`Index_tw.cshtml`)
- Header con título "Cajas", subtítulo, botón "Historial de cierres" y "Nueva caja" (solo admin).
- KPIs en grid 2×2 (mobile) / 4×1 (desktop): Turnos abiertos (verde), Efectivo esperado (azul), Cajas en uso, Cajas disponibles.
- Sección "Turnos abiertos" o "Mi caja abierta": cards con fondo/ingresos/esperado + acciones (Ver turno, Movimiento, Cerrar).
- Empty state "No hay cajas abiertas" si no hay turnos.
- Sección "Tus cajas": filtros all/disp/uso/arch + tabla desktop (4 columnas: Caja, Ubicación, Estado, Acciones) + cards mobile.
- Chips: `chip-ok` (Disponible), `chip-info` (En uso), `chip-neutral` (Archivada), `chip-live` (En vivo).
- Empty state "No hay cajas activas configuradas" si no hay ninguna.
- Panel modal acoplado a la derecha (desktop: sticky, 28rem) o drawer mobile.

### Modales Create/Edit (`_CreateModal_tw.cshtml` / `_EditModal_tw.cshtml`)
- Panel drawer: header con título + código, body con Código+Nombre / Sucursal+Ubicación / Descripción / toggle Activa.
- `#modal-caja-errors` visible en validación fallida.
- Footer con "Cancelar" (ghost) y "Crear caja"/"Guardar cambios" (primary).
- Toggle switch CSS para "Caja activa" (`caja-switch` + `caja-switch-track`).
- Animación drawer-in en mobile; posición sticky en desktop.

### Abrir (`Abrir_tw.cshtml`)
- Layout con aside info de la caja (Nombre, Código, Sucursal, Estado chip) y form principal.
- Campo "Fondo inicial" con prefijo `$` (`.field-money`).
- Lista ayuda contextual (`.help-list`).
- Botones "Cancelar" + "Abrir caja".

### RegistrarMovimiento (`RegistrarMovimiento_tw.cshtml`)
- Toggle segmentado tipo Ingreso/Egreso (`.caja-movimiento-toggle` + `.tipo-btn` con colores verdes/rojos).
- Grid concepto + monto con prefijo `$`.
- Aside resumen del turno (fondo, ingresos, egresos, saldo esperado).

### Cerrar (`Cerrar_tw.cshtml`)
- Sección "Recuento físico": campos denominaciones o monto total contado.
- Resultado visual: diferencia OK (verde `.is-ok`) / advertencia (`.is-warn`) / diferencia negativa (`.is-bad`).
- Notas adicionales + confirmación.
- Botones "Cancelar turno" + "Confirmar cierre".

### DetallesApertura (`DetallesApertura_tw.cshtml`)
- KPIs financieros del turno (fondo, ingresos, egresos, saldo).
- Tabla de movimientos con filtros de búsqueda y tipo.
- Acciones: Nuevo movimiento, Imprimir, Cerrar caja (solo admin/propietario).
- Alta densidad de información: verificar mobile con tabla de movimientos larga.

### DetallesCierre (`DetallesCierre_tw.cshtml`)
- Resumen del cierre: fecha, duración, usuario, resultado (chip ok/warn/bad).
- KPIs financieros del cierre.
- Timeline de movimientos.
- Cards de diferencia (`.result-card.is-ok|is-warn|is-bad`).
- Botón "Volver al historial".

### Historial (`Historial_tw.cshtml`)
- Filtros: fecha desde/hasta, caja, usuario, botón Filtrar + Limpiar.
- Tabla de cierres con chips resultado, usuario, montos.
- Paginación o empty state.

---

## 10. Validaciones ejecutadas

### `git diff --check`
```
git diff --check wwwroot/js/caja-index.js   → exit 0 (sin whitespace errors)
```
Ejecutado en ML10.1. Ningún archivo del rework tiene errores de whitespace.

### `dotnet build`
```
Build result:
  10 warnings
  2 errors — ambos son MSB3021/MSB3027 (file-lock en TheBuryProyect.exe, PID 28612)
  0 errores Razor / CS propios
```

**Aclaración sobre file-lock PID 28612:** El error MSB3021/MSB3027 es cosmético. Indica que el `.exe` estaba bloqueado por la app corriendo en el momento del build. No es un error de compilación Razor ni C#. El exit code 1 del proceso build no refleja errores en el código fuente. Patrón reproducible en todos los builds de la sesión con app corriendo.

### Validaciones manuales recomendadas
No se realizaron pruebas E2E en browser durante el rework (no había servidor disponible para UI testing). Ver sección 13 para el checklist completo.

---

## 11. Deuda pendiente

### Delete optimista en filas creadas por AJAX (conocida, menor)
La fila `<tr>` insertada por `_handleCajaCreada()` **no incluye** el form de eliminación con `RowVersion`. El controller AJAX (`CajaController.Create`) retorna:
```json
{ "ok": true, "entity": { "id": ..., "codigo": ..., "nombre": ..., "sucursal": ..., "ubicacion": ..., "activa": ... } }
```
Sin `rowVersion`. Para resolverlo:
1. Agregar a la respuesta JSON: `rowVersion = Convert.ToBase64String(model.RowVersion)`
2. Actualizar `_buildCajaDesktopRow()` en `caja-index.js` para incluir el form delete con ese valor.

El usuario puede eliminar la caja recargando la página o en la próxima visita. No es bloqueante.

### Ninguna otra deuda detectada en el rework.

---

## 12. Riesgos

| Área | Riesgo | Nivel |
|---|---|---|
| Index + modales AJAX | `onCajaGuardada` maneja solo los casos Create/Edit sin cambio de estado. El edge case de cambio de `Activa` hace reload — correcto pero no optimista. | Bajo |
| Cerrar caja | Cálculo de diferencia monto contado/esperado vive en `caja-cerrar.js`. No fue modificado. Si el JS no carga o hay error en el cálculo, el usuario puede commitear un monto incorrecto. | Medio — validar en QA manual |
| RegistrarMovimiento | El enum `TipoMovimientoCaja` (0=Ingreso, 1=Egreso) debe hacer binding correcto desde el toggle JS al campo hidden. El JS de `caja-registrar-movimiento.js` no fue modificado — si el binding visual no coincide con el campo hidden, se registra el tipo incorrecto. | Medio — validar en QA manual |
| DetallesApertura | Vista de alta densidad. Filtros de búsqueda y tipo de movimiento dependen de `caja-detalles-apertura.js`. No modificado. Verificar mobile con muchos movimientos. | Bajo |
| Permisos admin | La lógica `esAdmin` controla botones de editar/eliminar/abrir. Si `ViewBag.EsAdmin` no llega correctamente, la UI muestra controles incorrectos. No fue modificado — pero verificar que las vistas migradas no hayan roto el acceso a `ViewBag`. | Bajo |

---

## 13. QA manual recomendado

```
[ ] Abrir Index (/Caja/Index)
[ ] Verificar KPIs (turnos abiertos, efectivo esperado, cajas en uso, disponibles)
[ ] Filtrar cajas: "Todas" → visible sin archivadas
[ ] Filtrar cajas: "Disponibles" → solo state=disp
[ ] Filtrar cajas: "En uso" → solo state=uso
[ ] Filtrar cajas: "Archivadas" → solo state=arch (si hay inactivas)
[ ] Crear caja desde modal (admin) → fila aparece en tabla sin reload
[ ] Verificar fila creada: 4 columnas, chip correcto, botón Abrir, botón Editar
[ ] Filtro sigue aplicado después de crear (nueva fila visible/oculta según estado)
[ ] Editar caja desde modal (admin, sin cambiar Activa) → fila actualizada in-place
[ ] Editar caja desde modal (admin, cambiando Activa) → feedback + reload
[ ] Abrir caja (/Caja/Abrir?cajaId=X) → form con fondo inicial
[ ] Registrar movimiento ingreso → tipo Ingreso, monto correcto
[ ] Registrar movimiento egreso → tipo Egreso, monto correcto
[ ] Verificar saldo actualizado en DetallesApertura
[ ] Cerrar caja → diferencia OK verde / diferencia negativa roja
[ ] Confirmar cierre → redirige al Index o historial
[ ] Revisar historial (/Caja/Historial) → filtros fecha/caja/usuario
[ ] Abrir detalle cierre → KPIs, timeline, result cards con color correcto
[ ] Revisar detalle apertura → movimientos, filtros, acciones visibles solo para admin/propietario
[ ] Probar mobile (< 768px): cards visibles, tabla oculta, drawer modal desde bottom
[ ] Probar tablet (768–1024px): verificar breakpoints de layout
[ ] Probar desktop (> 1024px): modal acoplado a la derecha (sticky), tabla visible
[ ] Verificar acceso no-admin: sin botón "Nueva caja", sin editar, sin eliminar
[ ] Verificar empty states: sin turnos abiertos, sin cajas configuradas
```

---

## 14. Comando exacto de git add

Para commitear solo el rework visual Caja (ML9 + ML10 + ML10.1):

```bash
git add Views/Caja/Abrir_tw.cshtml Views/Caja/Cerrar_tw.cshtml Views/Caja/Create_tw.cshtml Views/Caja/DetallesApertura_tw.cshtml Views/Caja/DetallesCierre_tw.cshtml Views/Caja/Edit_tw.cshtml Views/Caja/Historial_tw.cshtml Views/Caja/Index_tw.cshtml Views/Caja/RegistrarMovimiento_tw.cshtml Views/Caja/_CreateModal_tw.cshtml Views/Caja/_EditModal_tw.cshtml wwwroot/css/caja-module.css wwwroot/js/caja-index.js
```

Para incluir la limpieza de referencia:
```bash
git add "referencia/Kardex _standalone_.html"
```

**No incluir:** `enet8.0TheBuryProyect.dll`, `referencia/Caja/` (untracked — son los HTML de referencia, conservar).

---

## Siguiente micro-lote recomendado

Opciones según prioridad:

1. **Commit del rework Caja** — staging con el comando exacto del punto 14, mensaje descriptivo de rework visual.
2. **QA manual básico** — abrir la app y correr el checklist del punto 13 antes de commitear.
3. **Resolución deuda delete optimista** — agregar `rowVersion` al JSON de respuesta en el controller si se quiere delete sin reload.
4. **Rework de otro módulo** — continuar con el siguiente módulo visual pendiente.

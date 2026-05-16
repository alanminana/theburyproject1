# Fase 10.18 — Diagnóstico y normalización de badges en DocumentoCliente

## A. Objetivo

Revisar `Views/DocumentoCliente/Index_tw.cshtml` y normalizar visualmente el estado de documentos de cliente.

El hallazgo original (Fase 10.17) indicaba que `@doc.EstadoNombre` se renderizaba como texto plano cerca de línea 255.

---

## B. Diagnóstico previo

### Vistas existentes en `Views/DocumentoCliente/`

- `Index_tw.cshtml` — listado principal con filtros, tabla y modal de subida
- `Details_tw.cshtml` — detalle individual
- `Upload_tw.cshtml` — flujo de subida (probablemente legacy o alternativo al modal)
- `_DocumentoClienteModuleStyles.cshtml` — estilos del módulo

### Hallazgo principal

El badge **ya estaba implementado** al momento de esta fase. El hallazgo de "texto plano" de 10.17 ya había sido resuelto (posiblemente dentro del desarrollo del propio módulo). La vista usaba un switch de `estadoClasses` + `estadoIcon` con icono Material Symbols.

**Estado encontrado (antes de este PR):**

```csharp
var estadoClasses = doc.Estado switch
{
    EstadoDocumento.Verificado => "bg-emerald-500/20 text-emerald-400",
    EstadoDocumento.Rechazado  => "bg-red-500/20 text-red-400",
    EstadoDocumento.Vencido    => "bg-orange-500/20 text-orange-400",
    _                          => "bg-amber-500/20 text-amber-400"
};
```

Sin clase `border` — incompleto respecto al patrón canónico del proyecto.

**Segundo problema detectado — línea 228:**

```html
<tr class="transition-colors hover:/50 hover:bg-slate-800/30">
```

`hover:/50` es una clase Tailwind inválida (token roto, sin prefijo de color). Genera ruido en el CSS sin efecto real.

---

## C. Vista revisada

`Views/DocumentoCliente/Index_tw.cshtml`

---

## D. Estados / valores encontrados

Enum `EstadoDocumento` (canónico, `Models/Enums/EstadoDocumento.cs`):

| Valor | Int | Descripción |
|---|---|---|
| Pendiente | 1 | Subido, esperando revisión |
| Verificado | 2 | Revisado y aprobado |
| Rechazado | 3 | Revisado y rechazado |
| Vencido | 4 | Documento expirado |

`EstadoNombre` es una propiedad calculada en `DocumentoClienteViewModel` que devuelve string según el enum. No es texto libre.

---

## E. Cambios aplicados

### Cambio 1 — Normalización de `estadoClasses` (líneas 212–218)

Agregado `border border-{color}-500/30` a cada rama del switch para alinear con el patrón canónico del proyecto (confirmado en Catálogo 10.17: `bg-red-500/20 text-red-400 border border-red-500/30`).

**Antes:**
```csharp
EstadoDocumento.Verificado => "bg-emerald-500/20 text-emerald-400",
EstadoDocumento.Rechazado  => "bg-red-500/20 text-red-400",
EstadoDocumento.Vencido    => "bg-orange-500/20 text-orange-400",
_                          => "bg-amber-500/20 text-amber-400"
```

**Después:**
```csharp
EstadoDocumento.Verificado => "border border-emerald-500/30 bg-emerald-500/20 text-emerald-400",
EstadoDocumento.Rechazado  => "border border-red-500/30 bg-red-500/20 text-red-400",
EstadoDocumento.Vencido    => "border border-orange-500/30 bg-orange-500/20 text-orange-400",
_                          => "border border-amber-500/30 bg-amber-500/20 text-amber-400"
```

### Cambio 2 — Eliminar clase Tailwind inválida en `<tr>` (línea 228)

**Antes:** `<tr class="transition-colors hover:/50 hover:bg-slate-800/30">`  
**Después:** `<tr class="transition-colors hover:bg-slate-800/30">`

Efecto: sin cambio visual perceptible (la clase era inválida y no generaba ningún estilo).

---

## F. Helpers / partials reutilizados o creados

- No se creó partial global — el badge se resuelve inline con switch local. No hay duplicación real en otras vistas que justifique un partial específico para `EstadoDocumento`.
- `EstadoColor` y `EstadoIcono` del ViewModel usan íconos Bootstrap (`bi-*`) — **legacy** respecto al módulo actual que ya usa Material Symbols. No se tocaron porque están fuera del alcance de esta fase.

---

## G. Tests / validaciones

| Verificación | Resultado |
|---|---|
| `dotnet build --configuration Release` | 0 errores, 0 warnings |
| `dotnet test --filter "DocumentoCliente|Cliente|Documento"` | 266/266 passing |
| `git diff --check` | limpio |

---

## H. Qué NO se tocó

- `Controllers/DocumentoClienteController.cs`
- `Models/Entities/DocumentoCliente.cs`
- `ViewModels/DocumentoClienteViewModel.cs`
- `Models/Enums/EstadoDocumento.cs`
- Acciones operativas (Verificar, Delete, Descargar, Upload)
- Modal de subida
- Filtros de la tabla
- Paginación
- Scripts JS (`documento-module.js`, `documento-index.js`, `documento-upload-modal.js`)
- Otros módulos fuera de alcance

---

## I. Riesgos / deuda

- `EstadoColor` y `EstadoIcono` en `DocumentoClienteViewModel` usan Bootstrap Icons (`bi-*`) — propiedades legacy que ya no coinciden con el módulo actual (Material Symbols). No generan error visible porque `Index_tw.cshtml` no las usa; pero si alguien las reutiliza en otra vista, obtendrá clases de Bootstrap sin el icon font correspondiente.
- `Upload_tw.cshtml` no fue revisada — podría tener renderizado de estado pendiente de normalizar en una fase posterior.

---

## J. Checklist actualizado

- [x] Diagnóstico de `Index_tw.cshtml`
- [x] Identificación de bug de clase Tailwind inválida (`hover:/50`)
- [x] Normalización de `estadoClasses` con `border` canónico
- [x] Eliminación de `hover:/50`
- [x] Build Release: 0 errores
- [x] Tests: 266/266 passing
- [x] `git diff --check`: limpio
- [x] Documentación creada
- [ ] Revisar `Upload_tw.cshtml` si tiene renderizado de estado (tarea futura)
- [ ] Evaluar limpieza de `EstadoColor` / `EstadoIcono` legacy en ViewModel (tarea futura)

# Fase 10.15 — Diagnóstico y normalización de badges en Ticket y OrdenCompra

## A. Objetivo

Revisar si `Views/Ticket/` y `Views/OrdenCompra/` tienen badges visuales divergentes o texto plano para estados operativos. Normalizar donde corresponda.

## B. Diagnóstico previo

### Ticket

- `Views/Ticket/Index_tw.cshtml` — **CANÓNICO**. Usa `TicketUiHelper.EstadoBadgeClass()` centralizado.
- `Views/Ticket/Details_tw.cshtml` — **CANÓNICO**. Usa `TicketUiHelper.EstadoBadgeClass()` centralizado.
- `Helpers/TicketUiHelper.cs` — **CANÓNICO**. Patrón: `badge-erp border border-{color}-500/20 bg-{color}-500/10 text-{color}-300`.
- **Sin divergencias. Sin cambios.**

### OrdenCompra

Cuatro vistas con badge inline divergente. No existía helper centralizado.

| Vista | Patrón encontrado | Problemas |
|---|---|---|
| `Index_tw.cshtml` | `bg-{color}-900/30 text-{color}-400`, sin border | Legacy. Borrador con clases conflictivas (bg y text light+dark mezclados). |
| `Details_tw.cshtml` | `bg-{color}-500/20 text-{color}-400 border-{color}-500/30` | Opacidad `/20` en lugar de `/10`. Color `text-{color}-400` en lugar de `text-{color}-300`. |
| `Recepcionar_tw.cshtml` | `bg-{color}-500/10 text-{color}-500 border-{color}-500/20` | Color `text-{color}-500` en lugar de `text-{color}-300`. Solo mapeaba 2 de 6 estados (mapping incompleto). |
| `Delete_tw.cshtml` | `bg-{color}-900/30 text-{color}-400 border-{color}-900/40` | Legacy. Borrador con clases conflictivas. Solo mapeaba 4 de 6 estados (faltaban EnTransito y Recibida). |

## C. Vistas revisadas

- `Views/Ticket/Index_tw.cshtml` — solo lectura, sin cambios
- `Views/Ticket/Details_tw.cshtml` — solo lectura, sin cambios
- `Views/OrdenCompra/Index_tw.cshtml` — normalizado
- `Views/OrdenCompra/Details_tw.cshtml` — normalizado
- `Views/OrdenCompra/Recepcionar_tw.cshtml` — normalizado
- `Views/OrdenCompra/Delete_tw.cshtml` — normalizado
- `Views/OrdenCompra/Create_tw.cshtml` — sin badges de estado (formulario de creación, sin cambios)

## D. Enums encontrados

### EstadoTicket (Models/Enums/EstadoTicket.cs)
- `Pendiente = 0`
- `EnCurso = 1`
- `Resuelto = 2`
- `Cancelado = 3`
- Ya normalizado desde fases previas. Sin cambios.

### TipoTicket (Models/Enums/TipoTicket.cs)
- Usado como badge en Ticket. Ya normalizado. Sin cambios.

### EstadoOrdenCompra (Models/Enums/EstadoOrdenCompra.cs)
- `Borrador = 0`
- `Enviada = 1`
- `Confirmada = 2`
- `EnTransito = 3`
- `Recibida = 4`
- `Cancelada = 5`
- Normalizado en esta fase.

## E. Cambios aplicados

### Nuevo: `Helpers/OrdenCompraUiHelper.cs`

Helper centralizado análogo a `TicketUiHelper.cs`. Expone:

- `EstadoBadgeClass(EstadoOrdenCompra)` → clase CSS canónica completa
- `EstadoNombre(EstadoOrdenCompra)` → nombre display del estado

Patrón canónico aplicado (igual que Ticket):
```
badge-erp border border-{color}-500/20 bg-{color}-500/10 text-{color}-300
```

Mapeado de colores:
- Borrador → slate
- Enviada → blue
- Confirmada → green
- EnTransito → amber
- Recibida → emerald
- Cancelada → rose

### Vistas OrdenCompra actualizadas

Todas reemplazan su inline switch por llamadas al helper. El wrapper de badge simplificado a `<span class="@estadoBadgeClass">@estadoNombre</span>`. En `Recepcionar_tw.cshtml`, el badge con dot animado (EnTransito) conserva el elemento `<span>` interno con `animate-pulse`.

## F. Tests / validaciones

```
dotnet build --configuration Release  → 0 errores, 0 advertencias
dotnet test --filter "OrdenCompra|Proveedor"  → 72/72 passed
git diff --check  → sin whitespace errors
```

No existen tests específicos de badge rendering. Los 72 tests cubren lógica de servicios, validaciones y controladores relacionados.

## G. Qué NO se tocó

- `Controllers/TicketController.cs` — sin cambios
- `Controllers/TicketApiController.cs` — sin cambios
- `Controllers/OrdenCompraController.cs` — sin cambios
- `Services/*` — sin cambios
- `Models/Entities/*` — sin cambios
- `Models/Enums/*` — sin cambios (solo lectura)
- CSS del módulo — sin cambios
- JS — sin cambios
- Vistas de Venta, Caja, Factura, Devolución, Cotización — sin cambios
- Módulo de Carlos — sin cambios

## H. Riesgos / deuda remanente

- Ningún riesgo de regresión identificado. Cambios son de renderizado puro.
- `EstadoOrdenCompra` no tiene atributos `[Display]`. `EstadoNombre()` en el helper centraliza los nombres display. Si en el futuro se agregan atributos `[Display]` al enum, revisar el helper para reutilizarlos.
- El badge animado en `Recepcionar_tw.cshtml` (dot de EnTransito) mantiene su funcionalidad. El tamaño de padding del `badge-erp` es levemente menor que el wrapper manual anterior. Diferencia visual mínima, coherente con el sistema.

## I. Checklist actualizado

### Completado en esta fase
- [x] Diagnóstico de Views/Ticket — sin divergencias
- [x] Diagnóstico de Views/OrdenCompra — 4 vistas con divergencias
- [x] `Helpers/OrdenCompraUiHelper.cs` creado
- [x] `Views/OrdenCompra/Index_tw.cshtml` normalizado
- [x] `Views/OrdenCompra/Details_tw.cshtml` normalizado
- [x] `Views/OrdenCompra/Recepcionar_tw.cshtml` normalizado
- [x] `Views/OrdenCompra/Delete_tw.cshtml` normalizado
- [x] Build limpio (0 errores, 0 warnings)
- [x] 72 tests passing
- [x] Commit y push

### Pendiente (fuera de alcance de esta fase)
- [ ] Fase 10.16: revisar módulos pendientes (Compra, Producto, Proveedor, etc.)

# Fase: Polish visual — MovimientoStock / Kardex / Modal de movimientos

**Rama:** `juan/polish-movimientostock-kardex`
**Agente:** Juan
**Fecha:** 2026-05-16

---

## A. Objetivo

Revisar y normalizar visualmente el módulo MovimientoStock / Kardex / modales de movimientos.
Cerrar deuda visual sin cambiar reglas de negocio, cálculos de stock, ni lógica de servicios.

---

## B. Diagnóstico previo

### Vistas existentes

| Vista | Estado |
|---|---|
| `Views/MovimientoStock/Index_tw.cshtml` | canónico — historial global con filtros y stats |
| `Views/MovimientoStock/Kardex_tw.cshtml` | canónico — kardex por producto, motivo, signo, stats |
| `Views/MovimientoStock/Create_tw.cshtml` | canónico — formulario de ajuste, sin issues |

### Modal

- Modal global en `Views/Catalogo/Index_tw.cshtml` (`#modal-movimientos`)
- Mismo modal filtra por producto al abrirse desde fila de catálogo (`data-movimientos-producto-id`)
- JS: `wwwroot/js/movimientos-inventario-modal.js`

### Issues detectados

| # | Archivo | Issue |
|---|---|---|
| 1 | `Index_tw.cshtml` línea 208 | `Cantidad` sin `Math.Abs` para Ajuste → posible `--3` en display |
| 2 | `Catalogo/Index_tw.cshtml` línea 2172 | Botón modal header con clases duplicadas `bg-slate-200 bg-slate-800` |
| 3 | `movimientos-inventario-modal.js` `tipoBadge()` | Badges con clases light/dark mixtas; sin `border` → inconsistente con Razor views |
| 4 | `Kardex_tw.cshtml` | Sin banner para producto `Activo=false` ni placeholder para `ViewBag.ProductoEliminado` |

### Signo y color: verificación

- `Entrada`: `+` / `text-emerald-400` ✅
- `Salida`: `-` / `text-rose-400` ✅
- `Ajuste positivo`: `+` / `text-emerald-400` ✅
- `Ajuste negativo`: `-` / `text-rose-400` — **afectado por issue #1 en Index**
- `Ajuste neutro`: `text-slate-400` ✅

### Motivo y Referencia

- Kardex: columna Motivo dedicada ✅
- Index: no tiene columna Motivo (referencia sí) — aceptable para listado global
- Modal JS: Motivo aparece como sub-texto bajo Referencia ✅

---

## C. Clasificación de componentes

| Componente | Clasificación | Decisión |
|---|---|---|
| `Index_tw.cshtml` | canónico | modificado — Math.Abs |
| `Kardex_tw.cshtml` | canónico | modificado — banner inactivo/eliminado |
| Modal global (Catálogo) | canónico | modificado — button clases duplicadas |
| `movimientos-inventario-modal.js` | canónico | modificado — badges normalizados |
| `Create_tw.cshtml` | canónico | no tocado |
| `MovimientoStockController` | canónico | solo lectura |
| `MovimientoStockService` | canónico | solo lectura |

---

## D. Cambios aplicados

### 1. `Views/MovimientoStock/Index_tw.cshtml`

**Qué**: Celda Cantidad en tabla — `m.Cantidad.ToString("N0")` → `Math.Abs(m.Cantidad).ToString("N0")`

**Por qué**: Para Ajuste, `Cantidad` en DB es `stockNuevo - stockAnterior` (puede ser negativo). El signo visual ya lo provee la función `Signo()`. Sin `Math.Abs`, un ajuste de -3 mostraba `--3`.

**Kardex_tw ya usaba `Math.Abs` correctamente** — esta corrección alinea Index al mismo patrón.

### 2. `Views/Catalogo/Index_tw.cshtml`

**Qué**: Botón "Volver al Inventario" del modal — eliminadas clases duplicadas `bg-slate-200 hover:bg-slate-300`.

**Por qué**: El sitio es dark-only. Las clases light no aplican y generaban conflicto visual (el botón podía renderizar gris claro en algunos contextos).

Resultado: `bg-slate-800 hover:bg-slate-700` — consistente con el resto del sistema.

### 3. `wwwroot/js/movimientos-inventario-modal.js`

**Qué**: Función `tipoBadge()` normalizada de clases light/dark mixtas a dark-only con border.

**Antes:**
```js
'bg-emerald-100 dark:bg-emerald-500/20 text-emerald-700 dark:text-emerald-400'
```

**Después:**
```js
'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
```

**Por qué**: Las vistas Razor usan dark-only. El JS usaba clases con prefijo `dark:` lo que generaba inconsistencia visual y faltaba el `border` que tienen todos los badges Razor del proyecto.

### 4. `Views/MovimientoStock/Kardex_tw.cshtml`

**Qué**: Dos banners condicionales antes del banner de SKU:

1. `ViewBag.ProductoEliminado == true` → banner slate "Producto eliminado — historial solo lectura"
2. `producto.Activo == false` → banner slate "Producto inactivo — historial solo lectura"

**Por qué**:
- El banner de `ViewBag.ProductoEliminado` es placeholder para cuando el controller implemente `GetByIdParaHistorialAsync` (ver deuda pendiente).
- El banner de `Activo == false` funciona ya que `GetByIdAsync` no filtra por `Activo`, solo por `IsDeleted`. Un producto inactivo pero no eliminado sí llega al Kardex y merece contexto visual.

---

## E. Producto eliminado en Kardex — deuda documentada

**Situación actual:**

- `IProductoService.GetByIdAsync` filtra `!p.IsDeleted`.
- Si el producto está soft-deleted, Kardex redirige con "Producto no encontrado".
- Los movimientos históricos existen en DB pero no son accesibles desde Kardex.

**Impacto:** Medio — solo afecta productos eliminados con historial.

**Riesgo si se implementa:** Bajo — requiere nuevo método en `IProductoService` + cambio en controller.

**Recomendación futura:**
```csharp
// IProductoService — agregar:
Task<Producto?> GetByIdParaHistorialAsync(int id); // include IsDeleted=true

// MovimientoStockController.Kardex:
var producto = await _productoService.GetByIdAsync(id)
    ?? await _productoService.GetByIdParaHistorialAsync(id);
ViewBag.ProductoEliminado = (producto != null && producto.IsDeleted);
```

El banner en `Kardex_tw.cshtml` ya espera `ViewBag.ProductoEliminado == true` para renderizar.

---

## F. Tests / validaciones

- `dotnet build --configuration Release` ✅ 0 errores, 0 advertencias
- Tests ejecutados con filtro `MovimientoStock|Producto|Catalogo|Inventario`
- Cambios visuales puros en Razor y JS — no afectan lógica de negocio ni contratos de datos

---

## G. Qué NO se tocó

- `MovimientoStockService` — sin cambios
- `ProductoService` — sin cambios
- `Create_tw.cshtml` — sin cambios
- `MovimientoStockController` — sin cambios
- Entidades, migraciones, VentaService, CajaService, DevolucionService
- Módulos de Carlos (Cotización) y Kira (TestHost/Ventas)
- Reglas de cálculo de stock, signos funcionales, conciliación, generación de movimientos

---

## H. Riesgos / deuda remanente

| Item | Severidad | Descripción |
|---|---|---|
| ProductoEliminado en Kardex | Media | Historial no accesible para soft-deleted. Requiere nuevo método en service + cambio controller |
| Dropdown Index solo activos | Baja | `GetAllAsync` en Index puede incluir inactivos; no es bloqueante para cierre |
| Modal — stats sin color | Baja | Stats del modal (`mov-stat-entradas`, etc.) son todos `text-white`; Kardex usa emerald/rose/amber. Inconsistencia menor, no bloqueante |

---

## I. Checklist actualizado

### Juan
- [x] DocumentoCliente agrupado por cliente + modal — cerrado
- [x] Producto eliminado preserva movimientos históricos — cerrado
- [x] Diagnóstico StockActual vs unidades físicas — cerrado
- [x] MovimientoStock / Kardex visual polish — **cerrado esta fase**

### Carlos
- [ ] V1.9+ Cotización — en curso en rama `carlos/cotizacion-v1-contratos`

### Kira
- [ ] Ventas/Create complementario — en curso en ramas separadas

---

## J. Confirmación de cierre

**Juan no agrega nuevos micro-lotes.**
Esta fase cierra la deuda visual de MovimientoStock / Kardex / modal de movimientos.
El siguiente frente debe definirse en una nueva sesión con contexto actualizado.

# Diagnóstico: StockActual vs unidades físicas en productos trazables

**Agente:** Juan  
**Fecha:** 2026-05-16  
**Rama:** juan/diagnostico-stockactual-unidades  

---

## A. Objetivo

Diagnosticar el comportamiento del sistema cuando:
- Un producto trazable tiene `Producto.RequiereNumeroSerie = true`
- `Producto.StockActual` es mayor que la cantidad de `ProductoUnidades` en estado `EnStock`
- El vendedor intenta vender más unidades de las que existen físicamente

Determinar si hay bug y definir el comportamiento correcto.

---

## B. Caso analizado

```
Producto: Televisor Samsung
RequiereNumeroSerie = true
StockActual = 5
ProductoUnidades creadas = 4 (todas EnStock)
Diferencia = +1
```

Escenarios probados:
1. Vender 1 unidad seleccionando ProductoUnidad válida
2. Vender 1 unidad sin ProductoUnidadId
3. Vender con Cantidad=2 en una línea trazable
4. Intentar vender más unidades que las físicas disponibles

---

## C. Diagnóstico actual

### Arquitectura del sistema

El sistema tiene dos conceptos de stock separados con roles distintos:

| Concepto | Entidad | Fuente | Rol |
|---|---|---|---|
| Stock agregado | `Producto.StockActual` | `MovimientoStock` | Indicador numérico, histórico, usado en reportes y check de stock no trazable |
| Unidades físicas | `ProductoUnidad` | `ProductoUnidadService` | Trazabilidad individual, estado (`EnStock`, `Vendida`, etc.), identificador real para venta trazable |

### Flujo de validación por operación

**CreateAsync (guardar como Presupuesto/Cotización):**
- **No valida trazabilidad.** Intencional: permite guardar sin selección completa.
- La venta queda en estado `Presupuesto` con `ProductoUnidadId` opcional.

**UpdateAsync (editar):**
- Llama `ValidarTrazabilidadDetallesVMAsync` → gate completo de trazabilidad.
- Bloquea: sin unidad, unidad incorrecta, unidad no EnStock, Cantidad>1, duplicados.

**ConfirmarVentaAsync (confirmar):**
1. `ValidarEstadoParaConfirmacion` → estado correcto
2. `ValidarStock(venta)` → `StockActual >= Cantidad` por línea (check agregado)
3. `ValidarUnidadesTrazablesAsync(venta)` → gate real de trazabilidad:
   - Requiere `ProductoUnidadId` para productos trazables
   - Unidad debe existir, no estar eliminada
   - Unidad debe pertenecer al producto
   - Unidad debe estar en `EstadoUnidad.EnStock`
   - Cantidad debe ser 1
   - Sin duplicados entre líneas
4. `DescontarStockYRegistrarMovimientos` → descuenta `StockActual`
5. `MarcarUnidadesVendidasAsync` → marca unidades como `Vendida`

**Frontend (venta-create.js):**
- `cargarUnidadesDisponibles`: llama `/api/productos/{id}/unidades-disponibles`
- Este endpoint llama `ObtenerDisponiblesPorProductoAsync` → solo devuelve `EnStock`
- Si StockActual=5 y EnStock=4 → selector muestra 4 opciones, no 5
- Guard pre-submit (línea 2038): trazable sin unidad → bloqueado
- Guard pre-submit (línea 2046): trazable con Cantidad≠1 → bloqueado

### Escenario StockActual=5, EnStock=4

| Acción del vendedor | ValidarStock | ValidarUnidadesTrazablesAsync | Resultado |
|---|---|---|---|
| Vender 1 con unidad física válida | PASA (5≥1) | PASA | ✅ Confirmada |
| Vender 1 sin ProductoUnidadId | PASA (5≥1) | BLOQUEA (falta unidad) | ✅ Error correcto |
| Vender con Cantidad=2, unidad válida | PASA (5≥2) | BLOQUEA (cantidad≠1) | ✅ Error correcto |
| Vender 5 líneas (solo 4 unidades reales) | PASA | BLOQUEA (5ta unidad no existe EnStock) | ✅ Error correcto |

**Conclusión: No hay bug.** El sistema previene correctamente todas las formas de venta inconsistente. `ValidarUnidadesTrazablesAsync` es el gate definitivo y verifica contra el estado real de BD, no contra `StockActual`.

### Observación sobre ValidarStock para trazables

`ValidarStock` compara `StockActual < Cantidad` por línea. Para productos trazables con Cantidad=1 por línea, este check siempre pasa si StockActual≥1. Esto es correcto porque:
- No es el gate real para trazables
- `ValidarUnidadesTrazablesAsync` es el gate definitivo
- Eliminar el check para trazables no agrega protección adicional

### Conciliación existente

`ProductoController.AjustarConciliacion` + `MovimientoStockService.RegistrarAjusteAsync` permite igualar `StockActual` a `UnidadesEnStock` mediante un ajuste de stock. La diferencia se detecta en `ObtenerConciliacionPorProductoAsync` → `DiferenciaStockVsUnidadesEnStock`. La vista `Unidades.cshtml` la muestra con opción de ajuste.

---

## D. Comportamiento esperado (confirmado correcto)

Para productos con `RequiereNumeroSerie = true`:
- ✅ Venta con `ProductoUnidadId` válido (`EnStock`, del producto) → **permitida**
- ✅ Venta sin `ProductoUnidadId` → **bloqueada** (incluso si StockActual > 0)
- ✅ Venta con Cantidad > 1 → **bloqueada**
- ✅ Venta con unidad no `EnStock` → **bloqueada**
- ✅ Venta con unidad de otro producto → **bloqueada**
- ✅ Venta con unidad duplicada en otra línea → **bloqueada**
- ✅ `StockActual > EnStock` no habilita ventas sin unidad física
- ✅ La diferencia puede resolverse por conciliación (`ProductoController.AjustarConciliacion`)

Para productos con `RequiereNumeroSerie = false`:
- ✅ Venta por stock agregado sin `ProductoUnidadId` → **permitida normalmente**

---

## E. Cambios aplicados

No se modificó código de negocio. El sistema ya era correcto.

Se agregaron 2 tests de regresión al archivo existente:
- `VentaServiceProductoUnidadTrazabilidadTests.cs` (Tests 42 y 43)

---

## F. Tests agregados

### Test 42: `ConfirmarVenta_ProductoTrazable_StockMayorAUnidades_PermiteVenderUnidadSeleccionada`
- Setup: StockActual=5, 4 ProductoUnidades EnStock
- Acción: ConfirmarVenta con 1 unidad física válida seleccionada
- Verificación: venta confirmada, unidad marcada Vendida
- Documenta: la diferencia StockActual vs EnStock no impide vender una unidad física válida

### Test 43: `ConfirmarVenta_ProductoTrazable_StockAltoSinUnidadFisica_BloqueaVenta`
- Setup: StockActual=5, 0 ProductoUnidades creadas
- Acción: ConfirmarVenta sin ProductoUnidadId
- Verificación: `InvalidOperationException` con mensaje "requiere selección de unidad individual"
- Documenta: StockActual alto no es sustituto de unidad física para productos trazables

### Cobertura preexistente (confirmada vigente)

| Escenario requerido | Test existente |
|---|---|
| Sin ProductoUnidadId → bloqueado | Test 2 (`ConfirmarVenta_ProductoTrazable_SinUnidadId_LanzaExcepcion`) |
| Cantidad > 1 → bloqueado | Test 5 (`ConfirmarVenta_ProductoTrazable_CantidadMayorUno_LanzaExcepcion`) |
| Unidad no EnStock → bloqueado | Test 4 (`ConfirmarVenta_ProductoTrazable_UnidadNoEnStock_LanzaExcepcion`) |
| Unidad de otro producto → bloqueado | Test 3 (`ConfirmarVenta_ProductoTrazable_UnidadDeOtroProducto_LanzaExcepcion`) |
| Producto no trazable sigue por stock | Test 30 (`CancelarVenta_Confirmada_SinUnidades_FuncionaSinError`) |
| Conciliación detecta diferencia | `ConciliacionStockUnidadesTests.DiferenciaPositiva_*` (múltiples) |

---

## G. Validaciones técnicas

```
dotnet build --configuration Release → 0 errores, 0 advertencias
dotnet test --filter "VentaServiceProductoUnidadTrazabilidadTests" → 42/42 ✅
dotnet test --filter "Venta|ProductoUnidad|MovimientoStock|Producto|Conciliacion" → 1219/1219 ✅
git diff --check → sin errores de whitespace
```

---

## H. Qué NO se tocó

- `VentaService` — sin cambios (era correcto)
- `VentaValidator` — sin cambios (ValidarStock es intencional para no-trazables)
- `ProductoUnidadService` — sin cambios
- `venta-create.js` — sin cambios
- `Views/Venta/Create_tw.cshtml` — sin cambios
- `CajaService` — fuera de alcance
- `DevolucionService` — fuera de alcance
- `Cotización` — fuera de alcance (rama Carlos)
- Migraciones — sin cambios

---

## I. Riesgos / Deuda remanente

| Item | Tipo | Riesgo | Recomendación |
|---|---|---|---|
| UI no distingue StockActual de UnidadesEnStock en carrito | Deuda UX | Bajo | Mostrar ambos valores cuando hay diferencia; opcional |
| ValidarStock incluye productos trazables en check agregado | Deuda técnica menor | Muy bajo | No es bug; podría optimizarse para skip trazables, pero no es necesario |
| CreateAsync no valida trazabilidad | Diseño intencional | Bajo | Permite guardado parcial; Confirmar es el gate definitivo |

---

## J. Checklist actualizado

- [x] Diagnóstico StockActual vs unidades físicas — **CERRADO, sin bug**
- [x] Tests 42 y 43 agregados a `VentaServiceProductoUnidadTrazabilidadTests.cs`
- [x] 1219/1219 tests pasan
- [x] Build limpio
- [x] Documentación creada
- [ ] UI MovimientoStock/Kardex visual — pendiente opcional (Juan)

---

## K. Coordinación Carlos / Kira

- Carlos: módulo Cotización en `E:\theburyproject-carlos-cotizacion` — no tocado
- Kira: módulo Ventas/TestHost — no tocado
- No se modificó `Program.cs`, `TestHost`, ni ningún módulo de Cotización o Factura

---

## L. Siguiente micro-lote recomendado

Si se desea mejorar la UX: agregar en el carrito de venta un indicador visual cuando `StockActual != UnidadesEnStock` para productos trazables. Bajo riesgo, sin impacto en backend. No es obligatorio dado que el backend ya protege correctamente.

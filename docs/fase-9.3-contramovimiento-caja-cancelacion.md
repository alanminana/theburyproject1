# Fase 9.3 — Contramovimiento de Caja al cancelar venta confirmada/facturada

**Fecha:** 2026-05-15  
**Tipo:** Implementación — cambio productivo  
**Estado:** Cerrado  
**Depende de:** Fase 9.1 (diagnóstico), Fase 9.2 (anulación de factura)

---

## A. Contexto

El diagnóstico de Fase 9.1 identificó el hallazgo **H-A2**: al cancelar una venta `Confirmada` o `Facturada`, el `MovimientoCaja` original (ingreso de la venta) quedaba activo sin reversión alguna. Esto generaba saldo de caja inflado y arqueos incorrectos.

La Fase 9.2 resolvió la anulación de factura al cancelar. Esta fase resuelve la reversión del movimiento de caja.

### Estado previo al inicio de la fase

| Comportamiento | Estado |
|----------------|--------|
| `CancelarVentaAsync` revierte stock y unidades | Correcto ✓ |
| `CancelarVentaAsync` anula factura (Fase 9.2) | Correcto ✓ |
| `CancelarVentaAsync` revierte MovimientoCaja | **No** ✗ |
| `ICajaService` expone método de reversión de venta | **No** ✗ |
| `ConceptoMovimientoCaja` tiene valor `ReversionVenta` | **No** ✗ |

---

## B. Decisión de diseño: Opción D — Contramovimientos

**Estrategia elegida**: crear un nuevo `MovimientoCaja` de tipo `Egreso` con concepto `ReversionVenta`, vinculado al mismo `VentaId`, dentro de la misma transacción de cancelación.

**Por qué esta opción y no las alternativas:**

| Opción | Descripción | Decisión |
|--------|-------------|----------|
| A — Eliminar ingreso original | Pierde trazabilidad histórica | Descartada |
| B — Campo `Estado` en MovimientoCaja | Cambio de esquema + migración | Descartada (demasiado amplio para V1) |
| C — Campo `MovimientoOrigenId` | Cambio de esquema + migración | Descartada |
| **D — Contramovimiento Egreso** | Solo nuevo enum value + nuevo método | **Elegida** |

**Ventajas del patrón contramovimiento:**
1. No requiere cambio de esquema en `MovimientoCaja`
2. `CalcularSaldoActualAsync` ya suma Ingresos y resta Egresos → el contramovimiento funciona automáticamente
3. Trazabilidad completa: ingreso original + egreso de reversión quedan en historial
4. El motivo de cancelación queda registrado en `Observaciones` del contramovimiento
5. Guard anti-duplicación sin campo adicional: solo buscar Egreso ReversionVenta para el mismo VentaId

---

## C. Regla de negocio implementada

**Cuándo se crea contramovimiento de caja:**
- `CancelarVentaAsync` fue invocado
- `estadoOriginal == EstadoVenta.Confirmada || estadoOriginal == EstadoVenta.Facturada`
- Existe un `MovimientoCaja` de tipo `Ingreso` asociado al `VentaId` en la base de datos

**Cuándo NO se crea contramovimiento:**
- Estado original era `Cotizacion`, `Presupuesto`, `PendienteRequisitos`, `PendienteFinanciacion`
- La venta tenía `TipoPago.CreditoPersonal` (no genera ingreso inmediato — no hay movimiento que revertir)
- La venta tenía `TipoPago.CuentaCorriente` (no genera ingreso inmediato)
- Ya existía un contramovimiento `ReversionVenta` para el mismo `VentaId` (guard anti-duplicación)

**Degradación graceful:**
- Si el `MovimientoCaja` original no se encuentra (venta sin caja registrada), se loguea warning y la cancelación continúa sin error
- Si la `AperturaCaja` del ingreso original no existe en BD (fue eliminada), se loguea warning y la cancelación continúa

---

## D. Concepto de movimiento utilizado

**`ConceptoMovimientoCaja.ReversionVenta = 21`**

Ubicación: `Models/Enums/ConceptoMovimientoCaja.cs`

```csharp
DevolucionCliente = 20,
ReversionVenta = 21,   // ← nuevo
AjusteCaja = 30,
```

El valor 21 fue elegido como continuación natural de la secuencia `DevolucionCliente = 20`, sin afectar ningún valor existente.

---

## E. Implementación

### Archivos modificados

| Archivo | Clasificación | Cambio |
|---------|---------------|--------|
| `Models/Enums/ConceptoMovimientoCaja.cs` | Canónico | Nuevo valor `ReversionVenta = 21` |
| `Services/Interfaces/ICajaService.cs` | Canónico | Nuevo método `RegistrarContramovimientoVentaAsync` |
| `Services/CajaService.cs` | Canónico | Implementación de `RegistrarContramovimientoVentaAsync` |
| `Services/VentaService.cs` | Canónico | Llamada a contramovimiento en `CancelarVentaAsync` |
| `TheBuryProyect.Tests/Integration/VentaServiceCancelarCajaTests.cs` | Nuevo | 11 tests de integración |

### 15 stubs de ICajaService actualizados (solo firma)

Todos los stubs en archivos de test del nuevo método fueron agregados como:
- `=> Task.FromResult<MovimientoCaja?>(null)` — en stubs de tests que ejercen `CancelarVentaAsync` sobre ventas Confirmadas/Facturadas
- `=> throw new NotImplementedException()` — en stubs donde el flujo no invoca el nuevo método

### Firma del nuevo método en ICajaService

```csharp
Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(
    int ventaId,
    string ventaNumero,
    string motivo,
    string usuario);
```

### Cambio en CancelarVentaAsync (VentaService.cs)

```csharp
_validator.ValidarNoEstaCancelada(venta);
var estadoOriginal = venta.Estado;  // capturar antes de cambiar a Cancelada

// ... todo el flujo existente de cancelación ...

await _context.SaveChangesAsync();

// Crear contramovimiento de caja para ventas que tuvieron ingreso inmediato
if (estadoOriginal == EstadoVenta.Confirmada || estadoOriginal == EstadoVenta.Facturada)
{
    var usuario = _currentUserService.GetUsername();
    await _cajaService.RegistrarContramovimientoVentaAsync(
        venta.Id, venta.Numero, motivo, usuario);
}

await transaction.CommitAsync();
```

### RegistrarContramovimientoVentaAsync — lógica principal

1. Buscar `MovimientoCaja` de tipo `Ingreso` con `VentaId == ventaId` (primario)
2. Fallback: buscar con `ReferenciaId == ventaId` y `VentaId == null` (movimientos registrados sin FK directa)
3. Si no existe ingreso → log warning → `return null` (cancelación continúa)
4. Guard: verificar si ya existe un `Egreso` con `ConceptoMovimientoCaja.ReversionVenta` para el mismo `VentaId` → si sí → `return null`
5. Verificar que `AperturaCaja` del movimiento original existe en BD → si no → log warning → `return null`
6. Crear contramovimiento con:
   - `Tipo = Egreso`
   - `Concepto = ReversionVenta`
   - `AperturaCajaId = movimientoOriginal.AperturaCajaId`
   - `TipoPago = movimientoOriginal.TipoPago`
   - `VentaId = ventaId`
   - `Monto = movimientoOriginal.Monto`
   - `Descripcion = $"Reversión por cancelación de venta {ventaNumero}"`
   - `Observaciones = $"Contramovimiento del ingreso #{movimientoOriginal.Id}. Motivo: {motivo}"`
7. `_context.MovimientosCaja.Add(contramovimiento)`
8. `await _context.SaveChangesAsync()`
9. Retornar contramovimiento creado

**Seguridad transaccional**: `CajaService` usa el mismo `AppDbContext` inyectado que `VentaService`. `CancelarVentaAsync` tiene una transacción abierta con `BeginTransactionAsync`. El `SaveChangesAsync` interno de `RegistrarContramovimientoVentaAsync` participa en esa misma transacción. Si el `CommitAsync` falla, el contramovimiento también es revertido. Mismo patrón que `RegistrarMovimientoVentaAsync` en `ConfirmarVentaAsync`.

---

## F. Tests

### Archivo: `TheBuryProyect.Tests/Integration/VentaServiceCancelarCajaTests.cs`

**Infraestructura**: tests de integración con SQLite in-memory, CajaService real + VentaService real, AppDbContext compartido. Stubs mínimos para servicios periféricos no relacionados con Caja.

| # | Test | Verifica |
|---|------|----------|
| 1 | `CancelarVenta_Confirmada_Efectivo_CreaContramovimientoEgreso` | Se crea MovimientoCaja Egreso con ReversionVenta |
| 2 | `CancelarVenta_Confirmada_Efectivo_SaldoCajaNeutralizado` | Saldo de caja pasa de 250m a 0m tras cancelación |
| 3 | `CancelarVenta_Confirmada_Efectivo_IngresoOriginalIntacto` | El ingreso original no es modificado ni eliminado |
| 4 | `CancelarVenta_Facturada_CreaContramovimiento_YFacturaAnulada` | Factura queda Anulada + Caja revertida (integración con 9.2) |
| 5 | `CancelarVenta_IntentoCancelarDosVeces_NoHayDuplicadoDeContramovimiento` | Guard anti-duplicación: segunda cancelación no genera segundo egreso |
| 6 | `CancelarVenta_Presupuesto_NoCreaBContramovimiento` | Venta en Presupuesto no genera contramovimiento |
| 7 | `CancelarVenta_Confirmada_CreditoPersonal_NoContramovimiento` | TipoPago.CreditoPersonal no genera contramovimiento |
| 8 | `CancelarVenta_Confirmada_Efectivo_VentaQuedaCancelada` | Estado de venta queda Cancelada |
| 9 | `RegistrarContramovimientoVenta_IngresoExistente_CreaEgresoReversionVenta` | CajaService aislado: crea contramovimiento correctamente |
| 10 | `RegistrarContramovimientoVenta_SinIngreso_RetornaNull` | Sin ingreso previo → null, sin excepción |
| 11 | `RegistrarContramovimientoVenta_YaRevertido_RetornaNull` | Ingreso ya revertido → null (guard anti-duplicación) |

**Total en suite con filtro de fase**: 571 tests pasando, 0 errores.

---

## G. Comportamiento verificado de integración con Fase 9.2

El test 4 (`CancelarVenta_Facturada_CreaContramovimiento_YFacturaAnulada`) valida explícitamente que ambas fases coexisten sin interferencia:

- Al cancelar una venta `Facturada`:
  1. Factura → `Anulada = true` ✓ (Fase 9.2)
  2. MovimientoCaja → Egreso ReversionVenta creado ✓ (Fase 9.3)
  3. Venta → `Cancelada` ✓
  4. Stock revertido ✓ (comportamiento previo intacto)

---

## H. Riesgos y deuda remanente

### Resuelto en esta fase
- [x] H-A2 — MovimientoCaja activo sin reversión tras cancelación
- [x] H-B1 — No existía `ReversionVenta` como concepto ni método de reversión en ICajaService

### Deuda remanente (no parte de esta fase)

**H-C2 — Transferencia y MercadoPago registrados como `VentaEfectivo`**  
Ambos medios usan concepto `VentaEfectivo`. El contramovimiento de esta fase también hereda ese concepto del movimiento original. No es un problema nuevo — el contramovimiento copia el `TipoPago` y `Concepto` del ingreso original para simetría contable. La distinción de medios no-efectivo es deuda de Fase 9.4.

**H-C1 — Sin estado de acreditación en MovimientoCaja**  
Sigue sin estado Pendiente/Acreditado/Revertido. Fase 9.4.

**H-B2 — AnularFacturaAsync no revierte Caja**  
Anular una factura manualmente (sin cancelar la venta) no revierte el MovimientoCaja. Fuera del alcance de las fases 9.2–9.3. Requiere decisión funcional.

### Limitación conocida: AperturaCaja cerrada

Si la `AperturaCaja` del ingreso original está `Cerrada = true`, el contramovimiento se registra igualmente en esa apertura. No se bloquea por estado de apertura. Esto es coherente con la lógica existente de `RegistrarMovimientoVentaAsync` y `RegistrarMovimientoDevolucionAsync`.

Si en el futuro se necesita impedir registros en aperturas cerradas, será un cambio transversal a todos los métodos de caja.

---

## I. Build y verificaciones

| Verificación | Resultado |
|--------------|-----------|
| `dotnet build --configuration Release` | OK — 0 errores, 0 advertencias |
| `dotnet test --filter "VentaService\|Caja\|..."` | 571 Passed, 0 Failed |
| `git diff --check` | OK — sin whitespace errors |

---

## J. Checklist

### Fase 9.3 — Contramovimiento de Caja al cancelar

- [x] Diagnóstico de estado previo documentado
- [x] Decisión de diseño justificada (Opción D — Contramovimientos)
- [x] `ConceptoMovimientoCaja.ReversionVenta = 21` agregado
- [x] `RegistrarContramovimientoVentaAsync` agregado a `ICajaService`
- [x] Implementación en `CajaService` con búsqueda primaria + fallback + guard + degradación graceful
- [x] `estadoOriginal` capturado en `CancelarVentaAsync` antes de cambiar estado
- [x] Llamada a contramovimiento en `CancelarVentaAsync` solo para Confirmada/Facturada
- [x] 15 stubs de `ICajaService` actualizados con la nueva firma
- [x] 11 tests de integración en `VentaServiceCancelarCajaTests.cs`
- [x] Integración con Fase 9.2 verificada (test 4)
- [x] Guard anti-duplicación verificado (test 5 y test 11)
- [x] Casos de exclusión verificados (tests 6, 7)
- [x] Build Release: OK
- [x] Suite de tests: 571 pasando, 0 errores
- [x] `git diff --check`: OK
- [x] Documento creado en `docs/fase-9.3-contramovimiento-caja-cancelacion.md`
- [x] `docs/fase-9.1-diagnostico-caja-comprobantes-cancelacion.md` actualizado

### Pendientes producción (NO implementados en esta fase)

- [ ] 9.4 — Estados de acreditación para Transferencia / MercadoPago
- [ ] 9.5 — QA E2E flujo venta → factura → caja → cancelación
- [ ] Revisar `AnularFacturaAsync` para reversión de caja en anulación manual de factura

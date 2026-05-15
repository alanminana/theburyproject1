# Fase 9.4B — Saldo Real y Acreditación Manual de Caja

## A. Diagnóstico previo

| Aspecto | Estado antes de esta fase |
|---|---|
| `CalcularSaldoActualAsync` | Incluye TODOS los movimientos (Ingreso - Egreso), sin filtrar por `EstadoAcreditacion`. Comportamiento correcto como **saldo operativo**. |
| `DetallesAperturaViewModel` | Tenía `SaldoActual` pero no `SaldoReal` ni `SaldoPendienteAcreditacion`. |
| `AuditableEntity` | Ya tenía `UpdatedAt` y `UpdatedBy` — disponibles para auditoría de acreditación. |
| Acreditación manual | No existía ningún método en servicio ni acción en controller. |
| UI | Mostraba badges de estado de acreditación pero sin botón de acción. |

---

## B. Decisión V1

- **Mantener `CalcularSaldoActualAsync`** como saldo operativo sin cambios. Compatibilidad total.
- **Agregar `CalcularSaldoRealAsync`** con la lógica correcta de reversiones.
- **Agregar `AcreditarMovimientoAsync`** — acreditación manual con validación de estado.
- **No implementar**: conciliación automática, rechazo/anulación avanzada, integración bancaria real.

---

## C. Reglas de saldo

### Saldo operativo (`CalcularSaldoActualAsync`)
```
SaldoOperativo = MontoInicial + TotalIngresos - TotalEgresos
```
Incluye **todos** los movimientos independientemente del `EstadoAcreditacion`.

### Saldo real (`CalcularSaldoRealAsync`)
```
SaldoReal = MontoInicial + IngresosReales - EgresosReales
```

**IngresosReales**: movimientos Ingreso con:
- `EstadoAcreditacion == Acreditado`
- `EstadoAcreditacion == null` (movimientos manuales: gastos, cobros sin estado explícito)

**EgresosReales**: movimientos Egreso con:
- `EstadoAcreditacion == null`
- `EstadoAcreditacion == NoAplica`
- `EstadoAcreditacion == Revertido` **Y** existe un Ingreso previo con la misma `VentaId` con `EstadoAcreditacion == Acreditado`

### Regla de reversión
| Escenario | SaldoReal |
|---|---|
| Venta Efectivo (Acreditado) cancelada | El ingreso sumó al SaldoReal; el contramovimiento lo resta. Neto: 0. |
| Venta Transferencia (Pendiente) cancelada | El ingreso nunca sumó al SaldoReal; el contramovimiento no resta. Neto: 0. |

### Saldo pendiente de acreditación
```
SaldoPendienteAcreditacion = Suma de Ingresos con EstadoAcreditacion == Pendiente
```

---

## D. Flujo manual de acreditación

**Método**: `AcreditarMovimientoAsync(int movimientoId, string usuario)`

**Transición permitida**: `Pendiente → Acreditado`

**Validaciones**:
- Movimiento debe existir y no estar eliminado
- Tipo debe ser `Ingreso`
- `EstadoAcreditacion` debe ser `Pendiente`

**No permite**:
- `Acreditado → Acreditado`
- `Revertido → Acreditado`
- `Anulado → Acreditado`
- `Rechazado → Acreditado`
- Egresos

**Auditoría**: actualiza `UpdatedAt = DateTime.UtcNow` y `UpdatedBy = usuario`.

**Acción controller**: `POST /Caja/AcreditarMovimiento` con `[PermisoRequerido(Modulo = "caja", Accion = "edit")]`.

---

## E. Cambios UI

**KPIs nuevos** (panel sobre tabla de movimientos):
- **Saldo real acreditado**: en verde, con leyenda "Solo dinero efectivamente recibido o acreditado"
- **Pendiente de acreditación**: en ámbar si > 0; en gris si = 0

**Tabla de movimientos**:
- Nueva columna "Acciones" (solo visible si la caja está abierta y el usuario puede operar)
- Botón "Marcar acreditado" visible **únicamente** en filas con `Tipo = Ingreso` y `EstadoAcreditacion = Pendiente`
- Confirmación nativa antes de POST
- Filas no pendientes muestran `—` en la columna de acciones

**Footer de tabla**:
- "Saldo calculado al momento" renombrado a "Saldo operativo"
- `colspan` ajustado dinámicamente según presencia de columna Acciones

---

## F. Tests (`CajaServiceSaldoRealAcreditacionTests.cs`)

| Test | Qué verifica |
|---|---|
| `SaldoReal_CeroSinMovimientos` | Saldo real arranca en 0 |
| `SaldoReal_IngresoEfectivoAcreditado_IncluyeEnSaldoReal` | Efectivo acreditado suma al saldo real |
| `SaldoReal_TransferenciaPendiente_NoIncrementaSaldoReal` | Transferencia pendiente no impacta saldo real |
| `SaldoReal_MercadoPagoPendiente_NoIncrementaSaldoReal` | MercadoPago pendiente no impacta saldo real |
| `SaldoReal_IngresoSinEstado_IncluyeEnSaldoReal` | Movimientos manuales sin estado cuentan como reales |
| `SaldoReal_EgresoSinEstado_ReduceSaldoReal` | Egresos sin estado (gastos) reducen saldo real |
| `SaldoOperativo_IncluyeTransferenciaPendiente` | Saldo operativo no cambió — sigue incluyendo pendientes |
| `SaldoOperativo_YSaldoReal_DifienenConPendiente` | Operativo ≠ Real cuando hay pendientes |
| `SaldoReal_ReversionDeIngresoAcreditado_ReduceSaldoReal` | Cancelar venta efectivo reduce saldo real |
| `SaldoReal_ReversionDeIngresoPendiente_NoImpactaSaldoReal` | Cancelar venta transferencia pendiente no impacta saldo real |
| `AlAcreditarTransferencia_SaldoRealAumenta` | Acreditar manualmente → saldo real aumenta |
| `AcreditarMovimiento_Pendiente_Exito` | Transición Pendiente→Acreditado correcta |
| `AcreditarMovimiento_YaAcreditado_LanzaExcepcion` | No puede acreditar movimiento ya acreditado |
| `AcreditarMovimiento_Revertido_LanzaExcepcion` | No puede acreditar movimiento revertido |
| `AcreditarMovimiento_Egreso_LanzaExcepcion` | No puede acreditar egresos |
| `AcreditarMovimiento_NoExiste_LanzaExcepcion` | No encontrado → excepción |
| `CancelacionTransferencia_SaldoOperativoNeutral` | Regresión 9.3: cancelación sigue neutralizando caja |

---

## G. Qué NO se tocó

- Lógica de ventas (confirmar, facturar)
- Comprobantes (`ComprobanteService`, `FacturacionService`)
- Trazabilidad individual (Fase 8.2)
- Cancelación 9.2 / 9.3 (sin regresión)
- Entidades ni migraciones (ningún campo nuevo — `UpdatedAt`/`UpdatedBy` ya existían)
- `CalcularSaldoActualAsync` (sin cambios, compatible)
- Integración MercadoPago real
- Conciliación bancaria automática

---

## H. Riesgos y deuda remanente

| Riesgo | Severidad | Mitigación / Pendiente |
|---|---|---|
| `SaldoPendienteAcreditacion` es simplemente la suma de ingresos Pendiente, no descuenta egresos Revertidos de esos pendientes | Baja | Suficiente para V1 operativo |
| Botón "Marcar acreditado" usa `confirm()` nativo — no es un modal de confirmación elegante | Baja | Mejora UX futura |
| No existe flujo de rechazo/anulación de movimiento pendiente | Media | Pendiente V2 si se necesita |
| Caja cerrada no permite acreditar movimientos pendientes históricos | Media | Pendiente — podría necesitarse para reconciliación post-cierre |
| Movimientos de CobroCuota y AnticipoCredito no tienen `EstadoAcreditacion` explícito | Baja | Correctamente tratados como `null` → reales |

---

## I. Checklist

- [x] `ICajaService.CalcularSaldoRealAsync` definido
- [x] `ICajaService.AcreditarMovimientoAsync` definido
- [x] `CajaService.CalcularSaldoRealAsync` implementado con regla de reversión correcta
- [x] `CajaService.AcreditarMovimientoAsync` implementado con validaciones
- [x] `CajaService.ObtenerDetallesAperturaAsync` popula `SaldoReal` y `SaldoPendienteAcreditacion`
- [x] `DetallesAperturaViewModel`: campos `SaldoReal` y `SaldoPendienteAcreditacion` agregados
- [x] `CajaController.AcreditarMovimiento` POST con permiso `edit`
- [x] Vista `DetallesApertura_tw.cshtml`: KPIs de SaldoReal y Pendiente
- [x] Vista: botón "Marcar acreditado" solo en Ingreso/Pendiente, con confirmación
- [x] Vista: columna Acciones solo visible en apertura abierta para el operador
- [x] Tests: 17 tests nuevos en `CajaServiceSaldoRealAcreditacionTests`
- [x] Regresión 9.3 cubierta en tests
- [x] Build verde
- [x] Tests previos de Caja siguen pasando

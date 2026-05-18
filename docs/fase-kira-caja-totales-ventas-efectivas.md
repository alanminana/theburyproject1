# Fase Kira — Caja: Totales de ventas efectivas

Rama: `kira/caja-totales-ventas-efectivas`  
Fecha: 2026-05-18

---

## A. Diagnóstico

Pantalla afectada: **Caja → Detalles de apertura → Ventas vinculadas al turno**.

`CajaService.ObtenerDetallesAperturaAsync` cargaba `ventasDelTurno` filtrando solo por `AperturaCajaId == aperturaId && !IsDeleted`, sin distinguir `EstadoVenta`.

---

## B. Bug detectado

`TotalesPorTipoPago` y `TotalRecargoDebito` se calculaban desde `ventasDelTurno` completo, incluyendo ventas que no representan ingresos reales:

- `EstadoVenta.Cotizacion`
- `EstadoVenta.Presupuesto`
- `EstadoVenta.Cancelada`
- `EstadoVenta.PendienteRequisitos`
- `EstadoVenta.PendienteFinanciacion`

Los saldos financieros reales (`TotalIngresos`, `TotalEgresos`, `SaldoActual`, `SaldoReal`, `SaldoPendienteAcreditacion`) no estaban afectados porque se calculan desde `MovimientoCaja`, que es la fuente autoritativa.

---

## C. Estados efectivos incluidos

Solo se suman en `TotalesPorTipoPago` y `TotalRecargoDebito`:

| Estado | Valor |
|---|---|
| `EstadoVenta.Confirmada` | 2 |
| `EstadoVenta.Facturada` | 3 |
| `EstadoVenta.Entregada` | 4 |

---

## D. Estados excluidos

| Estado | Motivo |
|---|---|
| `EstadoVenta.Cotizacion` (0) | Sin compromiso de pago |
| `EstadoVenta.Presupuesto` (1) | Presupuesto formal, no confirmado |
| `EstadoVenta.Cancelada` (5) | Operación anulada |
| `EstadoVenta.PendienteRequisitos` (6) | No completada |
| `EstadoVenta.PendienteFinanciacion` (7) | Crédito no configurado |

---

## E. Cambios aplicados

### `Services/CajaService.cs`

Dentro de `ObtenerDetallesAperturaAsync`, después de cargar `ventasDelTurno`:

```csharp
var ventasEfectivas = ventasDelTurno
    .Where(v =>
        v.Estado == EstadoVenta.Confirmada ||
        v.Estado == EstadoVenta.Facturada ||
        v.Estado == EstadoVenta.Entregada)
    .ToList();
```

`TotalesPorTipoPago` y `TotalRecargoDebito` usan `ventasEfectivas`.  
`VentasDelTurno` sigue usando `ventasDelTurno` completo (listado auditoría).

---

## F. Tests agregados

Archivo: `TheBuryProyect.Tests/Integration/CajaServiceTests.cs`

| Test | Descripción |
|---|---|
| `CajaDetalle_NoSumaPresupuestosEnTotalesPorTipoPago` | Venta Presupuesto no aparece en TotalesPorTipoPago |
| `CajaDetalle_NoSumaCanceladasEnTotalesPorTipoPago` | Venta Cancelada no aparece en TotalesPorTipoPago |
| `CajaDetalle_TotalRecargoDebitoExcluyePresupuesto` | Presupuesto con débito/recargo no suma en TotalRecargoDebito |
| `CajaDetalle_TotalRecargoDebitoExcluyeCancelada` | Cancelada con débito/recargo no suma en TotalRecargoDebito |
| `CajaDetalle_SumaConfirmadaFacturadaEntregadaEnTotalesPorTipoPago` | Confirmada + Facturada + Entregada suman; Presupuesto y Cancelada no |

`SeedVentaAsync` recibió parámetro opcional `EstadoVenta estado = EstadoVenta.Confirmada` sin romper llamadas existentes.

---

## G. Validaciones ejecutadas

```
dotnet build --configuration Release     → 0 errores, 0 advertencias
dotnet test --filter "Caja"              → 166/166 correctas (antes: 161)
dotnet test --filter "Caja|Venta|MovimientoCaja" → 894/894 correctas
dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal" → 1/1 correcta
git diff --check                         → sin espacios en blanco problemáticos
```

---

## H. Qué NO se tocó

- `TotalIngresos`, `TotalEgresos`, `SaldoActual`, `SaldoReal`, `SaldoPendienteAcreditacion`
- `MovimientoCaja` y su lógica de acreditación
- `VentaService` y su lógica de cancelación
- `VentasDelTurno` en el ViewModel (sigue mostrando todas las ventas del turno)
- Lógica de cierre/apertura de Caja
- Cotización, ProductoUnidad, Stock, Factura
- Vistas Razor (no fue necesario)

---

## I. Riesgos / Deuda remanente

- `EstadoVenta.Cotizacion` (`0`) y `EstadoVenta.Presupuesto` (`1`) podrían tener `AperturaCajaId` asignado si fueron iniciadas desde Caja abierta. La exclusión es intencional: no representan ingresos confirmados.
- Si en el futuro se agrega un nuevo estado efectivo (e.g. `Liquidada`), debe actualizarse el filtro en `ventasEfectivas` y los tests correspondientes.
- `VentasDelTurno` en la vista Caja muestra todas las ventas del turno (incluyendo Presupuestos). Esto puede generar confusión visual — ver Fase 2.

---

## J. Recomendación para Fase 2 UX

Prompt sugerido:

> **KIRA — FASE CAJA 2 — Tabla de ventas del turno con distinción visual por estado**
>
> En la vista `Views/Caja/Detalles.cshtml` (o equivalente), la tabla `VentasDelTurno` muestra todas las ventas vinculadas al turno, incluyendo Presupuestos y Canceladas.
>
> Objetivo: diferenciar visualmente las ventas efectivas (Confirmada, Facturada, Entregada) de las no efectivas (Presupuesto, Cancelada, PendienteRequisitos, PendienteFinanciacion).
>
> Opciones aceptables:
> - Badge/etiqueta de estado con color semántico por fila
> - Separación en dos secciones de la tabla
> - Filtro/tab para mostrar solo efectivas
>
> Restricciones:
> - No cambiar lógica de negocio ni service
> - No alterar saldos financieros
> - Mantener acceso al listado completo del turno para auditoría

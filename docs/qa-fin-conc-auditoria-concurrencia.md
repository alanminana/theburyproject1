# QA-FIN-CONC — Auditoría de concurrencia: cancelación de venta vs. pagos/caja

Base: commit `369b77e` (QA-FIN-P0). Worktree propio: `theburyproject1-qa-fin-conc` (rama `qa/fin-conc-20261014`).
Método: lectura de código (estático) + harness SQL Server escrito pero **no ejecutado** (turno QA-RELEASE pendiente).
Código de producción: sin cambios. BD de desarrollo: no tocada.

## 1. Veredicto

| Pregunta | Resultado |
|---|---|
| ¿Puede quedar una venta cancelada con un pago aplicado después de la guardia? | **FAIL (por análisis de código; confirmación dinámica BLOCKED)** — TOCTOU en `CancelarVentaAsync` |
| Pago empieza antes que la cancelación | PASS por diseño en RC (sin RCSI) — la guardia espera el X lock y rechaza |
| Cancelación empieza antes que el pago | PASS **solo** si el pago empieza tras el commit de la cancelación; **FAIL** si el pago confirma entre la guardia y la carga de cuotas |
| Pago entre carga de cuotas y su UPDATE | PASS — la `RowVersion` de la cuota aborta la cancelación (`DbUpdateConcurrencyException`) |
| Cierre de caja / movimientos | **FAIL (hallazgo adicional)** — sin transacción ni lock |
| Ejecución dinámica en SQL Server | **BLOCKED** (falta turno QA-RELEASE y BD `qa_fin_conc*`) |

## 2. Causa raíz

`CancelarVentaAsync` serializa **solo con otras cancelaciones** (lock de fila sobre `Ventas`). El flujo de pago (`CreditoService.RegistrarPagoCuotaAsync`, Serializable) **no lee ni bloquea `Ventas`** (a confirmar por `EmitirDocumentosPagoAsync` → `DocumentoContextoBuilder`, línea ~328, que sí lee `Ventas`). No existe un recurso común entre ambos flujos.

La guardia `AsegurarSinPagosAplicadosParaCancelarAsync` corre en `READ COMMITTED`: no retiene ningún lock tras leer. Un pago que empieza justo después no es visto, y la guardia **no se reevalúa**.

## 3. Huella de transacciones y locks

| | Cancelación (`VentaService.cs:1203`) | Pago (`CreditoService.cs:911`) |
|---|---|---|
| Aislamiento | `BeginTransactionAsync()` → READ COMMITTED (default SQL Server, no hay RCSI en el repo) | `IsolationLevel.Serializable` |
| Lock inicial | `ExecuteUpdate` sobre `Ventas` (X, fila) | ninguno propio; lee `Cuotas`+`Creditos` (range/S retenidos) |
| Guardia | 3 `AnyAsync` sobre Cuotas/PagosCuota/MovimientosCaja, **sin locks retenidos** | — |
| Escribe | Venta, Cuotas no pagadas → `Cancelada`, Credito → `Cancelado`, stock, unidades, caja (contramovimiento) | Cuota (MontoPagado/Estado), MovimientoCaja `CobroCuota`, PagoCuota, Credito.Saldo |
| Control optimista | `RowVersion` de cada Cuota/Credito **cargada** (tracked) | `RowVersion` de cuota (obligatoria en los comandos públicos) |
| Rechaza si... | guardia detecta pago | cuota `Cancelada` (`CreditoService.cs:1218`); **no** revisa `Venta.Estado` ni `Credito.Estado` |

Nota: `CancelarCreditoAsociadoAVenta` (`VentaService.cs:3598`) cancela toda cuota que no sea `Pagada`/`Cancelada`, es decir también las **`Parcial`**, y pone `SaldoPendiente = 0`.

## 4. Secuencia peligrosa (I-4)

```
Cancelación (RC)                         Pago (SERIALIZABLE)
-------------------------------------    -------------------------------------
BEGIN TRAN
UPDATE Ventas (X fila 1)
Guardia: Cuotas/Pagos/Caja  -> limpio
        ...                              BEGIN TRAN
        ...                              SELECT cuota 1 (+credito)   [S/range]
        ...                              UPDATE cuota 1 (MontoPagado, Parcial)
        ...                              INSERT MovimientoCaja CobroCuota, PagoCuota
        ...                              COMMIT
Carga cuotas (lee COMMITTED: cuota1=Parcial, RowVersion nueva)
UPDATE cuotas -> Cancelada (RowVersion fresca => 0 conflictos)
UPDATE Credito -> Cancelado, Saldo = 0
COMMIT
=> Venta Cancelada + Credito Cancelado + PagoCuota Aplicado + ingreso CobroCuota en caja
   (el contramovimiento solo revierte conceptos de venta: el cobro queda huérfano).
```

## 5. Interleavings

| # | Orden | Resultado esperado hoy | Mecanismo |
|---|---|---|---|
| I-1 | Pago completo → Cancelación | Rechazada ✔ | guardia ve el pago |
| I-2 | Pago en vuelo con X lock → Cancelación | Guardia espera, luego rechaza ✔ | RC bloquea contra X (sin RCSI) |
| I-3 | Cancelación carga cuotas → Pago confirma → Cancelación escribe | Cancelación aborta ✔ | RowVersion de cuota |
| **I-4** | **Guardia pasa → Pago confirma → Cancelación carga cuotas** | **Cancelada con pago aplicado ✘** | nada lo impide |
| I-5 | Igual que I-4 pero el pago lee `Ventas` | Bloqueo cruzado → deadlock/timeout; una transacción aborta, invariante se conserva (a observar) | X en Ventas vs S/X en Cuotas |
| I-6 | Cancelación confirma → Pago empieza | Pago rechazado (cuota `Cancelada`) ✔ | estado de cuota |
| I-7 | Con **RCSI activo** | I-2 deja de bloquear: la guardia ve datos viejos; se salva solo por RowVersion si la cancelación ya había cargado cuotas | requiere verificar `is_read_committed_snapshot_on` |

I-4 solo es posible si la cancelación carga las cuotas **después** del commit del pago; la ventana es el trabajo entre la guardia y `RestaurarCreditoPersonall` (devolución de stock, unidades) y crece con carga.

## 6. Otros flujos (misma familia, análisis estático)

- **Cierre de caja** (`CajaService.CerrarCajaAsync`, ~1135): sin transacción, sin lock. Lee movimientos, calcula totales y marca `Cerrada`; un cobro, anticipo, devolución o **contramovimiento de cancelación** que se inserta entre la lectura y el `SaveChanges` queda en una apertura cerrada y fuera de `TotalIngresos/EgresosSistema` (inserciones no tocan la fila de apertura, así que la `RowVersion` no lo detecta). Las altas (`RegistrarMovimientoCuotaAsync`, `…Anticipo…`, `…Devolucion…`) validan `ObtenerAperturaActivaParaUsuarioAsync` sin lock → TOCTOU simétrico. **FAIL probable.** Dos cierres simultáneos: el segundo falla por RowVersion (excepción no controlada → 500; seguro pero feo).
- **Pago vs. pago** (misma cuota): protegido por Serializable + RowVersion obligatoria. PASS. Sin clave de idempotencia: un doble envío con RowVersion distinta tras un pago parcial legítimo no es detectable más que por RowVersion.
- **Devolución vs. cancelación de venta**: `CancelarVentaAsync` no consulta devoluciones y `DevolucionService.CompletarAsync` no consulta `Venta.Estado` con lock; ambas revierten stock/caja. Riesgo de doble reversión de stock y de egreso de caja. No verificado dinámicamente.
- **Anticipo** (`VentaService.cs:1088`): se registra en el flujo de confirmación; la cancelación posterior depende de que `ConceptosIngresoVenta` lo incluya (no incluye `Anticipo`). Revisar con decisión de negocio (D-x del doc FIN-P0).

## 7. Estrategia propuesta (sin cambiar reglas contables)

1. **Recurso común: la fila de `Creditos`.** Ambas rutas toman `UPDLOCK, HOLDLOCK` sobre el crédito (`SELECT … FROM Creditos WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id`, o `ExecuteUpdate` de `UpdatedAt` como ya hace la venta) **antes** de cualquier lectura de validación, siempre en el mismo orden (Venta → Crédito → Cuotas) para evitar deadlocks.
   - Cancelación: tras el claim de la venta, lock del crédito y **luego** la guardia.
   - Pago: lock del crédito al inicio de la transacción y rechazo si `Credito.Estado` ∈ {Cancelado, Finalizado, Rechazado} (hoy solo se mira la cuota).
2. **Re-verificación dentro del lock**: la guardia debe ejecutarse después del lock del crédito (así deja de ser TOCTOU) y, para defensa en profundidad, repetirse inmediatamente antes del `SaveChanges` final.
3. **Control optimista complementario**: en la cancelación, adjuntar a la transacción un `UPDATE Creditos … WHERE Id AND RowVersion = @leida` (el crédito ya se carga) y comprobar que el crédito cargado coincide con el leído tras la guardia.
4. **Caja**: transacción + lock de la fila de `AperturaCaja` (`UPDLOCK`) tanto en `CerrarCajaAsync` como en cada `RegistrarMovimiento*` (o alta de movimiento con `ExecuteUpdate` de la apertura `WHERE Cerrada = 0`, verificando filas afectadas). Capturar `DbUpdateConcurrencyException` en el cierre y devolver mensaje de negocio.
5. **Idempotencia**: clave de operación (`IdempotencyKey` único con índice filtrado) en `PagoCuota` y en las cancelaciones/contramovimientos; los reintentos devuelven el resultado previo en lugar de repetir.
6. **Locks de rango**: no depender de Serializable global; el lock explícito de crédito cubre la ventana con costo acotado.

Decisiones de negocio pendientes (no implementadas): qué hacer con la cancelación cuando hay un pago concurrente (rechazar — recomendado hasta que exista reversión auditada), y tratamiento de anticipos/devoluciones en la cancelación.

## 8. Archivos que requerirían cambios (fase de implementación)

- `Services/VentaService.cs` — `CancelarVentaAsync`, `AsegurarSinPagosAplicadosParaCancelarAsync`, `RestaurarCreditoPersonall`.
- `Services/CreditoService.cs` — `RegistrarPagoCuotaAsync` (≈885–1090) y el pago múltiple (≈1519), `ObtenerCuotaDelCreditoAsync`.
- `Services/CajaService.cs` — `CerrarCajaAsync`, `RegistrarMovimientoCuotaAsync`, `…Anticipo…`, `…Devolucion…`, `RegistrarContramovimientoVentaAsync`.
- `Services/DevolucionService.cs` — `CompletarAsync` (verificar estado de venta/crédito bajo lock).
- Modelos/migración: `IdempotencyKey` en `PagoCuota` (si se aprueba).

## 9. Tests

`TheBuryProyect.Tests/Concurrency/CancelarVsPagoLockModelSqlServerTests.cs` — modela la huella de locks de ambas transacciones con ADO.NET contra SQL Server (no ejecuta los servicios reales; fase 2). Gated por `QA_RELEASE_TURNO=1` y `QA_FIN_CONC_SQL` (BD con catálogo `qa_fin_conc*`; solo toca el esquema `qaconc`).

| Test | Esperado hoy |
|---|---|
| I1 pago antes de cancelar | PASS |
| I2 pago en vuelo (X) | PASS (requiere RCSI OFF; lo verifica) |
| I3 pago tras carga de cuotas | PASS (RowVersion) |
| **I4 TOCTOU** | **FAIL — demuestra el bug** |
| I5 pago lee Venta | invariante se conserva; se registra deadlock/timeout |
| M1 / M2 con lock de crédito | PASS — validan la mitigación |

Estado de ejecución: compilan; `dotnet test --filter CancelarVsPagoLockModel` → 7 omitidos (BLOCKED por autorización). SQLite no se usó como evidencia de locking.

**Para correr (cuando QA-RELEASE dé turno):**
`$env:QA_RELEASE_TURNO='1'; $env:QA_FIN_CONC_SQL='Server=...;Database=qa_fin_conc_001;...'; dotnet test TheBuryProyect.Tests --filter CancelarVsPagoLockModel`

## 10. Pendientes / limitaciones

- Fase 2: repetir I-4/I-5/M-1/M-2 con los servicios reales (interceptor de comandos como compuerta) y confirmar si `EmitirDocumentosPagoAsync` lee `Ventas`.
- Verificar `is_read_committed_snapshot_on` en el entorno objetivo (cambia I-2/I-7).
- Devolución/anticipo/cierre: hallazgos estáticos sin reproducción.

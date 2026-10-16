# QA-FIN-RESIDUAL — Auditoría de cierre de caja, devoluciones, anticipos e idempotencia

> **Nota de integración (2026-10-16).** Este documento es la auditoría original (rama `qa/fin-residual-20261014`, commit `24973bfe`). Desde entonces `main` incorporó FIN-P0 y CONC-P0. **Los defectos descritos aquí siguen ABIERTOS en `main`; esta integración solo suma evidencia y NO los corrige.** Estado vigente, cifras y plan de conversión a regresión: [qa-fin-residual-registro-defectos.md](qa-fin-residual-registro-defectos.md).

Rama `qa/fin-residual-20261014` (worktree `theburyproject1-qa-fin-residual`), base `5c40a67a` = QA-FIN-CONC-P0
(la corrección financiera más reciente; integrada en `integracion/qa-v2-20261014`, no en `main`). Sin merge ni push.

**Alcance respetado:** auditoría + tests de regresión. **No se modificó ningún servicio de producción**, no se
implementó reversión contable, no se tocó la base de desarrollo, no se ejecutó ningún E2E ni SQL Server (sin turno FIN).
Único cambio fuera de tests nuevos: `ConcSqlHostFixture` pasó de `sealed` a heredable (`SufijoCatalogo`) para que
la clase nueva use catálogo propio `<base>_res` y ambas corran en paralelo.

## 1. Veredicto

Cuatro de los cinco riesgos pendientes **existen**; dos se **confirmaron por ejecución** (sin concurrencia, con
servicios reales), el resto sigue siendo inferencia hasta tener turno SQL Server.

| Riesgo declarado | Resultado | Evidencia |
|---|---|---|
| Cerrar caja mientras entra un cobro | **FAIL (inferido) — BLOCKED dinámico**; la mitad secuencial de la causa raíz está **confirmada** | H-CAJA-1 ejecutado; R-01/R-02 sin correr |
| Devolver una venta a la vez que se cancela | **FAIL confirmado** en ambos órdenes, sin necesitar carrera | H-DEV-1/2/3 ejecutados |
| Anticipo y cancelación posterior | **FAIL confirmado** (el anticipo queda cobrado en una venta cancelada) | H-ANT-1/2 ejecutados |
| Reintentos sin clave de idempotencia | **Parcial**: cobros de cuota y cancelación **PASS** (RowVersion/estado); movimientos manuales **FAIL confirmado**; devolución inferido | H-IDEM-1 y contratos ejecutados |
| Facturar mientras se cancela o cobra | **PASS inferido** (RowVersion de `Venta`, sin ciclo de locks); un caso legado **FAIL inferido** | R-09/R-10 sin correr |

Un hallazgo no pedido, el más directo: **el contramovimiento de una cancelación se asienta en la apertura del
ingreso original aunque ya esté cerrada** (H-CAJA-2, confirmado): el libro de una caja cerrada deja de coincidir con
su `CierreCaja`.

Leyenda de evidencia: **CONFIRMADO** = ejecutado en esta rama (SQLite, servicios reales, 15/15 en verde);
**INFERIDO** = leído del código, sin ejecución; **BLOCKED** = test preparado y compilado, omitido por falta de turno.

## 2. Invariantes contables que deben mantenerse

| ID | Invariante |
|---|---|
| I-C1 | Una apertura cerrada es **inmutable**: Σ libro en efectivo = `CierreCaja.TotalIngresos/EgresosSistema`. Nada entra ni sale después del cierre. |
| I-C2 | Todo asiento de caja nuevo exige apertura **abierta y vigente**, verificada bajo el mismo lock que el cierre (no solo "leída antes"). |
| I-C3 | Una apertura tiene como máximo un `CierreCaja`; una caja, como máximo una apertura abierta. |
| I-V1 | Para una venta cancelada: Σ egresos de caja ligados a ella (reversión + reembolsos) = Σ ingresos ligados a ella (venta + anticipo). Nunca más. |
| I-V2 | El stock devuelto por cancelación + devoluciones de una venta ≤ lo vendido. |
| I-D1 | Σ cantidad devuelta (devoluciones no rechazadas) por producto ≤ cantidad vendida, también bajo concurrencia. |
| I-D2 | No se completa/crea una devolución sobre una venta cancelada; no se cancela una venta con devoluciones vivas (regla por defecto, ver §8). |
| I-A1 | Un anticipo > 0 confirmado en la venta queda asentado en caja, ligado a la venta, y la cancelación lo revierte o se rechaza. |
| I-I1 | Reintentar la misma operación (mismo token) produce un solo efecto. |
| I-F1 | Venta cancelada ⇒ sin factura vigente y sin ingreso huérfano. |
| (previos) | I-1..I-4 de QA-FIN-CONC/P0: crédito cancelado ⇒ 0 pagos aplicados; ledger = Σ MontoPagado; ≤ 1 contramovimiento por venta. |

## 3. Huella de transacciones y locks (código real)

| Operación | Transacción / aislamiento | Locks tomados | Control optimista | Valida |
|---|---|---|---|---|
| `CerrarCajaAsync` | **ninguna** (un `SaveChanges`); lectura RC | ninguno retenido | `RowVersion` de `AperturaCaja`; índice único `CierresCaja.AperturaCajaId` | `Cerrada`; **no** movimientos concurrentes |
| `RegistrarMovimientoAsync` (manual) | ninguna | ninguno | — | `Cerrada`, vencida (leídas sin lock) |
| `RegistrarMovimientoVentaAsync` | la del flujo (Confirmar/Facturar) | ninguno | — | apertura = `Venta.AperturaCajaId`; **no valida `Cerrada`** |
| `RegistrarMovimientoCuotaAsync` / `…Anticipo…` | la del cobro (Serializable) / la de Confirmar | S sobre la fila de apertura (solo cobro, por Serializable) | — | `ObtenerAperturaActivaParaUsuarioAsync` (sin lock en anticipo); anticipo devuelve `null` en silencio |
| `RegistrarContramovimientoVentaAsync` | la de `CancelarVentaAsync` | (lock de crédito+venta del P0) | — | asienta en `movimientoOriginal.AperturaCajaId`, **sin validar `Cerrada`** |
| `RegistrarMovimientoDevolucionAsync` | la de `CompletarDevolucionAsync` | ninguno | — | apertura **activa del usuario** (seguro frente al cierre) |
| `CrearDevolucionAsync` | RC | ninguno | — | cantidad ≤ vendido − ya devuelto (lectura sin lock); `Venta` solo existe/no borrada |
| `CompletarDevolucionAsync` | RC | fila de `Devolucion` | `RowVersion` de `Devolucion` (obligatorio) | solo `Estado = Aprobada`; **no mira `Venta.Estado`** |
| `CancelarVentaAsync` | RC | Crédito → Venta (P0) | `RowVersion` de cuotas/crédito | pagos aplicados; **no mira devoluciones ni anticipo** |
| `FacturarVentaAsync` | **ninguna** (varios `SaveChanges`) | ninguno | `RowVersion` de `Venta` | `Estado = Confirmada` |
| `ConfirmarVentaAsync` / `…CreditoAsync` | RC | ninguno propio | `RowVersion` de `Venta` | estado, caja del usuario (sin lock) |

Orden global de locks vigente (P0): **Crédito → Venta → Cuotas/Caja**. Toda corrección de §8 coloca la apertura **al final**.

## 4. Matriz de riesgos

Impacto financiero cualitativo: **Alto** = dinero mal contabilizado y difícil de detectar; **Medio** = descuadre
detectable en conciliación; **Bajo** = fricción/mensaje. Prob.: probabilidad real de ocurrencia en operación normal.

| ID | Flujo | Descripción | Evidencia | Estado | Impacto | Prob. | Test |
|---|---|---|---|---|---|---|---|
| H-CAJA-1 | Cobro/venta/anticipo vs cierre | Ningún alta de movimiento verifica la apertura bajo lock; `RegistrarMovimientoVentaAsync` ni siquiera valida `Cerrada` | **CONFIRMADO** (secuencial) + INFERIDO (carrera) | **FAIL** | Alto | Media: ventana = duración de Confirmar/cobro | `Hallazgo_RegistrarMovimientoVenta_AsientaIngresoEnAperturaCerrada`; R-01, R-02 |
| H-CAJA-2 | Cancelación vs cierre | Contramovimiento cae en la apertura original aunque esté **cerrada** | **CONFIRMADO** | **FAIL** | Alto | **Alta** (cancelar una venta de ayer) | `Hallazgo_CancelarVenta_ContramovimientoCaeEnAperturaYaCerrada_Y_DivergeDelCierre`; R-03 |
| H-CAJA-3 | Cierre con pagos pendientes | Movimientos `Pendiente` de acreditar se pueden acreditar tras el cierre; cierre no advierte | **CONFIRMADO** | **FAIL** (decisión de negocio) | Medio | Alta | `Hallazgo_CierreConMovimientoPendienteDeAcreditar…` |
| R-C4 | Cierre vs cierre | Índice único + RowVersion lo frenan; el segundo recibe el mensaje crudo de EF (`TempData["Error"] = ex.Message`) | INFERIDO | PASS (seguro) / mensaje deficiente | Bajo | Media | R-04 (BLOCKED) |
| R-C5 | Abrir vs abrir | Sin índice único filtrado; la defensa es `Caja.RowVersion` | INFERIDO | PASS probable | Medio si falla | Baja | R-05 (BLOCKED) |
| H-DEV-1 | Devolución sobre venta cancelada | `CrearDevolucionAsync` solo valida existencia | **CONFIRMADO** | **FAIL** | Medio | Media | `Hallazgo_CrearDevolucion_SobreVentaYaCancelada_SeAcepta` |
| H-DEV-2 | Devolver → cancelar | Cancelar contra-asienta el 100 % del ingreso encima del reembolso (egreso 150 sobre 100) y reintegra el stock otra vez (13 vs 12) | **CONFIRMADO** | **FAIL** | **Alto** | Media | `Hallazgo_CancelarVenta_TrasDevolucionReembolsada_DuplicaEgresoDeCajaYStock`; R-07 |
| H-DEV-3 | Cancelar → completar devolución aprobada | Se reembolsa de nuevo (egreso 200 sobre 100) y se reintegra stock (14 vs 12) | **CONFIRMADO** | **FAIL** | **Alto** | Media | `Hallazgo_CompletarDevolucionAprobada_SobreVentaCancelada_ReembolsaDeNuevo`; R-07 |
| R-D4 | Dos altas de devolución simultáneas | Ambas leen "ya devuelto = 0"; sin lock ni fila compartida escrita | INFERIDO | **FAIL** probable | Medio | Baja | R-06 (BLOCKED); `Contrato_CrearDevolucion_SecuencialNoPuedeExcederLoVendido` (PASS) |
| R-D5 | Doble completar | Estado + RowVersion de `Devolucion` | **CONFIRMADO** (secuencial) | PASS | — | — | `Contrato_CompletarDevolucion_Reintento_NoDuplicaReembolso`; R-08 (concurrente, BLOCKED) |
| R-D6 | Reembolso vs lo realmente cobrado | `Completar` no compara el reembolso con lo cobrado de la venta (p. ej. venta a crédito sin cuotas pagadas) | INFERIDO | Decisión de negocio | Medio | Baja | sin test (regla no definida) |
| H-ANT-1 | Anticipo y cancelación | Anticipo = `AnticipoCredito`, `ReferenciaId = CreditoId`, sin `VentaId`; ni se revierte ni bloquea la cancelación | **CONFIRMADO** | **FAIL** | **Alto** | Media | `Hallazgo_CancelarVentaACredito_NoRevierteNiBloqueaPorElAnticipoCobrado` |
| H-ANT-2 | Anticipo sin caja | `RegistrarMovimientoAnticipoAsync` devuelve `null` en silencio y `ConfirmarVentaCreditoAsync` lo ignora | **CONFIRMADO** (servicio) + INFERIDO (carrera con el cierre) | **FAIL** | Alto | Baja | `Hallazgo_RegistrarMovimientoAnticipo_SinCajaAbierta_DevuelveNullSinFallar` |
| H-IDEM-1 | Movimiento manual duplicado | Sin clave de idempotencia: un reintento asienta dos veces | **CONFIRMADO** | **FAIL** | Medio | Media (doble clic/reintento) | `Hallazgo_RegistrarMovimientoManual_ReintentoIdenticoDuplicaElAsiento` |
| R-I2 | Reintento de cobro de cuotas | `RowVersion` de cuota como token | **CONFIRMADO** (secuencial) | PASS | — | — | `Contrato_PagoMultipleDeCuotas_ReintentoConLaMismaRowVersion_SeRechazaSinDuplicar` |
| R-I3 | Reintento de cancelación | estado Cancelada | **CONFIRMADO** | PASS | — | — | `Contrato_CancelarVenta_Reintento_NoDuplicaContramovimientoNiStock` |
| R-I4 | Reintento de Confirmar / Crear devolución | Confirmar: estado + RowVersion (PASS inferido); Crear devolución: sin token (cap por cantidad) | INFERIDO | PASS / FAIL parcial | Medio | Baja | sin test propio |
| R-F1 | Facturar vs cancelar | `RowVersion` de `Venta` debería abortar a Facturar | INFERIDO | PASS probable | Medio si falla | Baja | R-09 (BLOCKED) |
| R-F2 | Facturar (legado) vs cancelar | Facturar commitea y **después** asienta ingreso de respaldo sin transacción: cancelar en el medio deja ingreso huérfano | INFERIDO | **FAIL** (solo ventas legadas sin ingreso) | Medio | Muy baja | R-10 (BLOCKED) |
| R-F3 | Facturar vs cobro de cuota | Facturar no retiene ningún otro lock ⇒ no hay ciclo; el cobro solo espera el commit corto | INFERIDO | PASS | — | — | sin test (propuesto, bajo valor) |
| R-V4 | Contramovimiento solo del primer ingreso | `FirstOrDefault` por Id: una venta con más de un ingreso `Venta*` revierte solo el primero | INFERIDO | riesgo bajo | Bajo | Muy baja | sin test |

## 5. Interleavings reproducibles (harness SQL Server, BLOCKED)

Archivo `TheBuryProyect.Tests/Concurrency/FinResidualSqlServerTests.cs`. Técnica idéntica a QA-FIN-CONC-P0:
`ConcGateInterceptor` detiene una operación en un comando SQL concreto (deterministas: la sincronía es por
`TaskCompletionSource`; el único `Delay` es la ventana para observar "queda bloqueada"). Cada test usa su propia
apertura/caja. Los tests afirman el **invariante deseado**: los marcados FAIL fallan hoy.

| Test | Compuerta | Secuencia | Esperado hoy | Invariante |
|---|---|---|---|---|
| R-01 | cierre antes de `INSERT CierresCaja` | cierre lee y calcula → **cobro confirma** → cierre commitea | **FAIL** | I-C1 |
| R-02 | cobro antes de `INSERT PagosCuota` | cobro en vuelo (movimiento sin commit) → arranca el cierre → ¿espera? | PASS sin RCSI / FAIL con RCSI | I-C1 |
| R-03 | cierre antes de `INSERT CierresCaja` | cierre lee → **cancelación** confirma → cierre commitea | **FAIL** | I-C1 |
| R-04 | cierre antes de `INSERT CierresCaja` | dos cierres | PASS (1 cierre; segundo rechazado) | I-C3 |
| R-05 | apertura antes de `INSERT AperturasCaja` | dos aperturas de la misma caja | PASS por `Caja.RowVersion` | I-C3 |
| R-06 | devolución antes de `INSERT Devoluciones` | dos altas por la cantidad total | **FAIL** | I-D1 |
| R-07 | cancelación antes de `INSERT MovimientosCaja` | completar devolución con reembolso mientras cancela | **FAIL** | I-V1, I-V2 |
| R-08 | completar antes de `INSERT MovimientosCaja` | doble completar | PASS | I-1 |
| R-09 | facturar antes de `INSERT Facturas` | cancelar completa; Facturar se libera | PASS por `RowVersion` | I-F1 |
| R-10 | facturar antes de su ingreso de respaldo | cancelar completa (nada que revertir); Facturar asienta | **FAIL** (legado) | I-F1 |

Pendiente de diseño (misma causa raíz que R-01): Confirmar venta / anticipo vs cierre — requiere sembrar una venta
confirmable completa (stock, documentos). Hoy lo cubre H-CAJA-1 (falta de guardia) + R-01 (carrera).

**Cómo correrlos (cuando FIN dé turno exclusivo):**
`qa-run.ps1 fin -- powershell -File run-conc.ps1` con `QA_RELEASE_TURNO=1`, `QA_FIN_CONC_SQL` (catálogo `qa_fin_conc_*`) y,
para repetir con RCSI, `QA_FIN_CONC_RCSI=1`. Filtro: `--filter FinResidualSqlServer`. El host de pruebas usa las
migraciones reales y se auto-elimina (`qa_fin_conc_001_res`).

## 6. Tests aislados (ejecutados: 15/15)

`TheBuryProyect.Tests/Integration/FinResidualCajaDevolucionAnticipoTests.cs` — SQLite, servicios reales vía DI,
sin concurrencia. Resultado de la corrida: `Superado: 15, Con error: 0` (junto con los 16 omitidos del harness previo).

* **`Hallazgo_*` (9):** fijan el comportamiento **defectuoso actual**; pasan hoy (= defecto confirmado) y **deben
  romperse** cuando se corrija el servicio, momento en que se invierte la aserción (cada una documenta el invariante deseado).
* **`Contrato_*` (6):** comportamiento correcto a preservar (guardia de apertura cerrada en el alta manual, segundo
  cierre rechazado, tope de cantidad devuelta, doble completar, reintento de cobro por RowVersion, reintento de cancelación).

## 7. Cierre de caja con pagos pendientes y movimientos posteriores

* **Pagos en vuelo:** el cobro es Serializable y retiene S sobre la fila de apertura; el cierre no toma lock, así que
  solo hay riesgo si el cierre **ya leyó** antes de que el cobro inserte (R-01). Con RCSI ON el cierre ni siquiera
  esperaría al cobro en vuelo (R-02).
* **Movimientos con acreditación `Pendiente`** (transferencia/tarjeta/MP): el arqueo solo cuenta efectivo, el cierre
  los ignora y `AcreditarMovimientoAsync` los acepta con la apertura cerrada (H-CAJA-3). Es decisión de negocio si
  eso es legítimo (conciliación bancaria) o si el cierre debe advertir/bloquear.
* **Reembolsos de devolución aprobada:** seguros frente al cierre — usan la apertura **activa del usuario**.
* **Reversión por cancelación:** *no* lo son — usan la apertura original (H-CAJA-2).
* **Venta/anticipo/cobro tras el cierre:** posibles solo por carrera (ventana = duración de Confirmar/cobro);
  `RegistrarMovimientoVentaAsync` además carece de la guardia que sí tiene el alta manual.

## 8. Propuesta de corrección por defecto (NO implementada; requiere reglas aprobadas)

Principio: la misma receta de QA-FIN-CONC-P0 — **un recurso común bloqueado primero y fail-closed**, y la regla
más conservadora hasta que exista la reversión auditada. La apertura va **al final** del orden global
(Crédito → Venta → Apertura), por lo que no introduce ciclos.

1. **Apertura como recurso de serialización (C-1, C-2, C-3).** Un único helper (estilo `CreditoConcurrencia`, o el
   `ExecuteUpdate … WHERE Cerrada = 0` ya usado en `CancelarVentaAsync`): todo alta de movimiento
   (venta, cuota, anticipo, devolución, manual, contramovimiento) toma el lock de la apertura **dentro de la
   transacción del flujo** y rechaza si está cerrada/vencida; `CerrarCajaAsync` pasa a transacción, toma el mismo lock
   **antes** de leer movimientos y captura `DbUpdateConcurrencyException`/violación de unicidad como error de negocio.
2. **Contramovimiento (H-CAJA-2).** *Regla a aprobar.* Por defecto: si la apertura original está cerrada, asentar la
   reversión en la apertura **activa del usuario que cancela** (con referencia a la original); si no hay, rechazar la cancelación.
3. **Devolución ↔ venta (H-DEV-1/2/3, R-D4).** Crear/Completar reclaman la venta (lock) y exigen estado devolvible;
   el tope de cantidad se evalúa bajo ese lock. Por defecto, `CancelarVentaAsync` **rechaza** si hay devoluciones
   no rechazadas/canceladas (mismo criterio que "pagos aplicados": hasta que exista reversión auditada).
4. **Anticipo (H-ANT-1/2).** Asentarlo con `VentaId`; la guardia de cancelación lo trata como pago aplicado
   (rechazo por defecto, no reversión automática); `RegistrarMovimientoAnticipoAsync` falla (fail-closed) si hay
   anticipo > 0 y no hay caja, igual que el cobro de cuota.
5. **Idempotencia (H-IDEM-1, R-I4).** `IdempotencyKey` (guid emitido al renderizar el formulario, índice único filtrado)
   en movimiento manual y alta de devolución; los cobros ya tienen RowVersion y no lo necesitan. Prioridad: manual y devolución.
6. **Facturar (R-F2).** Una transacción con claim de la venta; el ingreso de respaldo dentro de ella y condicionado a
   `Estado = Facturada`, o eliminarlo (ver si quedan ventas legadas).
7. **Decisiones de negocio previas:** acreditación posterior al cierre (H-CAJA-3), reembolso vs. lo cobrado (R-D6),
   destino de la reversión de una apertura cerrada (punto 2).

Tests de la corrección: invertir las aserciones de los 9 `Hallazgo_*`; ejecutar R-01…R-10 con y sin RCSI y exigir
0 deadlocks (orden de locks Crédito → Venta → Apertura); estrés cobro‖cierre del mismo estilo que el de 24 iteraciones del P0.

## 9. Limitaciones

* Ningún escenario con SQL Server se ejecutó: **BLOCKED**, no PASS. Todo "PASS inferido" se descarta o confirma al correr R-xx.
* Los `Hallazgo_` ejecutados prueban la **falta de guardia/regla**, no la carrera; la probabilidad real de H-CAJA-1 y H-ANT-2 depende de la ventana.
* No se evaluó fiscalmente el envío ni la facturación electrónica (fuera de alcance).
* `Venta.AperturaCajaId` se reasigna a la caja de quien factura (`FacturarVentaAsync`); no se midió su efecto en conciliación.

# QA-FIN-CONC-P0 — Integridad transaccional de pagos y cancelaciones

Rama `qa/fin-conc-p0-20261014` (base `a81e5e38` = FIN-P0 `369b77e` + auditoría de concurrencia). Sin merge ni push.
Alcance: sin tocar `TheBuryProjectDb`, sin reversión auditada, sin cambios de fórmulas financieras.

## 1. Causa raíz (confirmada)

`CancelarVentaAsync` serializaba solo contra otras cancelaciones (lock de fila de `Ventas`); los cobros
(`RegistrarPagoCuotaAsync`, `PagarCuotasAsync`) no comparten ningún recurso con ella. La guardia "no hay pagos"
corre en READ COMMITTED y no retiene locks, así que el estado que valida no está protegido hasta el commit (TOCTOU).

Lo que la ejecución real en SQL Server añadió al análisis estático:

* **La corrupción I-4 no se materializa hoy, pero por una dependencia accidental**: el cobro lee `Ventas` antes de
  confirmar (emisión de documentos) y choca con el X lock que la cancelación toma sobre la venta desde el inicio.
  Resultado medido **sin la corrección**: el cruce no termina en inconsistencia sino en **deadlock: 24 de 24
  iteraciones** (`qa-fin-conc-p0-evidencia/03-prefix.log`), con el cobro ganando y la cancelación como víctima (`DEADLOCK_VICTIMA`). El invariante
  se conservó solo porque SQL Server eligió víctima; en el escenario "pago en vuelo" la cancelación también murió por
  deadlock en vez de rechazarse con el mensaje de negocio.
* El orden de locks era opuesto (cancelación Venta→cuotas, cobro cuotas→Venta): la causa del deadlock.
* El cobro no comprobaba `Credito.Estado`; solo la cuota. Un crédito `Cancelado` con una cuota aún `Pendiente`
  (estado inconsistente o cuota recargada tras esperar un lock) aceptaba el cobro (4 tests SQLite lo demuestran, ver §4).

## 2. Corrección

`Services/CreditoConcurrencia.cs` (nuevo): lock de actualización de la **fila de `Creditos`**
(`UPDLOCK, HOLDLOCK, ROWLOCK` por PK; no-op fuera de SQL Server) + lectura de estado del crédito desde la base.

| Flujo | Cambio |
|---|---|
| `VentaService.CancelarVentaAsync` | lock del crédito **primero**, luego la venta (orden Crédito→Venta); verifica que `CreditoId` no cambió; la guardia pasa a ser exacta y se **repite antes de persistir** |
| `CreditoService.RegistrarPagoCuotaAsync` (individual, adelanto, primera cuota) | primer paso de la transacción: lock del crédito + rechazo si el crédito está Cancelado/Rechazado |
| `CreditoService.PagarCuotasAsync` | créditos resueltos **antes** de abrir la transacción (sin locks SERIALIZABLE retenidos), locks en orden ascendente de Id, mismo rechazo |
| `CreditoService.CancelarCreditoAsync` | transacción + mismo lock (misma invariante) |

`Finalizado` se deja fuera del rechazo a propósito: no hay deuda exigible y un test existente fija el contrato
("cuota ya pagada", `InvalidOperationException`). Orden global de locks: **Crédito (asc) → Venta → Cuotas/Caja**.

## 3. Aislamiento verificado (SQL Server 16.0.4295.3, catálogo descartable `qa_fin_conc_001[_svc]`)

* Cancelación: `READ COMMITTED`; cobro: `SERIALIZABLE` (leído de `sys.dm_exec_sessions` en plena compuerta).
* `READ_COMMITTED_SNAPSHOT`: OFF por defecto del servidor; la batería se repitió con **ON** (`qa-fin-conc-p0-evidencia/07-postfix-rcsi.log`; corrida final con el código definitivo: `11-final.log`, 15/16 — el único rojo es `I4` del modelo sin mitigación, por diseño).
* Con la corrección, en la compuerta se observa `KEY U Creditos` retenido por la cancelación y el cobro en
  `LCK_M_U` bloqueado por ella; no se observó ningún bloqueo contra lecturas.

## 4. Resultados

Pruebas con servicios reales (`Concurrency/CancelarVsPagoServiciosRealesSqlServerTests.cs`): un interceptor de
comandos detiene una operación en un SQL concreto (cancelación: carga del crédito+cuotas posterior a la guardia;
cobro: antes de insertar el `PagoCuota`, con cuota y caja ya escritas sin commit) y el test lanza la otra operación.

| Prueba | Sin corrección | Con corrección | Con RCSI ON |
|---|---|---|---|
| I-4 cobro intercalado tras la guardia | invariante OK, **cancelación = deadlock** | cobro bloqueado por el lock, cancelación OK, cobro rechazado ("crédito Cancelado") | PASS |
| I-4 pago múltiple | deadlock | PASS | PASS |
| Pago en vuelo → cancelación | **cancelación = deadlock** (esperado: rechazo "pagos aplicados") | cancelación bloqueada hasta el commit y luego rechazada | PASS |
| Pago antes / pago después | PASS | PASS | PASS |
| Dos cancelaciones simultáneas | PASS | PASS (1 OK, 1 rechazada, 1 contramovimiento) | PASS |
| RowVersion cuota/crédito | PASS | PASS | PASS |
| Estrés 24 cobro‖cancelación | **24 deadlocks** | 12 cobros OK / 12 cancelaciones OK / **0 deadlocks**, invariante OK | idem |

Invariante verificado en cada caso: venta y crédito se cancelan juntos; crédito Cancelado ⇒ 0 `PagoCuota` Aplicado, 0
cuotas con `MontoPagado`, 0 cobros en caja; Σ ledger = Σ `MontoPagado`; PagoCuota Aplicado ⇔ cobro en caja; ≤ 1
contramovimiento por venta y ninguno revierte un cobro de cuota ajeno.

Tests aislados (SQLite, `Integration/CreditoCobroYCancelacionSerializadosTests.cs`, 6 casos): antes de la corrección
fallaban los 4 de "cobro sobre crédito terminal" (individual ×3 estados y múltiple; el cobro se aceptaba); con ella pasan los 6.
Suite C# completa con la corrección: 5445 superadas, 0 fallos, 20 omitidas preexistentes (`qa-fin-conc-p0-evidencia/10-suite.log`).
Harness de modelo (7 tests): ejecutados por primera vez; corregido un bug propio del harness (`Scalar<int>` sobre `bit`,
nunca ejecutado). `I4` del *modelo sin mitigación* falla **por diseño** (demuestra el bug del modelo); M1/M2 (mitigados) pasan.

Limitación honesta: la ventana "pago confirma entre guardia y carga de cuotas" no pudo forzarse hasta la
corrupción en el código previo porque el cobro choca antes contra la venta; por eso la evidencia "antes" es deadlock,
no pago huérfano. El caso `Credito.Estado` terminal con cuota pendiente sí está demostrado (SQLite).

## 5. Análisis de deadlocks

* Antes: ciclo Venta(X, cancelación) ↔ Cuotas/Ventas(S, cobro): 100 % en el estrés.
* Después: ambos toman primero el lock del crédito; el segundo espera sin retener nada. 0 deadlocks en 24+ cruces,
  las 9 pruebas y RCSI ON.
* Costo: se serializan cobro y cancelación **del mismo crédito** (lock de una fila, no bloquea lectores ni otros créditos).
* Riesgo residual: flujos que escriben `Ventas` y luego `Creditos` sin el lock (p. ej. facturación) frente a un cobro
  (Crédito→Venta) no se probaron; no forman parte del cambio.

## 6. Fuera de alcance / pendiente

Cierre de caja y altas de movimiento (TOCTOU de apertura), devolución vs cancelación, idempotencia de cobros y anticipos
(ver `qa-fin-conc-auditoria-concurrencia.md` §6); reversión auditada definitiva.

## 7. Reproducción

`qa-run.ps1 fin -- powershell -File run-conc.ps1` (crea `qa_fin_conc_001`, exporta `QA_RELEASE_TURNO=1` y
`QA_FIN_CONC_SQL`, ejecuta `dotnet test --filter CancelarVsPago`, elimina las BD). Con `QA_FIN_CONC_RCSI=1` el catálogo
de servicios se crea con RCSI ON. El host usa las migraciones reales (`EnsureCreated` falla en SQL Server por cascadas).

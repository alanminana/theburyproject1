# QA-FIN-RESIDUAL — Registro de defectos financieros conocidos

> **Estado tras FIN-CAJA-4-P0: 10 hallazgos CORREGIDOS (H-DEV-1, H-DEV-2, H-DEV-3 — causa raíz CR-3, FIN-DEV-P0 —,
> H-ANT-1, H-ANT-2 — causa raíz CR-4, FIN-ANT-P0 —, H-CAJA-1, H-CAJA-2 — causa raíz CR-1, FIN-CAJA-P0 y FIN-CAJA-2-P0 —,
> H-IDEM-1 — causa raíz CR-5, FIN-IDEM-P0 —, H-CAJA-3 — causa raíz CR-2, FIN-CAJA-3-P0 — y H-CAJA-4 — deadlock cierre vs.
> cobro de cuota, FIN-CAJA-4-P0) con pruebas de regresión; no quedan hallazgos ABIERTOS en este registro. La conciliación
> auditada de pagos posteriores al cierre sigue PENDIENTE (funcionalidad separada, NO implementada).**
> Un test `Hallazgo_` en verde significa que **reprodujo un defecto vigente**, no que esté corregido.
> Los 10 escenarios históricos SQL Server (R-01 a R-10) están **EJECUTADOS y APROBADOS** contra un SQL Server efímero
> (RCSI 0 y 1) en `.github/workflows/fin-dev-sqlserver.yml`, junto con R-09B, R-10B…R-10E, R-11…R-14 y los CAJA-4-xx de
> FIN-CAJA-4-P0; el workflow exige `Passed` para cada uno (un omitido o «NO ENCONTRADO» hace fallar el job).
>
> **FIN-FACT-P0 — H-FACT-1 CORREGIDO** (hallazgo del QA histórico en SQL Server real, escenario R-10): `FacturarVentaAsync`
> commiteaba la factura y el estado de la venta sin transacción ni lock y recién después asentaba el ingreso de caja "de
> respaldo" de las ventas legadas (confirmadas antes del fix que asienta el ingreso al confirmar). Una cancelación en esa
> ventana no encontraba ingreso que contra-asentar y el ingreso posterior quedaba huérfano sobre una venta `Cancelada`
> (medido: ingresos=100, egresos=0, con RCSI 0 y 1). Ahora factura + estado + ingreso son una sola transacción bajo los
> locks Crédito → Venta → Apertura (el orden de la cancelación y del cierre); R-09, R-09B, R-10, R-10B y R-10C pasan en
> ambos modos RCSI y el workflow exige `Passed` para cada uno.
>
> **FIN-CAJA-4-P0 — H-CAJA-4 CORREGIDO** (deadlock 1205 entre el cobro de una cuota y el cierre de caja, escenario R-01; el cierre
> era la víctima en 6 de 6 ejecuciones históricas). Causa raíz confirmada con el grafo de deadlock: el cobro (`SERIALIZABLE`)
> releía «la apertura activa del usuario» dentro de su transacción, reteniendo `RangeS-S` sobre `AperturasCaja`, y luego
> convertía a `U` mientras el cierre, que ya tenía `U`, convertía a `X`. Ahora el cobro usa la apertura que el servidor ya resolvió
> antes de la transacción solo como candidata: toma el lock directo y revalida estado, vigencia y propietario bajo el lock
> (`CajaService.RegistrarMovimientoCuotaAsync(…, aperturaIdCandidata)`). R-01 pasa 12 de 12 veces (6 en RCSI 0, 6 en RCSI 1, 0
> deadlocks). Detalle, grafo, orden de locks y resultados en
> [qa-fin-sql-historico-resultado.md](qa-fin-sql-historico-resultado.md).

>
> **FIN-FACT-REFERENCIA-P0 — H-FACT-2 CORREGIDO** (defecto distinto de H-FACT-1, detectado en el QA de FIN-CAJA-4-P0): el
> fallback de `FacturarVentaAsync` para ventas legadas daba por «ya registrado» cualquier ingreso con `VentaId == null` y
> `ReferenciaId == venta.Id`. `ReferenciaId` es polimórfico (venta, cuota, crédito, devolución, orden ML…): el cobro de una
> cuota con `Cuota.Id == Venta.Id` (p. ej. 250 y 250) suprimía el ingreso propio de la venta, que quedaba `Facturada` sin su
> ingreso de caja. Ahora un movimiento solo cuenta como ingreso de la venta si es `Ingreso`, vigente, de concepto de cobro de
> venta (efectivo, tarjeta, cheque, transferencia, Mercado Pago) y: tiene `VentaId` igual a la venta, **o** (histórico sin
> `VentaId`) `ReferenciaId == venta.Id` **y** `Referencia == venta.Numero` (el formato real que graba
> `RegistrarMovimientoVentaAsync`). El criterio vive en un único lugar (`Services/IngresoVentaCaja.cs`) y lo usan la
> facturación y `RegistrarContramovimientoVentaAsync`. Pruebas: `VentaServiceFacturacionAtomicidadTests.FactRef*` (SQLite, con
> `Cuota.Id == Venta.Id` real; 18 de 32 fallan contra el código anterior) y `FinResidualSqlServerTests.FactRef01…10` (SQL Server
> efímero, RCSI 0 y 1; 7 de 10 fallan contra el código anterior, 02/05/09 son reglas que debían seguir cumpliéndose). El
> harness de R-10* ya no evita la colisión desplazando identidades. **Ambigüedad histórica no resuelta:** un ingreso histórico
> sin `VentaId` cuya `Referencia` no sea exactamente el número de la venta (formato anterior a `Referencia = ventaNumero`, si
> existiera) no se reconoce y la facturación asentaría un segundo ingreso; no se inventa una asociación ni se modifican saldos
> automáticamente. Antes de aplicarlo en producción conviene consultar si existen ingresos de venta con `VentaId IS NULL` y
> `Referencia <> Venta.Numero`.

Origen: commit `24973bfe` de la rama `qa/fin-residual-20261014` (auditoría completa en
[qa-fin-residual-auditoria.md](qa-fin-residual-auditoria.md)). Verificado contra `main` `f9e1440b`: los servicios
financieros (`CajaService`, `DevolucionService`, `VentaService`, `CreditoService`) no cambiaron respecto de la base de
esa auditoría y los 9 hallazgos seguían vigentes. **FIN-DEV-P0** (rama `fix/fin-dev-p0-devolucion-cancelacion-20261016`,
sobre `main` `c04f1b86`) corrigió H-DEV-1/2/3; **FIN-ANT-P0** (rama `fix/fin-ant-p0-anticipos-20261016`, sobre `main` `1f5c746e`) corrigió H-ANT-1/2. La evidencia original se conserva abajo.

## Cómo leer los resultados de CI

| Categoría (trait `Categoria`) | Cantidad | Qué significa "verde" |
|---|---|---|
| `DefectoConocido` (`Hallazgo_*`) | **0** | Ninguno abierto. Si reaparece uno, pasa porque fija el comportamiento defectuoso actual. |
| `Regresion` (`Regresion_*`; los 3 `Regresion_H_DEV_*`, los 2 `Regresion_H_ANT_*` más las matrices FIN-DEV-P0 y FIN-ANT-P0) | **5** hallazgos + matrices | El defecto está **CORREGIDO**: la prueba exige el comportamiento correcto y falla contra el código anterior. |
| `ContratoValido` (`Contrato_*`) | **6** | Comportamiento correcto que debe preservarse. No son bugs. |
| `SqlServerNoEjecutado` | **0** | Ninguno. Si reaparece uno, es un escenario omitido (BLOCKED) y **no** cuenta como aprobado. |
| `SqlServerRegresion` (`R01`…`R14`, `C4_*`) | **27** | Todos los escenarios SQL Server: R-01 … R-05 (cierre de caja; R-01 = H-CAJA-4), R-06 … R-08 y R-11 … R-14 (FIN-DEV-P0), R-09 … R-10E (FIN-FACT-P0) y los CAJA-4-04…10 de FIN-CAJA-4-P0. Se ejecutan solo en `fin-dev-sqlserver.yml` (SQL Server efímero, con y sin RCSI); el CI general los omite. |

Los nombres visibles en CI llevan el prefijo `DEFECTO CONOCIDO ABIERTO [H-…]`, `CONTRATO VALIDO …` o
`SQL SERVER EFIMERO [R-…]`. El test `FinResidualRegistroDefectosConocidosTests` falla si alguien quita un
hallazgo o sus metadatos sin actualizar este registro. Filtros útiles:
`dotnet test --filter "Categoria=DefectoConocido"`, `--filter "Categoria=ContratoValido"`.

## Hallazgos ABIERTOS: ninguno

Los 9 hallazgos del registro están corregidos. Lo único pendiente es una **funcionalidad**, no un defecto: la conciliación
auditada de ingresos que quedaron `Pendiente` al cerrar la caja (ver «H-CAJA-3»).

## H-CAJA-3 — CORREGIDO en FIN-CAJA-3-P0 (causa raíz CR-2)

**Regla aprobada por el dueño:** la caja **puede cerrarse con ingresos pendientes de acreditar** (transferencia, tarjeta,
Mercado Pago, etc.); esos movimientos se conservan `Pendiente` y visibles. **Después del cierre queda prohibido cambiar
directamente su estado de acreditación**, porque alteraría el saldo histórico de la apertura. No se reabre la caja, no se
modifica el cierre, no se transfiere el movimiento a otra apertura ni se registra un ajuste compensatorio.

**Corrección** (`CajaService.AcreditarMovimientoAsync`): la acreditación corre en una transacción (propia, o la ambiental del
llamador, que no confirma ni revierte) y toma el **mismo lock de apertura** que el cierre y todo alta de movimiento
(`AperturaCajaConcurrencia`, H-CAJA-1: `UPDLOCK, HOLDLOCK, ROWLOCK` sobre la fila de `AperturasCaja`, exclusión mutua con
RCSI 0 y 1). Bajo el lock relee la apertura de la base y rechaza si está cerrada; relee también el movimiento
(`ReloadAsync`, nunca confía en una entidad rastreada obsoleta) y conserva las validaciones previas (solo ingresos, solo
`Pendiente`). Mensaje: «No se puede acreditar el movimiento: pertenece a una caja ya cerrada. Se requiere una conciliación
auditada.» La regla vale al invocar el servicio directamente, sin pasar por MVC. Es el último lock de la cadena
Crédito → Venta → Apertura: no cierra ciclos. **Invariante:** si gana la acreditación, se confirma antes del cálculo del
cierre; si gana el cierre, la acreditación se rechaza sin efectos. Nunca se confirma una acreditación sobre una apertura
cerrada.

**No cambia:** el cierre con pendientes (no suma lo pendiente a lo contado), la apertura **vencida pero todavía abierta**
sigue admitiendo la acreditación (no existe regla comercial nueva), y sin migraciones ni datos históricos modificados.

**Otras rutas que escriben `EstadoAcreditacion`:** (1) alta de movimientos de venta (`RegistrarMovimientoVentaAsync`) y
(2) contramovimiento de venta (`Revertido`): ambos son altas bajo el lock de apertura (H-CAJA-1/2). (3)
`MercadoLibreOrderService.RegistrarLiquidacionAsync` fija `Acreditado` sobre un movimiento que acaba de crear con
`RegistrarMovimientoAsync` (estado inicial `null`); el saldo real ya cuenta `null` como acreditado, así que `null →
Acreditado` **no modifica el saldo** de una apertura cerrada. No se tocó (fuera de alcance, sin efecto financiero).

**Pruebas:** `Regresion_H_CAJA_3_*` (10, SQLite; fallan 3 sin el fix: caja cerrada, entidad rastreada obsoleta y
transacción ambiental) y `FinCajaSqlServerTests` CAJA-3-01…07 + CAJA-3-04B (SQL Server efímero, RCSI 0/1, compuertas
deterministas; 6 de los 8 fallan sin el fix; paso de verificación propio en `fin-dev-sqlserver.yml`).

**PENDIENTE (funcionalidad separada, NO implementada):** procedimiento de **conciliación auditada** para confirmar
bancariamente un ingreso que quedó `Pendiente` tras el cierre. Hoy esos movimientos permanecen `Pendiente` y el sistema
**no** permite conciliarlos después del cierre.

## H-IDEM-1 — CORREGIDO en FIN-IDEM-P0 (causa raíz CR-5)

`MovimientoCaja.IdempotencyKey` (`Guid?`, migración aditiva `FinIdemP0MovimientoCajaIdempotencyKey`, índice único filtrado
`[IdempotencyKey] IS NOT NULL`). El formulario `Caja/RegistrarMovimiento` emite un token nuevo en cada apertura y lo reenvía
tal cual en el POST. `CajaService.RegistrarMovimientoAsync`:

* **mismo token + mismos datos** (apertura, tipo, concepto, monto, descripción, referencia, observaciones, usuario) ⇒ devuelve
  el **mismo** movimiento, sin escribir. Vale también tras el cierre de la caja (no reabre ni agrega nada);
* **mismo token + datos distintos** ⇒ `InvalidOperationException`, sin efectos;
* **tokens distintos** ⇒ movimientos legítimos distintos;
* la comprobación se repite **dentro del lock de apertura** de H-CAJA-1 (reintento simultáneo ya confirmado) y un choque con el
  índice único se resuelve como reintento; las guardias de caja cerrada/vencida de H-CAJA-1/2 no cambian.
* sin token (llamadores automáticos, p. ej. liquidación de Mercado Libre) el comportamiento es el anterior.

Pruebas: `Regresion_H_IDEM_1_…` (SQLite), `CajaMovimientoManualIdempotenciaHttpTests` (formulario → POST doble) y
`FinIdemSqlServerTests` IDEM-00…IDEM-07 (SQL Server efímero, RCSI 0/1; paso propio en `fin-dev-sqlserver.yml`).
Alcance: no tocaba H-CAJA-3 (corregido después en FIN-CAJA-3-P0). El alta de devolución (R-I4) no se incluyó en esta PR.

## H-CAJA-2 — CORREGIDO en FIN-CAJA-2-P0 (causa raíz CR-1)

**Regla aprobada por el dueño (conservadora):** una venta no puede cancelarse cuando la cancelación requiere registrar una
reversión en la caja del ingreso original y esa caja ya está **cerrada**. Se rechaza toda la cancelación (rollback completo)
con «No se puede cancelar la venta: el ingreso original pertenece a una caja ya cerrada. Se requiere un procedimiento de
reversión auditada.» No se mueve dinero a otra caja, no se tocan cierres históricos y no se implementa devolución automática
(ese procedimiento auditado queda **pendiente**).

* `CajaService.RegistrarContramovimientoVentaAsync` identifica el ingreso original como antes (con `VentaId`, o histórico
  sin `VentaId` con `ReferenciaId == venta.Id` + `Referencia == venta.Numero`; siempre con concepto de cobro de venta) y la
  decisión definitiva se toma **bajo el lock compartido de la apertura** releyendo su estado de la base
  (`BajoLockDeAperturaAsync` ahora rechaza una apertura cerrada siempre, también en el modo `validarVigencia: false`).
  La protección vive en `CajaService`, no sólo en `VentaService`: una invocación directa tampoco puede insertar el egreso.
* Una apertura **abierta pero vencida** conserva el comportamiento comercial vigente (la reversión se sigue admitiendo);
  cambiarlo sería una regla comercial nueva y **no** se asumió.
* Venta sin ingreso de caja que revertir (presupuestada, crédito sin ingreso inmediato): `RegistrarContramovimientoVentaAsync`
  devuelve `null` como antes y la cancelación no se bloquea, aunque `Venta.AperturaCajaId` apunte a una caja cerrada.
* Apertura original inexistente o eliminada: antes se informaba éxito sin revertir el dinero (`null` silencioso); ahora la
  cancelación se rechaza (nunca éxito con efectos monetarios incompletos).
* Orden de locks intacto: Crédito → Venta → Apertura. El rechazo ocurre al final de `CancelarVentaAsync`, y su `catch`
  hace rollback de toda la transacción (stock, unidades, crédito, facturas y venta incluidos).
* Pruebas: `Regresion_H_CAJA_2_*` (7, SQLite) y `CAJA-06..CAJA-09` (SQL Server efímero, RCSI 0 y 1).

**Evidencia original preservada** (fila del registro antes de la corrección):

| H-CAJA-2 | CR-1 | `Hallazgo_CancelarVenta_ContramovimientoCaeEnAperturaYaCerrada_Y_DivergeDelCierre` | Libro de caja cerrada ≠ `CierreCaja` (egresos 100 vs 0) | La reversión se asienta en la apertura original aunque esté cerrada | Reversión en la apertura activa de quien cancela, o se rechaza. **Regla a aprobar** |

## Los 3 hallazgos CORREGIDOS en FIN-DEV-P0 (causa raíz CR-3)

Evidencia histórica original (se conserva) y su corrección. Regla comercial aprobada por el dueño: solución
**conservadora** — rechazar la operación incompatible, sin calcular reembolsos parciales ni tocar reglas contables.

| ID | Evidencia original (QA-FIN-RESIDUAL) | Corrección | Regresión |
|---|---|---|---|
| H-DEV-1 | `Hallazgo_CrearDevolucion_SobreVentaYaCancelada_SeAcepta`: `CrearDevolucionAsync` no miraba `Venta.Estado`; habilitaba H-DEV-3 | `CrearDevolucionAsync` abre la transacción y toma el lock de la venta **antes** de leer nada; rechaza si la venta está `Cancelada` (o no existe) sin insertar cabecera ni detalles | `Regresion_H_DEV_1_CrearDevolucion_SobreVentaCancelada_SeRechazaSinEfectos`; SQL Server: R-11 |
| H-DEV-2 | `Hallazgo_CancelarVenta_TrasDevolucionReembolsada_DuplicaEgresoDeCajaYStock`: egreso 150 sobre 100 ingresados; stock 13 contra 12 | `CancelarVentaAsync`, con la fila de la venta ya bloqueada y **antes** de devolver stock/anular facturas/contra-asentar, rechaza si hay devoluciones `Pendiente`, `EnRevision`, `Aprobada` o `Completada`. `Rechazada`/`Cancelada` son históricas y no bloquean (nunca produjeron stock ni caja) | `Regresion_H_DEV_2_CancelarVenta_TrasDevolucionReembolsada_SeRechazaSinDuplicarCajaNiStock`; matriz `Regresion_CancelarVenta_ConDevolucionViva_*` / `…HistoricaSinEfectos_EsPermitida`; SQL Server: R-07, R-12 |
| H-DEV-3 | `Hallazgo_CompletarDevolucionAprobada_SobreVentaCancelada_ReembolsaDeNuevo`: egreso 200 sobre 100; stock 14 contra 12 | `CompletarDevolucionAsync` toma el lock de la venta y revalida su estado **dentro** de la transacción, antes de cualquier escritura (estado de la devolución, stock, unidades, caja). Cubre también devoluciones aprobadas históricas sobre ventas ya canceladas | `Regresion_H_DEV_3_CompletarDevolucionAprobada_SobreVentaCancelada_SeRechazaSinEfectos`; `Regresion_CompletarDevolucion_ErrorDeCaja_RevierteTodo` |

### Cierre de dos casos límite de H-DEV-3 (FIN-DEV-P0, hotfix de aprobación)

La PR #77 dejó dos huecos que este hotfix cierra; no cambia ninguna otra regla:

* `AprobarDevolucionAsync` no tomaba el lock de la venta ni revalidaba su estado: una devolución `Pendiente`/`EnRevision`
  histórica sobre una venta ya `Cancelada` podía aprobarse y emitir una nota de crédito vigente. Ahora, dentro de la
  transacción y **antes** de cambiar el estado de la devolución o crear la nota, toma el lock de la venta y rechaza si
  no existe, está eliminada o está `Cancelada`.
* `CompletarDevolucionAsync` rechazaba `Cancelada` pero no el `null` de `BloquearYObtenerEstadoAsync` (venta inexistente
  o eliminada). Ahora también lo rechaza, antes de cualquier escritura.

Regresión: `Regresion_AprobarDevolucionPendiente_SobreVentaCancelada_SeRechazaSinEfectos` (NC y reembolso),
`…SobreVentaEliminada_…`, `Regresion_CompletarDevolucionAprobada_SobreVentaEliminada_…`,
`Regresion_AprobarDevolucion_Valida_Y_Reintento_NoDuplicaNotaCredito`, `Regresion_AprobarDevolucion_ErrorDeInsercion_RevierteTodo`.
SQL Server efímero (con y sin RCSI): R-13 (aprobación en vuelo vs. cancelación) y R-14 (camino `UPDLOCK` real con venta
cancelada/eliminada). El escenario histórico no es alcanzable por servicios (la cancelación ya rechaza devoluciones
vivas), por eso R-14 lo fuerza escribiendo sobre la fila de la venta.

### Estrategia de bloqueo (`Services/VentaConcurrencia.cs`)

* Recurso compartido: la **fila de `Ventas`**. Cancelar la toma con el `UPDATE … SET UpdatedAt` que ya usaba (lock
  exclusivo hasta el commit). Crear y Completar devolución la toman con `SELECT [Estado] … FROM [Ventas] WITH (UPDLOCK,
  HOLDLOCK, ROWLOCK)`, que es incompatible con ese `UPDATE` y con otro `UPDLOCK`, y bloquea también con RCSI.
* La validación (estado de la venta, devoluciones vivas, cantidades ya devueltas) se lee **bajo** el lock, no antes: un
  `AnyAsync` en READ COMMITTED no serializa nada. Con transacción ambiental, el lock vive hasta el commit de la externa.
* Orden global de locks **sin cambios**: Crédito → Venta → (stock, caja, devoluciones). Las operaciones de devolución no
  toman el crédito y toman la Venta primero, así que no introducen ciclos.
* Efecto lateral buscado: Crear devolución queda serializada por venta, lo que también cierra la carrera de R-06 (dos
  altas simultáneas que leían "ya devuelto = 0").

### Limitaciones conocidas de la corrección

* `AprobarDevolucionAsync` no revalida el estado de la venta: una devolución **Pendiente** histórica sobre una venta ya
  cancelada todavía puede aprobarse (y generar su nota de crédito). Completarla sí se rechaza. Fuera de alcance de
  FIN-DEV-P0; queda como riesgo a decidir.
* Una venta con devolución viva ya no puede cancelarse: para cancelarla hay que rechazar/cancelar la devolución
  (solo posible si no está `Completada`). No existe aún una reversión auditada de devoluciones completadas.

## Los 2 hallazgos CORREGIDOS en FIN-ANT-P0 (causa raíz CR-4)

Regla comercial aprobada por el dueño (solución **conservadora**, sin devoluciones automáticas de dinero): una venta con
un anticipo efectivamente cobrado **no se cancela automáticamente**; la devolución del anticipo requiere un procedimiento
separado y auditado, **que NO existe todavía y no forma parte de esta PR**. Si se confirma una venta a crédito con anticipo
mayor que cero, el asiento de caja es obligatorio y atómico con la confirmación.

| ID | Evidencia original (QA-FIN-RESIDUAL) | Corrección | Regresión |
|---|---|---|---|
| H-ANT-1 | `Hallazgo_CancelarVentaACredito_NoRevierteNiBloqueaPorElAnticipoCobrado`: anticipo `AnticipoCredito` con `ReferenciaId = CreditoId` y sin `VentaId`; la cancelación solo revertía `Venta*` y solo miraba `CobroCuota`: el anticipo quedaba cobrado en una venta cancelada | (1) Los anticipos **nuevos** se asientan con `VentaId` (sin migración: la columna ya existía) conservando `ReferenciaId = CreditoId`. (2) `CancelarVentaAsync`, con el crédito y la venta ya bloqueados (Crédito → Venta) y **antes** de devolver stock/anular facturas/contra-asentar, rechaza si hay un ingreso vigente de concepto `AnticipoCredito` por `VentaId == venta` **o** histórico (`VentaId == null` y `ReferenciaId == CreditoId`). Acreditaciones `Rechazado`/`Anulado`/`Revertido` no cuentan como cobradas. (3) Si el crédito lo comparten **dos o más ventas vivas**, el anticipo histórico es ambiguo: no se atribuye al azar y la cancelación también se rechaza (fail-closed, con mensaje que lo indica). La guardia se repite justo antes de persistir (defensa en profundidad) | `Regresion_H_ANT_1_CancelarVentaACredito_ConAnticipoCobrado_SeRechazaSinTocarNada` y la matriz `Regresion_H_ANT_1_*` (`VentaServiceConfirmarCreditoTests`: anticipo nuevo, histórico, ambiguo, sin anticipo, cuotas pagadas, anticipo anulado); SQL Server: ANT-02, ANT-03 |
| H-ANT-2 | `Hallazgo_RegistrarMovimientoAnticipo_SinCajaAbierta_DevuelveNullSinFallar`: `RegistrarMovimientoAnticipoAsync` devolvía `null` en silencio y `ConfirmarVentaCreditoAsync` ignoraba el resultado | `RegistrarMovimientoAnticipoAsync` lanza `InvalidOperationException` con anticipo > 0 y sin caja abierta válida (anticipo 0 sigue devolviendo `null`), y es idempotente por `VentaId` (un reintento devuelve el movimiento existente). `ConfirmarVentaCreditoAsync` pasa `venta.Id`, **verifica** el resultado y, si no quedó persistido, lanza y revierte **toda** la transacción. Además toma el lock del crédito y reclama la venta (mismo orden Crédito → Venta que la cancelación) y rechaza confirmar una venta ya `Confirmada`/`Facturada`/`Entregada`/`Cancelada` (reintento o doble clic) | `Regresion_H_ANT_2_RegistrarMovimientoAnticipo_SinCajaAbierta_LanzaErrorDeNegocio` y la matriz `Regresion_H_ANT_2_*` (`VentaServiceConfirmarCreditoTests`: un solo movimiento vinculado, anticipo 0, sin caja, fallo simulado null/excepción con rollback completo, reintento); SQL Server: ANT-01, ANT-04 |

### Compatibilidad con movimientos históricos y límites de FIN-ANT-P0

* **Sin migración.** `MovimientoCaja.VentaId` ya existía (nullable, con FK e índice). Los anticipos anteriores a esta PR
  conservan `VentaId = null`; no se reescribieron datos.
* `ReferenciaId` es un campo **polimórfico**: para `AnticipoCredito` identifica al **crédito**, nunca a la venta. No se cambió
  su significado ni los reportes que lo usan (`CajaConciliacionBuilder`).
* **No hay devolución de anticipos.** Cancelar una venta con anticipo cobrado queda **rechazado**; revertirlo requiere el
  procedimiento de devolución auditada que habrá que diseñar en una PR aparte.
* ~~**H-CAJA-1 sigue ABIERTO y afecta a esta PR.**~~ **CORREGIDO en FIN-CAJA-P0** (ver abajo). `ANT-05` se invirtió a
  regresión (el cierre espera la confirmación en vuelo e incluye el anticipo) y se agregó `ANT-05B` (cierre completado antes
  del anticipo: la confirmación entera se rechaza y revierte).

### FIN-CAJA-P0 — H-CAJA-1 CORREGIDO (lock de apertura compartido con el cierre)

**Invariante:** ningún movimiento nuevo de caja se inserta en una apertura que ya fue cerrada; el cierre calcula sus totales
con los movimientos confirmados hasta ese momento.

**Mecanismo.** `AperturaCajaConcurrencia.BloquearAsync` toma `UPDLOCK, HOLDLOCK, ROWLOCK` sobre la fila de `AperturasCaja`
(mismo estilo que `CreditoConcurrencia`). UPDLOCK fuerza lectura con lock también bajo READ_COMMITTED_SNAPSHOT y dos locks
de actualización sobre la misma fila son incompatibles: exclusión mutua con RCSI 0 y 1 (probado, ver abajo). Se retiene hasta
el commit/rollback.

* **Escritores de movimientos** (`RegistrarMovimientoAsync`, `…VentaAsync`, `…CuotaAsync`, `…AnticipoAsync`,
  `…DevolucionAsync`): `BajoLockDeAperturaAsync` abre una transacción si no hay ambiental (y confirma al final) o participa
  en la existente **sin** confirmarla; toma el lock; **relee** la apertura de la base (no del change tracker) y rechaza si no
  existe, está cerrada o vencida; recién entonces inserta.
* **`CerrarCajaAsync`:** toma el mismo lock; relee la apertura (`ReloadAsync` si ya estaba rastreada); lee los movimientos;
  calcula; crea el `CierreCaja` y marca la apertura/caja cerradas; confirma una sola vez. Dos cierres simultáneos: el segundo
  espera y ve «La caja ya está cerrada» (además de la clave única 1:1 `CierreCaja.AperturaCajaId`).
* **Orden de locks:** Crédito → Venta → **Apertura**. La apertura es el último eslabón y ningún flujo la retiene mientras
  espera Crédito o Venta; el cierre no toma Crédito ni Venta. No cierra ciclos con CONC-P0.
* **Cambio de comportamiento:** `RegistrarMovimientoVentaAsync` ahora rechaza una apertura cerrada o vencida (antes no
  validaba nada). Una carrera con el cierre que antes terminaba con el movimiento «perdido» en una caja cerrada ahora termina
  con una excepción de negocio y rollback de la operación del llamador.

**Pruebas** (`FinCajaSqlServerTests`, `FinAntSqlServerTests` ANT-05/ANT-05B, SQL Server efímero con RCSI 0 y 1, compuertas
deterministas): movimiento nuevo sobre apertura cerrada (CAJA-01), cierre completado antes del movimiento en las 5 rutas
(CAJA-02-*), movimiento en vuelo y cierre que espera e incluye (CAJA-03-*), dos cierres (CAJA-04), transacción ambiental y
rollback total (CAJA-05), contramovimiento en vuelo (CAJA-06), modo RCSI efectivo (CAJA-00).

**Límites (NO resueltos aquí):**

* ~~**H-CAJA-2** sigue ABIERTO.~~ **CORREGIDO en FIN-CAJA-2-P0**: la reversión por cancelación también rechaza una apertura
  cerrada (ver sección «H-CAJA-2»). Sigue sin validar *vigencia* (apertura abierta pero vencida): regla comercial no decidida.
* ~~**H-CAJA-3** sigue ABIERTO.~~ **CORREGIDO en FIN-CAJA-3-P0**: la acreditación toma el mismo lock de apertura y se
  rechaza si la caja está cerrada (ver sección «H-CAJA-3»).

### Causas raíz independientes

* ~~**CR-1** — Falta de guardia de «apertura abierta» al asentar (H-CAJA-1, H-CAJA-2).~~ **H-CAJA-1 CORREGIDO en FIN-CAJA-P0**; **H-CAJA-2 CORREGIDO en FIN-CAJA-2-P0**.
* ~~**CR-2** — Acreditación posterior al cierre (H-CAJA-3).~~ **CORREGIDA en FIN-CAJA-3-P0** (ver sección «H-CAJA-3»).
* ~~**CR-3** — Devolución y venta sin control cruzado (H-DEV-1, H-DEV-2, H-DEV-3).~~ **CORREGIDA en FIN-DEV-P0** (ver abajo).
* ~~**CR-4** — Anticipo (H-ANT-1, H-ANT-2).~~ **CORREGIDA en FIN-ANT-P0** (ver arriba). Queda abierta la interacción con H-CAJA-1.
* ~~**CR-5** — Idempotencia del movimiento manual (H-IDEM-1).~~ **CORREGIDA en FIN-IDEM-P0** (ver sección «H-IDEM-1»).

## Los 6 contratos válidos (no son bugs)

`Contrato_RegistrarMovimientoManual_RechazaAperturaCerrada`, `Contrato_CerrarCaja_SegundoCierreSecuencial_SeRechaza`,
`Contrato_CrearDevolucion_SecuencialNoPuedeExcederLoVendido`, `Contrato_CompletarDevolucion_Reintento_NoDuplicaReembolso`,
`Contrato_PagoMultipleDeCuotas_ReintentoConLaMismaRowVersion_SeRechazaSinDuplicar`,
`Contrato_CancelarVenta_Reintento_NoDuplicaContramovimientoNiStock`.

## Los escenarios SQL Server (todos EJECUTADOS en CI efímero, RCSI 0 y 1)

`FinResidualSqlServerTests` exige `QA_RELEASE_TURNO=1` **y** `QA_FIN_CONC_SQL` (catálogo `qa_fin_conc*`); `fin-dev-sqlserver.yml`
los define y verifica que cada uno figure `Passed` en el TRX. Resultados **medidos** (ya no inferidos):

| Escenario | Qué prueba | Resultado |
|---|---|---|
| R-01 | Cierre con el lock tomado vs. cobro de cuota que arranca después — H-CAJA-4 | **FAIL medido antes** de FIN-CAJA-4-P0 (deadlock, el cierre es la víctima); **PASS medido ahora** (RCSI 0 y 1, 12/12 repeticiones). **Ejecutado en CI** |
| R-02 | Cierre con un cobro en vuelo | **PASS medido** (RCSI 0 y 1). **Ejecutado en CI** |
| R-03 | Cancelación de venta vs. cierre con el lock tomado | **PASS medido**. **Ejecutado en CI** |
| R-04 | Dos cierres simultáneos | **PASS medido**. **Ejecutado en CI** |
| R-05 | Dos aperturas simultáneas de la misma caja | **PASS medido**. **Ejecutado en CI** |
| R-06 | Dos altas de devolución por la cantidad total | FAIL (antes de FIN-DEV-P0). **Ejecutado en `fin-dev-sqlserver.yml`** |
| R-07 | Completar devolución mientras se cancela la venta | FAIL (antes de FIN-DEV-P0). **Reescrito para la regla nueva y ejecutado en `fin-dev-sqlserver.yml`** |
| R-08 | Doble completar simultáneo | PASS. **Ejecutado en `fin-dev-sqlserver.yml`** |
| R-09 | Cancelar mientras se factura (gana Facturar: la cancelación espera el lock de la venta y anula la factura) | **PASS medido** (RCSI 0 y 1). **Ejecutado en `fin-dev-sqlserver.yml`** (FIN-FACT-P0) |
| R-09B | Facturar mientras se cancela (gana Cancelar: la facturación espera y se rechaza sin factura ni ingreso) | **PASS medido**. Nuevo, ejecutado en CI |
| R-10 | Cancelar entre la factura y su ingreso de respaldo (legado) — H-FACT-1 | **FAIL medido antes** de FIN-FACT-P0 (ingresos=100, egresos=0); **PASS medido ahora** (RCSI 0 y 1). **Ejecutado en CI** |
| R-10B | Dos facturaciones simultáneas de la misma venta legada | **PASS medido**. Nuevo, ejecutado en CI |
| R-10C | Facturar mientras se cierra la caja | **PASS medido**. Nuevo, ejecutado en CI |
| R-10D / R-10E | Facturar en transacción ambiental que falla (se confirma / no se reintenta) | **PASS medido**. Ejecutados en CI (FIN-FACT-HARDENING-P0) |
| CAJA-4-04 | Dos cobros de la misma cuota | **PASS medido** (un pago, un movimiento). FIN-CAJA-4-P0 |
| CAJA-4-05a/b | Pago de varias cuotas vs. cierre (cierre primero / cobro primero) | **PASS medido**; el rechazado no deja efectos parciales. FIN-CAJA-4-P0 |
| CAJA-4-06a/b | Adelanto de cuota vs. cierre (ambos órdenes) | **PASS medido**. FIN-CAJA-4-P0 |
| CAJA-4-08 | La apertura se cierra después de la selección previa y antes del lock | **PASS medido**: rechazo, sin elegir otra caja. FIN-CAJA-4-P0 |
| CAJA-4-09 | Apertura candidata de otro usuario | **PASS medido**: no recibe el movimiento. FIN-CAJA-4-P0 |
| CAJA-4-10 | Totales y diferencia del cierre en ambos órdenes y con faltante | **PASS medido**. FIN-CAJA-4-P0 |

## Plan: de Hallazgo_ a test de regresión

Cada PR correctiva, una por causa raíz y en este orden (de mayor a menor riesgo en dinero):

1. ~~**CR-3**~~ (devolución↔venta): **hecho en FIN-DEV-P0**. ~~**CR-4**~~ (anticipo): **hecho en FIN-ANT-P0**.
2. **CR-1** (guardia y lock de apertura al final del orden Crédito → Venta → Apertura): ~~H-CAJA-1~~ **hecho en FIN-CAJA-P0**; ~~H-CAJA-2~~ **hecho en FIN-CAJA-2-P0**.
3. **CR-5** (idempotencia manual) y ~~R-10 (Facturar, H-FACT-1)~~ **hecho en FIN-FACT-P0**; ~~H-CAJA-4 (deadlock cobro de cuota vs. cierre)~~ **hecho en FIN-CAJA-4-P0**.
4. ~~**CR-2**~~ tras la decisión de negocio: **hecho en FIN-CAJA-3-P0**.

En cada una: invertir las aserciones del `Hallazgo_`, quitar el trait `DefectoConocido`, renombrarlo a `Regresion_*`,
actualizar las constantes de `FinResidualRegistroDefectosConocidosTests`, y ejecutar los R-xx correspondientes en un
SQL Server efímero (con y sin RCSI) exigiendo cero deadlocks.

## Preguntas abiertas para el dueño del negocio

1. ~~**H-CAJA-2**~~ — **RESUELTA por el dueño: se rechaza cancelar cuando la reversión cae en una caja cerrada** (implementado en FIN-CAJA-2-P0). Pendiente: diseñar el procedimiento de reversión auditada; y decidir si una apertura abierta pero vencida admite reversión.
2. ~~**H-DEV-2**~~ — **RESUELTA por el dueño: se rechaza cancelar con devoluciones vivas** (implementado en FIN-DEV-P0).
3. ~~**H-ANT-1**~~ — **RESUELTA por el dueño: se rechaza cancelar con anticipo cobrado** (implementado en FIN-ANT-P0). Pendiente: diseñar el procedimiento de devolución auditada del anticipo.
4. ~~**H-CAJA-3**~~ — **RESUELTA por el dueño: el cierre se permite con pendientes; la acreditación directa posterior al cierre se rechaza** (implementado en FIN-CAJA-3-P0). Pendiente: diseñar el procedimiento de conciliación auditada.

## Hallazgos fuera de alcance, sin corregir

* **Fallback de `FacturarVentaAsync` y `ReferenciaId` polimórfico** (descubierto en FIN-CAJA-4-P0): el fallback de ventas legadas da por
  «ya registrado» cualquier ingreso `VentaId == null` con `ReferenciaId == venta.Id`, sin filtrar por concepto; un cobro de cuota con
  `cuotaId == ventaId` suprime el ingreso de respaldo. Acotado a ventas legadas. Ver
  [qa-fin-sql-historico-resultado.md](qa-fin-sql-historico-resultado.md).

## Limitaciones

* Los `Hallazgo_` prueban la **falta de guardia o regla**, no la carrera. La probabilidad real de H-CAJA-1 depende de la ventana de concurrencia (ver `ANT-05` para la interacción con anticipos).
* Los E2E de concurrencia sin integrar (`e2e-tx/financiero/financiero-concurrencia.spec.js`) están acoplados a un stack local persistente y cubren invariantes de CONC-P0, no estos hallazgos; quedan fuera de esta evidencia.

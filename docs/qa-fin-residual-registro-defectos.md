# QA-FIN-RESIDUAL — Registro de defectos financieros conocidos

> **Estado tras FIN-DEV-P0: 3 hallazgos CORREGIDOS (H-DEV-1, H-DEV-2, H-DEV-3, causa raíz CR-3) con pruebas de
> regresión, y 6 hallazgos todavía ABIERTOS.**
> Un test `Hallazgo_` en verde significa que **reprodujo un defecto vigente**, no que esté corregido.
> De los 10 escenarios SQL Server (R-01 a R-10), **R-06, R-07 y R-08 se ejecutan ahora contra un SQL Server efímero**
> en `.github/workflows/fin-dev-sqlserver.yml` (más R-11, R-12, R-13 y R-14, nuevos); los otros 7 siguen **NO EJECUTADOS /
> BLOQUEADOS**: omitirlos no equivale a PASS.

Origen: commit `24973bfe` de la rama `qa/fin-residual-20261014` (auditoría completa en
[qa-fin-residual-auditoria.md](qa-fin-residual-auditoria.md)). Verificado contra `main` `f9e1440b`: los servicios
financieros (`CajaService`, `DevolucionService`, `VentaService`, `CreditoService`) no cambiaron respecto de la base de
esa auditoría y los 9 hallazgos seguían vigentes. **FIN-DEV-P0** (rama `fix/fin-dev-p0-devolucion-cancelacion-20261016`,
sobre `main` `c04f1b86`) corrigió H-DEV-1/2/3; la evidencia original de los tres se conserva abajo.

## Cómo leer los resultados de CI

| Categoría (trait `Categoria`) | Cantidad | Qué significa "verde" |
|---|---|---|
| `DefectoConocido` (`Hallazgo_*`) | **6** | El defecto **sigue ABIERTO**. Pasa porque fija el comportamiento defectuoso actual. |
| `Regresion` (`Regresion_*`; los 3 `Regresion_H_DEV_*` más la matriz FIN-DEV-P0) | **3** hallazgos + matriz | El defecto está **CORREGIDO**: la prueba exige el comportamiento correcto y falla contra el código anterior. |
| `ContratoValido` (`Contrato_*`) | **6** | Comportamiento correcto que debe preservarse. No son bugs. |
| `SqlServerNoEjecutado` (`R01`..`R10`) | **10** | Omitidos (BLOCKED) en el CI general, que no tiene SQL Server. **R-06/R-07/R-08 sí se ejecutan** en `fin-dev-sqlserver.yml`; los otros 7 **no se ejecutaron nunca**. |
| `SqlServerRegresion` (`R11`..`R14`) | **4** | Nuevos de FIN-DEV-P0: se ejecutan solo en `fin-dev-sqlserver.yml` (SQL Server efímero, con y sin RCSI). |

Los nombres visibles en CI llevan el prefijo `DEFECTO CONOCIDO ABIERTO [H-…]`, `CONTRATO VALIDO …` o
`NO EJECUTADO / BLOQUEADO [R-…]`. El test `FinResidualRegistroDefectosConocidosTests` falla si alguien quita un
hallazgo o sus metadatos sin actualizar este registro. Filtros útiles:
`dotnet test --filter "Categoria=DefectoConocido"`, `--filter "Categoria=ContratoValido"`.

## Los 6 hallazgos ABIERTOS (PR correctiva: PENDIENTE)

| ID | Causa raíz | Test (`FinResidualCajaDevolucionAnticipoTests`) | Impacto (dinero / caja / stock) | Comportamiento defectuoso que reproduce | Comportamiento esperado de la futura regresión |
|---|---|---|---|---|---|
| H-CAJA-1 | CR-1 | `Hallazgo_RegistrarMovimientoVenta_AsientaIngresoEnAperturaCerrada` | Ingreso posterior al cierre; el arqueo no refleja el libro | `RegistrarMovimientoVentaAsync` no valida `Cerrada` | Todo asiento exige apertura abierta y vigente bajo el mismo lock que el cierre |
| H-CAJA-2 | CR-1 | `Hallazgo_CancelarVenta_ContramovimientoCaeEnAperturaYaCerrada_Y_DivergeDelCierre` | Libro de caja cerrada ≠ `CierreCaja` (egresos 100 vs 0) | La reversión se asienta en la apertura original aunque esté cerrada | Reversión en la apertura activa de quien cancela, o se rechaza. **Regla a aprobar** |
| H-CAJA-3 | CR-2 | `Hallazgo_CierreConMovimientoPendienteDeAcreditar_YAcreditacionPosteriorCambiaElSaldoRealDeLaApertura` | Saldo real de una apertura cerrada cambia tras el arqueo (+70) | El cierre ignora pendientes y se pueden acreditar después | **A definir por negocio**: advertir/bloquear o permitir auditado |
| H-ANT-1 | CR-4 | `Hallazgo_CancelarVentaACredito_NoRevierteNiBloqueaPorElAnticipoCobrado` | **Dinero/caja**: anticipo queda en caja sin devolución | Anticipo sin `VentaId`; la cancelación no lo ve | Anticipo con `VentaId`; rechazo o reversión. **Regla a aprobar** |
| H-ANT-2 | CR-4 | `Hallazgo_RegistrarMovimientoAnticipo_SinCajaAbierta_DevuelveNullSinFallar` | Venta a crédito confirmada sin ingreso del anticipo | Devuelve `null` en silencio | Falla (fail-closed) con anticipo > 0 y sin caja |
| H-IDEM-1 | CR-5 | `Hallazgo_RegistrarMovimientoManual_ReintentoIdenticoDuplicaElAsiento` | Asiento duplicado (egreso 50 en vez de 25) | `MovimientoCaja` sin clave de idempotencia | Mismo token ⇒ un solo asiento |

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

### Causas raíz independientes

* **CR-1** — Falta de guardia de «apertura abierta» al asentar (H-CAJA-1, H-CAJA-2).
* **CR-2** — Acreditación posterior al cierre (H-CAJA-3). Decisión de negocio.
* ~~**CR-3** — Devolución y venta sin control cruzado (H-DEV-1, H-DEV-2, H-DEV-3).~~ **CORREGIDA en FIN-DEV-P0** (ver abajo).
* **CR-4** — Anticipo (H-ANT-1, H-ANT-2).
* **CR-5** — Idempotencia del movimiento manual (H-IDEM-1).

## Los 6 contratos válidos (no son bugs)

`Contrato_RegistrarMovimientoManual_RechazaAperturaCerrada`, `Contrato_CerrarCaja_SegundoCierreSecuencial_SeRechaza`,
`Contrato_CrearDevolucion_SecuencialNoPuedeExcederLoVendido`, `Contrato_CompletarDevolucion_Reintento_NoDuplicaReembolso`,
`Contrato_PagoMultipleDeCuotas_ReintentoConLaMismaRowVersion_SeRechazaSinDuplicar`,
`Contrato_CancelarVenta_Reintento_NoDuplicaContramovimientoNiStock`.

## Los 10 escenarios SQL Server (R-06/R-07/R-08 ejecutados en CI efímero; los otros 7 NO EJECUTADOS)

`FinResidualSqlServerTests` exige `QA_RELEASE_TURNO=1` **y** `QA_FIN_CONC_SQL` (catálogo `qa_fin_conc*`). Ninguno de
los workflows actuales los define, así que **nunca se ejecutaron**. El resultado esperado es una **inferencia de la
lectura del código**, no un resultado medido.

| Escenario | Qué prueba | Resultado esperado (inferido, no medido) |
|---|---|---|
| R-01 | Cobro entra entre la lectura del cierre y su commit | FAIL |
| R-02 | Cierre con un cobro en vuelo | Depende de RCSI |
| R-03 | Cancelación entre la lectura del cierre y su commit | FAIL |
| R-04 | Dos cierres simultáneos | PASS |
| R-05 | Dos aperturas simultáneas de la misma caja | PASS |
| R-06 | Dos altas de devolución por la cantidad total | FAIL (antes de FIN-DEV-P0). **Ejecutado en `fin-dev-sqlserver.yml`** |
| R-07 | Completar devolución mientras se cancela la venta | FAIL (antes de FIN-DEV-P0). **Reescrito para la regla nueva y ejecutado en `fin-dev-sqlserver.yml`** |
| R-08 | Doble completar simultáneo | PASS. **Ejecutado en `fin-dev-sqlserver.yml`** |
| R-09 | Cancelar mientras se factura | PASS |
| R-10 | Cancelar entre el commit de Facturar y su ingreso de respaldo (legado) | FAIL |

## Plan: de Hallazgo_ a test de regresión

Cada PR correctiva, una por causa raíz y en este orden (de mayor a menor riesgo en dinero):

1. ~~**CR-3**~~ (devolución↔venta): **hecho en FIN-DEV-P0**. Queda **CR-4** (anticipo): H-ANT-1/2.
2. **CR-1** (guardia y lock de apertura al final del orden Crédito → Venta → Apertura): H-CAJA-1/2.
3. **CR-5** (idempotencia manual) y R-10 (Facturar).
4. **CR-2** solo tras decisión de negocio.

En cada una: invertir las aserciones del `Hallazgo_`, quitar el trait `DefectoConocido`, renombrarlo a `Regresion_*`,
actualizar las constantes de `FinResidualRegistroDefectosConocidosTests`, y ejecutar los R-xx correspondientes en un
SQL Server efímero (con y sin RCSI) exigiendo cero deadlocks.

## Preguntas abiertas para el dueño del negocio

1. **H-CAJA-2**: al cancelar una venta de una caja ya cerrada, ¿la reversión va en la caja activa de quien cancela, o se rechaza?
2. ~~**H-DEV-2**~~ — **RESUELTA por el dueño: se rechaza cancelar con devoluciones vivas** (implementado en FIN-DEV-P0).
3. **H-ANT-1**: ¿se rechaza cancelar con anticipo cobrado, o se revierte el anticipo automáticamente?
4. **H-CAJA-3**: ¿se puede acreditar una transferencia/tarjeta después del cierre, o el cierre debe advertir o bloquear?

## Limitaciones

* Los `Hallazgo_` prueban la **falta de guardia o regla**, no la carrera. La probabilidad real de H-CAJA-1 y H-ANT-2 depende de la ventana de concurrencia.
* Los E2E de concurrencia sin integrar (`e2e-tx/financiero/financiero-concurrencia.spec.js`) están acoplados a un stack local persistente y cubren invariantes de CONC-P0, no estos hallazgos; quedan fuera de esta evidencia.

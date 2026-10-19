# QA-FIN-SQL-HIST-P0 — Ejecución de los escenarios SQL Server históricos R-01 … R-10

> **Estado: H-CAJA-4 CORREGIDO (FIN-CAJA-4-P0) y H-FACT-1 CORREGIDO (FIN-FACT-P0).** Este documento conserva la medición
> histórica (QA-FIN-SQL-HIST-P0, rama `qa/fin-sql-historico-r01-r10-20261017`, commit `efc12349`) y agrega el resultado
> posterior a la corrección de H-CAJA-4 (sección «FIN-CAJA-4-P0: corrección y verificación»). De los siete escenarios
> históricos pendientes (R-01, R-02, R-03, R-04, R-05, R-09, R-10) **cinco pasaban** (R-02, R-03, R-04, R-05, R-09) y
> **dos revelaron defectos reales de producción**: **R-01 → H-CAJA-4** (deadlock real entre el cierre de caja y el
> cobro de una cuota; corregido por FIN-CAJA-4-P0) y **R-10 → H-FACT-1** (ingreso de caja huérfano; corregido por
> FIN-FACT-P0, PR #85/#86). Las tablas «medido» de las secciones históricas describen el código **anterior** a esas correcciones.

Los resultados `PASS/FAIL` de [qa-fin-residual-auditoria.md](qa-fin-residual-auditoria.md) eran **inferencias** de la
lectura del código, anteriores a las correcciones FIN-DEV/ANT/CAJA/IDEM. Este documento contiene **únicamente lo medido**.

## Entorno y método

* SQL Server 2022 (RTM-CU27, 16.0.4295.3) en un contenedor Docker efímero, credenciales sintéticas, puerto solo en
  `127.0.0.1`. Catálogos de QA `qa_fin_conc*` (guards de `ConcEnv`/`ConcSqlHostFixture`); `QA_RELEASE_TURNO=1`.
* Dos pasadas completas: `QA_FIN_CONC_RCSI=0` (`READ_COMMITTED_SNAPSHOT OFF`) y `=1` (`ON`). El fixture **verifica en
  `sys.databases`** que el catálogo quedó en el modo pedido y el test lo imprime (`[modo SQL] READ_COMMITTED_SNAPSHOT=…`);
  si no coincide, el fixture falla.
* Servicios reales (`CajaService`, `VentaService`, `CreditoService`, `DevolucionService`) y la misma compuerta
  determinista (`ConcGateInterceptor`).

## Qué estaba mal en el harness (clase A: problema de coordinación, no de producción)

Tras las correcciones de locks (`AperturaCajaConcurrencia`, `VentaConcurrencia`), una operación que arranca mientras otra
retiene el lock **debe quedar esperando**. Los tests R-01, R-03, R-04 (y R-06, que ya corría en CI) hacían
`await segunda` **antes** de liberar la compuerta de la primera: la segunda esperaba el lock hasta el `CommandTimeout`
de 30 s, el test seguía y la aserción pasaba **vacía** (`SqlException` por timeout contado como "rechazada").
Medido en la corrida original: R-01, R-03, R-04 y R-06 figuraban "Correctas" tras 30 s de timeout.

Cambios (ver `FinResidualSqlServerTests.cs`):

* La segunda operación se lanza **sin esperarla**, se observa que **queda bloqueada** (`QuedaBloqueadaAsync`), la
  compuerta se libera **siempre en un `finally`** y recién entonces se esperan ambos resultados con tope de 60 s
  (`ResultadoAsync`; una operación colgada falla el test).
* `Clasificar` distingue `DEADLOCK_VICTIMA` (1205), `TIMEOUT_SQL` (-2) y `UNIQUE_VIOLADO`; `NoEsFalloDeInfraestructura`
  los rechaza: **un timeout, un deadlock o una compuerta no alcanzada nunca son PASS**.
* Cada test ahora exige, además del invariante de caja (`I-CAJA`), el **bloqueo observado**, el **motivo del rechazo**
  (mensaje de negocio, no excepción de infraestructura) y la **ausencia de efectos parciales** (cuota, ledger,
  movimientos, stock, estado de venta, saldo del crédito, cantidad de cierres/aperturas abiertas).
* R-02, R-05 y R-09 se endurecieron (el cierre incorpora exactamente el cobro; la perdedora de R-05 es
  `DbUpdateConcurrencyException` y la caja queda `Abierta`; R-09 no deja ninguna factura ni ingreso sin contra-asentar).
* No se quitó ninguna aserción sustantiva. R-06, R-07 y R-08 conservan su nombre y su lugar en CI; solo R-06 cambió su
  coordinación por el defecto descrito (R-07 y R-08 ya observaban el bloqueo).

**Demostración de que el harness sigue detectando violaciones reales** (mutación temporal en el worktree, revertida con
`git checkout`, no commiteada): se anuló el `UPDLOCK` de `AperturaCajaConcurrencia.BloquearAsync` y el de
`VentaConcurrencia`. Con esa mutación fallan R-01, R-03, R-04 y R-06 (RCSI 0) por "no esperó el lock", y R-02 (RCSI 1)
por el invariante (`Ingresos en efectivo del libro (300,00) ≠ CierreCaja.TotalIngresosSistema`). Con RCSI 0, R-02 sigue
pasando bajo la mutación porque el `SELECT` del cierre espera el X-lock del insert del cobro en vuelo: es exactamente
la diferencia RCSI 0/1 que el escenario original describía.

## Resultado medido por escenario

| ID | Escenario | RCSI 0 | RCSI 1 | Observación medida |
|---|---|---|---|---|
| R-01 | Cobro concurrente con cierre de caja | **FAIL** (históricamente; **PASS tras FIN-CAJA-4-P0**) | **FAIL** (históricamente; **PASS tras FIN-CAJA-4-P0**) | **Deadlock SQL real (H-CAJA-4)**: el cierre es la víctima (1205), el cobro confirma. Sin corrupción de saldos. Ver abajo. |
| R-02 | Cierre con un cobro en vuelo | PASS | PASS | El cierre queda bloqueado, espera, **incorpora** el cobro (ingresos del cierre = 300), 1 cierre. |
| R-03 | Cancelación de venta concurrente con cierre | PASS | PASS | La cancelación queda bloqueada; al cerrarse, se rechaza con "caja ya cerrada" y revierte **toda** su transacción (venta Confirmada, stock 10, sin egreso). |
| R-04 | Dos cierres simultáneos | PASS | PASS | El segundo queda bloqueado y luego "La caja ya está cerrada"; exactamente 1 `CierreCaja`; caja `Cerrada`. |
| R-05 | Dos aperturas simultáneas de la misma caja | PASS | PASS | Gana la segunda; la primera cae con `DbUpdateConcurrencyException` (RowVersion de `Caja`); 1 apertura abierta, caja `Abierta`. |
| R-06 | (CI) Dos altas de devolución por el total | PASS | PASS | La segunda queda bloqueada y luego "La cantidad a devolver excede lo disponible"; devuelto = 2 de 2 vendidos. |
| R-07 | (CI) Completar devolución vs cancelar | PASS | PASS | Sin cambios (ya observaba el bloqueo). |
| R-08 | (CI) Doble completar | PASS | PASS | Sin cambios. |
| R-09 | Facturación concurrente con cancelación | PASS | PASS | Facturar pierde por `RowVersion` de la venta: 0 facturas (ni siquiera anuladas), venta `Cancelada`, ingreso contra-asentado (100/100). |
| R-10 | Cancelación durante el ingreso de respaldo de una factura legada | **FAIL** (históricamente; **PASS tras FIN-FACT-P0**) | **FAIL** (históricamente; **PASS tras FIN-FACT-P0**) | **Defecto real (H-FACT-1)**: venta `Cancelada` con ingreso 100 y egreso 0. Ver abajo. |

R-11 … R-14 (FIN-DEV-P0) también pasan en ambos modos.

## H-CAJA-4 — deadlock entre el cierre de caja y el cobro de una cuota (R-01)

**Secuencia reproducible.** El cierre toma el lock de la apertura (`UPDLOCK, HOLDLOCK` sobre `AperturasCaja`), lee los
movimientos y se detiene justo antes de `INSERT CierresCaja`. Un cobro de cuota de esa apertura arranca.

**Medido (6 de 6 ejecuciones: 4 con RCSI 0 y 2 con RCSI 1; el grafo de deadlock es idéntico en ambos modos):** el cobro queda esperando; al liberar, SQL Server detecta un deadlock y
elige al **cierre** como víctima (`InvalidOperationException` con `SqlException 1205`). El cobro confirma en la apertura
que sigue abierta (cuota `Pagada/300`, 1 `PagoCuota`, 1 cobro en caja, saldo del crédito 600), `Cerrada=false`,
0 `CierresCaja`. El invariante `I-CAJA` **se conserva** (ningún movimiento queda fuera del cierre; no hay doble asiento ni
cambio parcial), pero **el cierre falla con un error de deadlock sin reintento** y la caja no se cierra. Si la víctima
fuera el cobro, fallaría el cobro.

**Causa raíz (grafo de deadlock, trace flag 1222; `deadlock-r01-ambos-modos.txt`):** ambas sesiones sobre la **misma clave**
`PK_AperturasCaja`:

| Sesión | Posee | Pide |
|---|---|---|
| Cobro de cuota (transacción `SERIALIZABLE`, `CreditoService.RegistrarPagoCuotaAsync`) | `RangeS-S` (por leer la apertura sin hint en `CajaService.RegistrarMovimientoCuotaAsync` → `ObtenerAperturaActivaParaUsuarioAsync`, retenido por `SERIALIZABLE` hasta el commit) | convertir a `U` (`BajoLockDeAperturaAsync` → `SELECT … WITH (UPDLOCK, HOLDLOCK)`) |
| Cierre (`CajaService.CerrarCajaAsync`) | `U` (su lock de apertura) | convertir a `X` (`UPDATE AperturasCaja SET Cerrada = 1`) |

Es un **deadlock de conversión de locks**: el cobro lee la apertura **antes** de tomar el lock exclusivo-de-actualización
y, bajo `SERIALIZABLE`, esa lectura retiene un `RangeS-S` que impide al cierre pasar a `X`. `SERIALIZABLE` ignora
`READ_COMMITTED_SNAPSHOT`, por eso es idéntico con RCSI 0 y 1. No lo detectaban `CAJA-02-CUOTA` (el cierre **termina antes**
de que arranque el cobro) ni `CAJA-03-CUOTA` (el cobro toma el lock **primero**).

**Alcance.** La ventana es la duración de la transacción de cierre (milisegundos) con un cobro que arranca en ese momento
en la misma apertura. Probablemente afecta a cualquier camino que lea la apertura dentro de una transacción
`SERIALIZABLE`/`REPEATABLE READ` antes de `BloquearAsync` (cobro múltiple, línea 1575 de `CreditoService`): **a verificar**
en la corrección.

**Corrección sugerida en ese momento (aplicada después, ver la sección de FIN-CAJA-4-P0):** tomar el lock de la apertura **antes** de leerla (resolver
el `Id` de la apertura con un `SELECT … WITH (UPDLOCK, HOLDLOCK)` en una sola sentencia, o leer la apertura con `UPDLOCK`)
o devolver el control a un reintento acotado ante `1205`. El test `R01_…` ya afirma el comportamiento correcto
(cobro bloqueado → rechazado por caja cerrada, sin efectos parciales, sin deadlock) y pasará cuando se corrija.

## H-FACT-1 — ingreso de caja huérfano en una venta cancelada (R-10)

**Secuencia reproducible (venta legada `Confirmada` sin ingreso asentado).** `VentaService.FacturarVentaAsync` hace
`SaveChanges` (venta `Facturada` + factura) **sin transacción ni lock de la venta** y, después, asienta el ingreso "de
respaldo" (`RegistrarMovimientoVentaAsync`). Detenido justo antes de ese `INSERT INTO MovimientosCaja`, la cancelación
completa: anula la factura, devuelve stock, pasa la venta a `Cancelada` y **no encuentra ingreso que contra-asentar**
(`Venta … no tiene MovimientoCaja de ingreso`). Al liberar, Facturar asienta un ingreso de $100.

**Medido (RCSI 0 y 1):** `cancelación=OK`, `facturar=OK`, venta `Cancelada`, **ingresos = 100, egresos = 0**. La caja
queda con un ingreso por una venta que se canceló (dinero que el cierre contará pero que no existe). Invariante
violado: venta `Cancelada` ⇒ Σ egresos = Σ ingresos.

**Alcance.** Solo ventas legadas confirmadas antes del fix que asienta el ingreso al confirmar (las nuevas ya traen su
ingreso y Facturar no lo repite), y solo si Facturar y Cancelar se pisan en milisegundos. Baja frecuencia, impacto
monetario real. **Corrección sugerida (no aplicada):** asentar el ingreso de respaldo dentro de la misma transacción que
el cambio de estado y bajo el lock de la venta (`VentaConcurrencia`), o revalidar el estado de la venta bajo lock antes
de asentarlo. El test `R10_…` afirma el invariante correcto y por eso está en rojo.

## FIN-CAJA-4-P0: corrección y verificación (H-CAJA-4)

Rama `fix/fin-caja-4-p0-deadlock-cobro-cierre-20261018` sobre `origin/main` `e8196205`. Esta sección es **posterior** a la
medición histórica de arriba.

### Causa raíz confirmada con el grafo de deadlock real

Grafo (`deadlock-r01-ambos-modos.txt`, trace flag 1222; mismo recurso `KEY PK_AperturasCaja` en RCSI 0 y 1):

| Sesión | Comando | Locks sobre la fila de la apertura |
|---|---|---|
| Cobro de cuota (`SERIALIZABLE`) | `SELECT [Id] FROM [AperturasCaja] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = @p0` (`BajoLockDeAperturaAsync`) | **posee** `RangeS-S`, **pide** convertir a `U` |
| Cierre de caja | `UPDATE [AperturasCaja] SET [Cerrada] = …` | **posee** `U`, **pide** convertir a `X` |

El cierre es la víctima (1205). El `RangeS-S` del cobro no viene de su `UPDLOCK` sino de una lectura **anterior** en la misma
transacción: `CreditoService` resolvía la caja antes de abrirla (`ObtenerAperturaActivaParaUsuarioAsync`) y, **ya dentro de
`SERIALIZABLE`**, `CajaService.RegistrarMovimientoCuotaAsync` volvía a ejecutar esa misma consulta (sin hints) para elegir la
apertura. Bajo `SERIALIZABLE` esa lectura retiene un lock de rango compartido hasta el commit, y `SERIALIZABLE` ignora
`READ_COMMITTED_SNAPSHOT`: por eso el ciclo es idéntico en RCSI 0 y 1.

Orden de formación del ciclo: (1) el cobro lee la apertura (`RangeS-S`); (2) el cierre toma `U` y calcula; (3) el cobro pide `U`
(espera: el cierre lo tiene); (4) el cierre pide `X` para marcar `Cerrada` (espera: el cobro tiene `RangeS-S`) ⇒ ciclo.

Por qué no se detectaba: `CAJA-02-CUOTA` hace que el cierre **termine antes** de que arranque el cobro (no hay solapamiento) y
`CAJA-03-CUOTA` / R-02 hacen que el cobro tome el lock **primero** (el cierre espera sin retener nada). Solo el orden «el cierre
ya tiene `U` y el cobro arranca después» fuerza la conversión cruzada. Además, la coordinación vieja de R-01 esperaba a la segunda
operación antes de liberar la compuerta y «pasaba» por timeout (ver la sección del harness).

### Solución

* `CajaService.RegistrarMovimientoCuotaAsync` gana una sobrecarga con `aperturaIdCandidata` (con implementación por defecto en la
  interfaz que delega en la anterior, por lo que los ~30 stubs de test no cambian). Con candidata **no se vuelve a consultar la
  apertura**: se toma directamente el `UPDLOCK, HOLDLOCK` de esa fila y recién bajo el lock se relee la apertura **de la base**
  (nunca del change tracker) y se exige que siga abierta, vigente y **que pertenezca al usuario que cobra**. Si no, se lanza
  `InvalidOperationException` y la transacción del cobro se deshace completa.
* `CreditoService.RegistrarPagoCuotaAsync` (pago individual, primera cuota y adelanto) y `PagarCuotasAsync` (pago múltiple) pasan
  `cajaActiva.Id`: el id que **el servidor** ya resolvió antes de abrir la transacción a partir del usuario autenticado. Nada viene
  del navegador, y la candidata no autoriza nada por sí sola.
* Sin retries, sin cambiar aislamiento ni hints, sin `NOLOCK/READPAST`, sin timeouts mayores.

Orden de adquisición de locks de los flujos de cobro:

| | Antes | Después |
|---|---|---|
| Cobro | lectura previa de la apertura (`RangeS-S` retenido) → Crédito `U` → … → apertura `U` | Crédito `U` → … → apertura `U` (sin lectura previa) |
| Cierre | apertura `U` → `X` | sin cambios |
| Cancelación de venta/crédito | Crédito → Venta → Apertura | sin cambios |

El orden global **Crédito → Venta → Apertura** se conserva; lo único que desaparece es la lectura compartida retenida *antes* del
lock de la apertura.

Revisión de caminos hermanos: solo los cobros de cuota (`CreditoService`) y `PunitorioService` abren transacciones
`SERIALIZABLE`; el resto (`VentaService`, anticipos, Mercado Libre, acreditaciones, devoluciones) lee la apertura bajo
`READ COMMITTED`, donde el lock compartido se libera al terminar la lectura y no puede cerrar este ciclo. El anticipo
(`RegistrarMovimientoAnticipoAsync`) conserva su lectura previa por esa razón. La sobrecarga sin candidata se mantiene por
compatibilidad: **un llamador nuevo que abra `SERIALIZABLE` y use esa sobrecarga reintroduciría el patrón** (riesgo residual).

### Resultado medido (SQL Server 2022 efímero, `sys.databases` verificado en cada corrida)

| Escenario | Código anterior | Corregido RCSI 0 | Corregido RCSI 1 |
|---|---|---|---|
| **R-01 / CAJA-4-01·02** cierre primero, el cobro espera y se rechaza | **FAIL: deadlock, cierre víctima** (reproducido en esta rama: `cierre=DEADLOCK_VICTIMA`, `cobro=OK(True)`) | PASS | PASS |
| R-02 / CAJA-4-03 cobro primero, el cierre espera e incorpora | PASS | PASS | PASS |
| CAJA-4-04 dos cobros de la misma cuota | PASS | PASS | PASS |
| CAJA-4-05a/b pago de varias cuotas vs cierre (ambos órdenes) | **FAIL** (05a: el cierre no llega a confirmarse) | PASS | PASS |
| CAJA-4-06a/b adelanto vs cierre (ambos órdenes) | **FAIL** (06a: el cierre no llega a confirmarse) | PASS | PASS |
| CAJA-4-07 cobro vs cancelación (`CancelarVsPagoServiciosRealesSqlServerTests`) | PASS | PASS | PASS |
| CAJA-4-08 la apertura se cierra tras la selección previa y antes del lock | el cobro **se imputó a otra caja abierta del mismo usuario** | PASS (rechazo; ninguna caja alternativa) | PASS |
| CAJA-4-09 una caja de otro usuario no recibe el movimiento | n/a (API nueva) | PASS | PASS |
| CAJA-4-10 totales y diferencia del cierre (cobro primero / cierre primero / faltante −50) | **FAIL** (deadlock) | PASS | PASS |
| R-03, R-04, R-05 | PASS | PASS | PASS |

Repeticiones de R-01 con la corrección, cada una en un proceso `dotnet test` nuevo (fixture, host y conexiones propios, catálogo
recreado): **6 en RCSI 0 y 6 en RCSI 1 = 12 de 12 PASS, 0 deadlocks** (el código anterior falló 6 de 6 en la medición histórica y 1 de 1
al reproducirlo en esta rama). En cada una el cobro quedó bloqueado por el lock del cierre, el cierre confirmó (apertura `Cerrada`, 1
`CierreCaja`, caja `Cerrada`) y el cobro rechazado no dejó rastros: cuota `Pendiente/0`, 0 `PagoCuota`, 0 movimientos, saldo del crédito
900 (intacto), invariante `I-CAJA` sin violaciones.

Los tests detectan el defecto: revirtiendo **solo** las dos llamadas de `CreditoService` que pasan la candidata fallan R-01,
CAJA-4-05a, CAJA-4-06a y CAJA-4-10 (el cierre no se confirma; en R-01 y CAJA-4-10 el error es `DEADLOCK_VICTIMA`) y CAJA-4-08 (cobro imputado a otra caja).

### Batería completa de regresión (todas las clases SQL Server, ambos modos)

`FinResidualSqlServerTests` (R-01 … R-14 + CAJA-4-xx, 27 escenarios), `FinCajaSqlServerTests` (H-CAJA-1/2/3), `FinIdemSqlServerTests`
(H-IDEM-1), `FinAntSqlServerTests` (FIN-ANT), `CancelarVsPagoServiciosRealesSqlServerTests` (CONC-P0) y
`CancelarVsPagoLockModelSqlServerTests`: **89 pruebas por modo; RCSI 0: 88 correctas; RCSI 1: 88 correctas.** La única que no pasa en ambos modos es
`CancelarVsPagoLockModelSqlServerTests.I4_…`, un **modelo de lock sin mitigación que está marcado «FAIL esperado — TOCTOU»** en su propio
código (demuestra el defecto original de CONC-P0; no está en el workflow de CI ni toca código de producción), preexistente e independiente de esta corrección.
FIN-FACT-P0/HARDENING (R-09, R-09B, R-10, R-10B…R-10E), FIN-ANT, FIN-CAJA, FIN-IDEM y CONC-P0 siguen en verde.

Deadlocks medidos en el motor (`xml_deadlock_report` de `system_health`, contenedor efímero): los **5** que involucran `AperturasCaja` ocurrieron
exclusivamente en las corridas de reproducción con el código anterior (R-01 y los CAJA-4 contra el código sin corregir). **Con la corrección:
0** deadlocks sobre `AperturasCaja` en las 12 repeticiones de R-01 y en las dos baterías completas. Los 2 deadlocks restantes del contador (uno por
modo) pertenecen a `CancelarVsPagoLockModelSqlServerTests` (`qaconc.Cuotas` / `qaconc.Ventas`, consultas del propio modelo), no a producción.

### H-FACT-2 (antes «fuera de alcance»): fallback de `FacturarVentaAsync` y `ReferenciaId` polimórfico — CORREGIDO en FIN-FACT-REFERENCIA-P0

`VentaService.FacturarVentaAsync` (fallback de ventas legadas) daba por «ya registrado» cualquier ingreso con `VentaId == null` y
`ReferenciaId == venta.Id`, sin filtrar por concepto ni por `Referencia`. `ReferenciaId` es polimórfico (cuota, crédito,
devolución…): un cobro de cuota cuyo `cuotaId` coincidiera con el id de una venta legada **suprimía** el ingreso de esa venta.
Se manifestó en este QA porque los escenarios crearon más cobros y desplazaron las identidades hasta hacer colisionar a R-10E, y
el harness lo evitaba descartando la venta. Corregido en FIN-FACT-REFERENCIA-P0: criterio único `IngresoVentaCaja` (concepto de
venta + `VentaId` directo, o histórico con `ReferenciaId` y `Referencia == Venta.Numero`) compartido con la cancelación; la
evasión del harness se eliminó y los escenarios `FACT-REF-01…10` (SQL Server efímero, RCSI 0 y 1) provocan la colisión a propósito.

## CI

`.github/workflows/fin-dev-sqlserver.yml` ejecuta ahora **R-01 … R-05 y `C4_*`** en un paso propio y exige `Passed` para cada uno en
el TRX por modo RCSI (omitido o «NO ENCONTRADO» ⇒ falla), junto con lo que ya ejecutaba (R-06…R-14, FIN-ANT, FIN-CAJA, FIN-IDEM,
CONC-P0, FIN-FACT). Con esto los diez escenarios históricos R-01 … R-10 están ejecutados y aprobados en ambos modos.

## Evidencia

Logs y TRX por modo en `D:\tmp\qa-hist\` (no versionados): `matriz-rcsi0.log`, `matriz-rcsi1.log`,
`mutacion-rcsi0.log`, `mutacion-rcsi1.log`, `deadlock-r01-ambos-modos.txt`, `ci/rcsi0/*.trx`, `ci/rcsi1/*.trx`,
`results/final-rcsi0.trx`, `results/final-rcsi1.trx`. En toda la corrida final el `errorlog` de SQL Server registra exactamente **2 deadlocks**
(uno por modo, ambos de R-01) y ninguno en los demás escenarios; las pruebas de estrés `CONC-P0` informan `deadlocks=0`.

# Cheques como medio de pago: auditoría del circuito y propuesta de cierre

> **Estado H10 (2026-10-30):** el propietario autorizó D1-A y D2-A y pidió contemplar D3-D6. El circuito está implementado en la rama `hito10/cheques` (ver la sección 6 y `docs/cheques-migracion-autorizacion.md`). **La migración no está aplicada a la base real**: hasta autorizarla y aplicarla, el circuito nuevo no funciona en producción y «Cheque» sigue sin poder usarse como medio de pago.

**Fecha:** 2026-10-30 · **Base:** `main` `216e8166` · **Alcance:** cheque RECIBIDO de un cliente como medio de pago de una venta (Hito 9, M3).
Método: lectura del código y tests de servicio/HTTP en SQLite en memoria. No se tocó ninguna base real ni se creó clon (no hizo falta). No hay migraciones nuevas.

## 0. Aclaración de modelo (importante)

Hay dos conceptos distintos que comparten la palabra "cheque":

| Concepto | Entidad | Estado hoy |
|---|---|---|
| Cheque **emitido** por la empresa a un proveedor | `Cheque` + `ChequeService` + `EstadoCheque` + permisos `cheques.*` (módulo "Compras") | Sin consumidores (controlador retirado, `docs/cheques-modulo-retirado.md`). **No sirve para el cheque recibido de un cliente**: su FK es `ProveedorId` y su estado (`Emitido/Entregado/Depositado/Cobrado/Rechazado/Anulado`) describe tesorería de pagos. |
| Cheque **recibido** del cliente en una venta | `DatosCheque` (snapshot 1:1 con `Venta`) + `MovimientoCaja` concepto `VentaCheque` con `EstadoAcreditacion` | Es el circuito auditado acá. No tiene entidad propia ni ciclo de vida: su "estado" vive en el movimiento de caja. |

Consecuencia: la propuesta **no reutiliza** `Cheque`/`ChequeService`/`cheques.*`.

## 1. Estado real hoy

### Funciona
- **Alta de datos**: Venta/Create y, desde #162, Venta/Edit guardan `DatosCheque` (número 1-12 dígitos, banco, titular, CUIT válido opcional, emisión, vencimiento, monto). Validación server-side (`DatosChequeViewModel`): vencimiento >= emisión, plazo <= 360 días, monto > 0. Cambiar de medio borra el snapshot (`SincronizarDatosChequeEdicionAsync`).
- **Confirmar**: una venta `Cheque` sin `DatosCheque` no se confirma (`VentaService` ~L1078).
- **Caja al confirmar**: `RegistrarMovimientoVentaAsync` crea ingreso `VentaCheque` con `EstadoAcreditacion = Pendiente`, bajo el lock de apertura.
- **Cómo cuenta en caja**: no es efectivo físico (`EsMovimientoFisico` solo acepta `TipoPago.Efectivo`), por lo tanto no entra en caja física esperada ni en el arqueo. Sí suma al "saldo" total y a `SaldoPendienteAcreditacion`; no suma a `CalcularSaldoRealAsync` (solo `Acreditado`/`null`). La conciliación lo clasifica como "A acreditar".
- **Acreditar**: `CajaService.AcreditarMovimientoAsync` es correcto y robusto: lock de apertura compartido con el cierre, relee estado en base, solo `Pendiente -> Acreditado` (idempotente: el 2.º intento falla), rechaza movimientos de caja cerrada. El endpoint `POST /Caja/AcreditarMovimiento` exige `caja.edit` + operar esa apertura (FIN-S-006, anti-IDOR) y tiene tests de autorización y de concurrencia en SQL Server.
- **Cancelar venta**: crea egreso `ReversionVenta` (`Revertido`) en la apertura original solo si está abierta, una sola vez (guard de doble reversión), con evento de auditoría `Ventas/CancelarVenta`. El saldo real es neutro tanto si el ingreso estaba pendiente como acreditado.

### Falta o está roto (con evidencia)
Ordenado por riesgo en la sección 2. Resumen: ningún lugar de la UI permite acreditar; no existe rechazo; acreditar no se auditaba; la acreditación es inviable para cheques diferidos porque la caja se cierra antes; un cheque pendiente de venta cancelada quedaba "pendiente" para siempre y acreditable; Venta/Edit no mostraba los datos guardados; el monto del cheque no se valida contra la venta; la devolución en efectivo no mira si el cheque se cobró; cobros de cuota con cheque cuentan como dinero real inmediato.

## 2. Brechas ordenadas por riesgo

| # | Brecha | Evidencia | Riesgo | Estado en esta PR |
|---|---|---|---|---|
| B1 | **Sin rechazo/devolución del cheque.** Nada modela "el banco lo rechazó": la venta queda cobrada y el stock/crédito/entrega intactos. | No existe `Rechazado` aplicado a cheques en `CajaService`/`VentaService`; el enum `EstadoAcreditacionMovimientoCaja.Rechazado` no tiene ningún escritor. | **Alto** (dinero) | **Implementado en PR (D2-A)**: `Caja/RechazarCheque`, motivo obligatorio, marca en Venta/Details y Caja. Pendiente: aplicar migración. |
| B2 | **Acreditar es inviable para cheques diferidos.** Una caja se cierra el mismo día; el cheque vence hasta 360 días después; `AcreditarMovimientoAsync` rechaza movimientos de caja cerrada ("requiere una conciliación auditada" que no existe). Cheque a 30 días = nunca acreditable. | `CajaService` `MensajeAcreditacionCajaCerrada`, `validarVigencia: false` | **Alto** | **Implementado en PR (D1-A)**: acreditación manual asentada en la caja abierta vigente (`AcreditacionCheque`); la caja cerrada no se modifica. Pendiente: aplicar migración. |
| B3 | **Sin botón de acreditar** en ninguna vista (el endpoint existe pero nadie lo invoca). | `grep AcreditarMovimiento Views` = 0 | **Alto** (circuito inoperable) | **Implementado en PR**: botones Acreditar/Rechazar en Caja/DetallesApertura y en la lista «Cheques pendientes». |
| B4 | **Venta cancelada con cheque pendiente** dejaba el ingreso `Pendiente`: seguía sumando en "pendiente de acreditación" y se podía acreditar (inflando saldo real con un egreso de reversión que luego lo contrapesaba). | Tests nuevos (fallaban antes) | **Medio-alto** | **CORREGIDO** (ver 4) |
| B5 | **Acreditar sin auditoría** (solo log). Cancelar, cerrar caja y otros sí auditan. | `AcreditarMovimientoAsync` solo `_logger` | **Medio** | **CORREGIDO** (ver 4) |
| B6 | **`DatosCheque.Monto` no se valida contra el total** ni al guardar ni al confirmar. El ingreso de caja usa el total de la venta, no el monto del cheque: pueden diferir. | Ningún uso de `DatosCheque.Monto` fuera del mapeo | **Medio** (conciliación cheque físico vs caja) | **Implementado en PR (D3)**: monto == total a cobrar (tolerancia $0,01) al guardar y al confirmar. |
| B7 | **Devolución de venta pagada con cheque**: el reembolso sale como egreso en **efectivo** (`TipoPago.Efectivo` fijo) aunque el cheque siga pendiente o se haya rechazado: se paga plata que no se cobró. Vale igual para tarjeta/transferencia pendientes. | `RegistrarMovimientoDevolucionAsync` | **Medio-alto** | **Implementado en PR (D4)**: Pendiente bloquea el reembolso en efectivo, Acreditado lo permite, Rechazado no reembolsa. |
| B8 | **Cuota pagada con "Cheque"** (`PagarCuota`, `MediosPagoCredito`): el ingreso `CobroCuota` nace con `EstadoAcreditacion = null` => cuenta como dinero real acreditado al instante, sin pendiente y sin datos del cheque. Inconsistente con la venta. | `RegistrarMovimientoCuotaCoreAsync` no setea estado | **Medio** | **Implementado en PR (D5)**: la cuota con cheque nace Pendiente y exige banco/número/vencimiento; solo en Pagar cuota individual (ver sección 6). |
| B9 | **Venta/Edit no precargaba el cheque** (inputs sin `value`): al reabrir una venta de cheque el panel aparecía vacío y guardar fallaba por campos requeridos; en Create, un error de validación borraba lo tipeado. | Test nuevo (fallaba antes) | **Medio** (UX/datos) | **CORREGIDO** |
| B10 | **Los datos del cheque no se ven en ninguna pantalla** (ni en Venta/Details, ni en Caja/conciliación). Solo hay un campo "Cheque" como etiqueta de medio. | `grep DatosCheque Views` solo en el form | **Medio** | **Implementado en PR**: tarjeta «Cheque» en Venta/Details y marca visible en Caja. |
| B11 | **Fecha de vencimiento sin efecto**: no hay alerta ni acreditación por vencimiento; el validador no impide emisión futura ni vencimiento ya pasado con emisión pasada (solo el `min` del HTML, no el servidor). | `DatosChequeViewModel.Validate` | Bajo-medio | **Implementado en PR (D6)**: emisión no futura; vencimiento no anterior a hoy salvo a la vista. Aviso «por vencer» (7 días) en «Cheques pendientes». Sin acreditación automática por vencimiento (D1-B descartada). |
| B12 | **Cheque duplicado**: el mismo banco+número puede registrarse en dos ventas. | Sin índice ni chequeo (`DatosCheque` solo tiene índice único por `VentaId`) | Medio | **Implementado en PR (D6)**: banco + número duplicado entre ventas vigentes se rechaza (a nivel de aplicación, sin índice único). |
| B13 | **Cancelar una venta de caja ya cerrada falla** ("se requiere un procedimiento de reversión auditada"); rechazo de un cheque tardío cae en el mismo agujero. | `MensajeReversionCajaCerrada` | Medio | **RESUELTA para cheques (H10-B13)**: el rechazo tardío ya estaba cubierto por D1/D2; la cancelación de una venta con cheque cuya caja cerró se resuelve con contrapartidas auditadas en la caja vigente (ver §6.4). Otros medios de pago con caja cerrada siguen rechazándose. |
| B14 | Cierre: `ChequesContados` va fijo en 0 en la vista de cierre y entra al "real" del arqueo; cheques pendientes no se reportan en el cierre. | `Views/Caja/Cerrar_tw.cshtml` L51 | Bajo | **Implementado en PR**: el cierre muestra cantidad y total de cheques pendientes del turno (informativo); no suma al arqueo. `ChequesContados` sigue en 0 en la vista. |
| B15 | Dashboard/Reportes no muestran cheques pendientes ni por vencer. | `DashboardService` sin referencias | Bajo | **Sin cambios** (fuera de alcance mínimo) |

## 3. Propuesta mínima

### 3.1 Modelo de estados (sin entidad nueva en la versión mínima)
El estado del cheque recibido es el `EstadoAcreditacion` del movimiento `VentaCheque` (ya existe):

```
Pendiente --acreditar--> Acreditado            (cheque cobrado/depositado y acreditado)
Pendiente --rechazar---> Rechazado             (rebotado, antes de cobrar)  [NUEVO escritor]
Pendiente --cancelar venta--> Anulado          (implementado en esta PR)
Acreditado --cancelar venta--> Acreditado + egreso Revertido (ya existe)
Acreditado --rechazar (post-acreditación)--> egreso "RechazoCheque" [decisión D2]
```
`Cheque`/`EstadoCheque` (emitidos) no participan.

### 3.2 Decisiones de negocio

Cada decisión indica opciones, impacto y recomendación. Las marcadas **INDISPENSABLE** son las únicas que hay que cerrar para poder implementar el circuito; el resto tiene recomendación por defecto que se puede aplicar sin consulta si el propietario no la objeta.

**D1 — Cuándo y dónde se acredita un cheque (INDISPENSABLE)**
- A (recomendada): **manual**, por un usuario con `caja.edit` (permiso existente), cuando el cheque se depositó/cobró. Como la caja del día de la venta ya cerró, la acreditación **se asienta en la caja abierta vigente** como un movimiento nuevo de ingreso `Acreditado` (concepto de acreditación de cheque) que referencia al original; el original queda marcado y **la caja cerrada no se modifica**. Impacto: nueva columna/relación de enlace (migración a generar y aprobar), UI mínima (3.5). No altera arqueos históricos.
- B: acreditación automática al vencer `FechaVencimiento`. Impacto: job nuevo; asume que un cheque vencido siempre se cobra (falso si rebota) y obligaría a revertir. No recomendada.
- C: permitir reabrir la caja cerrada para acreditar. Rompe H-CAJA-1/3 y los arqueos. No recomendada.

**D2 — Qué pasa al rechazarse un cheque (INDISPENSABLE)**
- A (recomendada, mínima): registrar el rechazo (`Rechazado`, o egreso de contrapartida si ya estaba acreditado, en la caja abierta vigente), con motivo obligatorio y auditoría. La venta **no se cancela sola**: queda con una marca "Cheque rechazado - pendiente de cobro" visible en Venta/Details y Caja, y el usuario decide cancelar la venta (flujo existente, que ya devuelve stock) o cobrar de nuevo con otro medio. Impacto: sin cambios en crédito/stock automáticos; cero riesgo de cancelaciones masivas por error.
- B: el rechazo cancela la venta automáticamente (stock vuelve, factura anulada). Riesgo alto si la mercadería ya se entregó.
- C: el rechazo reabre una deuda del cliente (cuenta corriente). Requiere un modelo de deuda de contado inexistente hoy (la venta tiene un solo `TipoPago` y no tiene saldo); alcance grande.
Efecto sobre crédito: un cheque rechazado en venta contado **no toca** el crédito personal del cliente en A. Si luego se quiere penalizar al cliente (mora/scoring) es decisión aparte.

**D3 — Monto del cheque vs total de la venta (decisión menor; se recomienda aplicar sin consulta)**
La venta tiene un único medio y el ingreso de caja es el total a cobrar, por lo que un cheque parcial no es representable (no hay pagos mixtos). Recomendación: exigir `DatosCheque.Monto == TotalACobrar` (con tolerancia de centavos) al guardar y al confirmar. Alternativa: permitir parcial, pero exige modelar pagos múltiples (fuera de alcance). Nota: hoy ventas existentes con monto distinto fallarían al confirmar; hace falta un barrido previo de datos.

**D4 — Devolución de una venta pagada con cheque**
Recomendación: si el ingreso del cheque está `Pendiente`, **bloquear el reembolso en efectivo** (no se cobró nada); si está `Acreditado`, el reembolso en efectivo se permite como hoy; si está `Rechazado`, no hay reembolso. Alternativa: permitir y registrar como deuda al proveedor de la confusión. Depende de D1/D2.

**D5 — Cobro de cuotas con cheque**
Recomendación: tratar el cobro de cuota con medio cheque (y de paso transferencia/tarjeta) igual que en la venta: `Pendiente` hasta acreditar. Impacta el saldo real y el estado de la cuota (la cuota hoy se marca pagada al cobrar). Es un cambio contable; requiere confirmar si la cuota queda pagada de inmediato o recién al acreditar.

**D6 — Validaciones adicionales del cheque**
Recomendación: (a) rechazar emisión futura; (b) rechazar vencimiento anterior a hoy salvo cheque "a la vista" (vencimiento = emisión); (c) rechazar banco+número duplicado entre ventas vigentes. Son reglas nuevas aunque pequeñas, por eso van a decisión.

### 3.3 Permisos (reutilizar, sin permisos nuevos)
- Acreditar y rechazar: `caja.edit` + `PuedeOperarAperturaAsync` (igual que hoy), idempotente y bajo lock de apertura.
- Ver datos del cheque en Venta/Details: `ventas.view` existente.
- No usar `cheques.*` (pertenece al cheque emitido a proveedores).

### 3.4 Auditoría (`SeguridadEventosAuditoria`)
- `Caja/AcreditarMovimiento` (hecho en esta PR), sin PII del cliente.
- `Caja/RechazarCheque` (propuesto): movimiento, venta, motivo, usuario.
- Cancelación de venta ya audita; el paso `Pendiente -> Anulado` viaja en la misma transacción.

### 3.5 UI mínima (después de decidir D1/D2)
1. Venta/Details: tarjeta "Cheque" (banco, número, titular, vencimiento, monto, estado de acreditación).
2. Caja/DetallesApertura y conciliación: botón "Acreditar" (y "Rechazar" con motivo) en la fila del ingreso `VentaCheque` pendiente, con confirmación; vista "Cheques pendientes" simple (lista ordenada por vencimiento, con aviso de por vencer).
3. Cierre de caja: mostrar el total de cheques pendientes como informativo (no suma al arqueo físico).

## 4. Correcciones técnicas implementadas en esta PR (sin reglas nuevas)

Tests escritos primero y verificados en rojo antes de la corrección:

1. **B4** `CajaService.RegistrarContramovimientoVentaAsync`: al cancelar una venta, un ingreso todavía `Pendiente` pasa a `Anulado` (estado ya existente en el enum, ya tratado como "no cobrado" por `VentaService`). Se relee el estado bajo el lock, así que una acreditación concurrente ya confirmada se respeta. El saldo total y el saldo real no cambian; desaparece de "pendiente de acreditación" y ya no se puede acreditar. Aplica a todos los medios pendientes (cheque, transferencia, MP, tarjeta). Tests: `CancelarVentaConChequePendiente_*`, `CancelarVentaConChequeYaAcreditado_*`.
2. **B5** `AcreditarMovimientoAsync` agrega el evento `Caja/AcreditarMovimiento` en el mismo `SaveChanges`. Test: `AcreditarCheque_RegistraEventoDeAuditoria_*` (también verifica que el 2.º intento no duplica).
3. **B9** `_VentaWizardForm.cshtml`: los siete campos del cheque se precargan desde `Model.DatosCheque` (Edit, y re-render de Create con errores). Test HTTP: `Edit_VentaConCheque_PrecargaLosDatosDelChequeEnElFormulario`.

Verificación: filtros `Caja|VentaService|Cancelar|Devolucion` = 1444 OK / 0 fallas, más los tests nuevos; CI completo en la PR.

## 5. Decisiones indispensables del propietario

Solo **dos** bloquean el desarrollo del circuito:

1. **D1**: ¿el cheque se acredita manualmente (recomendado), y esa acreditación se asienta en la caja abierta vigente sin tocar la caja cerrada?
2. **D2**: ¿el rechazo solo se registra y deja la venta marcada para resolver a mano (recomendado), o cancela la venta / reabre deuda?

D3-D6 tienen recomendación por defecto y pueden aplicarse en la misma etapa de implementación salvo objeción. Orden sugerido de implementación una vez decididas D1/D2: (1) migración de enlace + servicio de acreditación en caja vigente, (2) UI mínima (Details + botón Acreditar/Rechazar), (3) D3/D4, (4) D5, (5) D6.

## 6. Implementación H10 (rama `hito10/cheques`)

### 6.1 Qué se implementó (decisiones autorizadas D1-A y D2-A, más D3-D6)

- **Modelo.** `MovimientoCaja.MovimientoOrigenId` (FK propia, índice único filtrado por `(MovimientoOrigenId, Concepto)`), nuevos conceptos `AcreditacionCheque` (13) y `RechazoCheque` (14), y cinco columnas de trazabilidad en `DatosCheque` (`AcreditadoAt/Por`, `RechazadoAt/Por`, `MotivoRechazo`). Una migración de solo DDL (`H10ChequesRecibidos`); no hay SQL crudo.
- **Servicio.** `IChequeRecibidoService` (implementado por `CajaService`, sin tocar `ICajaService`): `AcreditarChequeAsync`, `RechazarChequeAsync`, `ObtenerChequesPendientesAsync`, `ObtenerEstadoChequeVentaAsync`. Mismo lock de apertura que el cierre y las altas de movimientos; relee el estado bajo el lock y, para cheques de una caja cerrada, serializa además sobre la apertura del cheque original.
- **D1-A (acreditar).** Manual, permiso `caja.edit` + `PuedeOperarApertura` (sin permisos nuevos). Si la caja del cheque sigue abierta, cambia el estado en el lugar. Si ya cerró, asienta un **ingreso nuevo** `AcreditacionCheque` (`TipoPago.Cheque`, no físico) en la caja abierta del usuario, referencia al original y marca el original `Acreditado`. No modifica movimientos, totales, efectivo esperado ni arqueo de la caja cerrada; su saldo real histórico no cambia (el dinero real se cuenta en la caja vigente). Idempotente: el segundo intento falla y no duplica; dos concurrentes, gana una.
- **D2-A (rechazar).** Motivo obligatorio (hasta 500), usuario y fecha en `DatosCheque`, auditoría `Caja/RechazarCheque` (sin datos personales del cliente). `Pendiente` pasa a `Rechazado`. `Acreditado` conserva su estado y genera un egreso `RechazoCheque` (no físico) en la caja abierta vigente. La venta **no** se cancela, no devuelve stock y no toca el crédito: queda la marca «Cheque rechazado – pendiente de cobro» en Venta/Details y en la caja. Se rechaza solo desde Pendiente o Acreditado; una venta cancelada no admite rechazo.
- **D3.** `DatosCheque.Monto == TotalACobrar` (tolerancia $0,01) al crear, al editar y al confirmar.
- **D4.** Devolución en efectivo: Pendiente bloquea, Acreditado permite, Rechazado (también si fue rechazado tras acreditarse) no reembolsa.
- **D5.** El cobro de cuota con cheque nace `Pendiente` (no es dinero real hasta acreditarlo) y exige banco, número (1-12 dígitos) y vencimiento no anterior a hoy; el detalle viaja en el ingreso de caja. **Solo se puede registrar desde «Pagar cuota» individual.** Adelanto, pago múltiple y cobro de la primera cuota al generar el crédito rechazan «Cheque» con un mensaje que remite a «Pagar cuota» (esas pantallas no capturan los datos del cheque).
- **D6.** Emisión no futura; vencimiento no anterior a hoy salvo cheque a la vista; banco + número duplicado entre ventas vigentes (no canceladas).
- **UI mínima.** Tarjeta «Cheque» en Venta/Details; Acreditar/Rechazar (confirmación; motivo obligatorio) en Caja/DetallesApertura; lista «Cheques pendientes» por vencimiento con aviso de por vencer (7 días) y total; total informativo de cheques pendientes en el cierre.

### 6.2 Decisión pendiente: qué pasa con la cuota si el cheque rebota (D5)

Hoy, al rechazarse el cheque de una cuota, **la cuota no se revierte**: sigue pagada o parcial, y el ingreso queda `Rechazado` (visible en Caja) con el motivo en sus observaciones. Opciones:

- **A (implementada, mínima):** marca de regularización solamente; el usuario corrige a mano. Riesgo: una cuota figura pagada sin dinero cobrado hasta que alguien actúe.
- **B (recomendada si se quiere automatizar):** al rechazar, reabrir el pago (restar de `MontoPagado`, reaplicar punitorio, recalcular saldo del crédito y puntaje). Requiere modelar la reversión del `PagoCuota` y su recibo; es una regla financiera nueva.
- **C:** impedir cheques para cuotas hasta cerrar B.

### 6.3 Efectos conocidos y límites

- La caja cerrada conserva sus números; sí cambia el estado del movimiento original (`Acreditado`) y, si se rechazó un cheque `Pendiente`, a `Rechazado`. El saldo total de una caja ya no suma los ingresos `Rechazado`.
- El «Cobrado» del encabezado de caja sigue siendo bruto (suma ingresos; no resta el egreso de rechazo ni la reversión de una venta cancelada): comportamiento previo, sin cambios.
- Cancelar una venta pagada con cheque cuya caja ya cerró: resuelto en §6.4 (B13). Con otros medios de pago y caja cerrada sigue rechazándose.
- Acreditar o rechazar exige poder operar la caja **del cheque original** (padrón o supervisor) y, para cajas cerradas, tener una caja abierta propia donde asentar.
- Los cheques de cuota no tienen vencimiento estructurado: en «Cheques pendientes» aparecen después de los de venta y su detalle va en texto.
- **Orden de locks (integridad bajo concurrencia).** Crédito → Venta → Apertura del cheque → Apertura vigente; la clave del cheque (`sp_getapplock`, dueño = transacción, recurso = hash de banco+número normalizados) es una hoja. Los asientos `AcreditacionCheque` y `RechazoCheque` se insertan **sin FK a `Ventas`** (el enlace viaja en `ReferenciaId` y el origen en `MovimientoOrigenId`): con `VentaId`, SQL Server lee la fila de la venta reteniendo la apertura y se invierte el orden de la cancelación (deadlock con READ COMMITTED sin snapshot, reproducido y corregido). Rechazo, cancelación y devolución revalidan el estado del cheque y de la venta **bajo el lock de la apertura**; la cancelación no agrega reversión si el cheque ya estaba rechazado (la salida ya está asentada).
- **Duplicado de cheque.** Banco y número se guardan y comparan normalizados (trim, espacios colapsados, mayúsculas). El chequeo y la inserción se serializan por clave con el lock de aplicación, en venta (alta y edición) y en cuota; cubre ventas y cuotas sin cambiar el modelo.

### 6.4 B13: cancelar una venta pagada con cheque cuando la caja original está cerrada

La caja cerrada es inmutable: no se reabre ni se le agregan, modifican o borran movimientos, y sus totales, efectivo esperado, saldo real y `CierreCaja` no cambian. La cancelación (`VentaService.CancelarVentaAsync` → `CajaService.RegistrarContramovimientoVentaAsync`) decide según el estado del cheque, releído **bajo el lock** de la apertura del cheque:

| Estado del cheque (caja cerrada) | Efecto |
|---|---|
| Pendiente | Nunca se cobró: el ingreso pasa a `Anulado` (sin egreso, no se devuelve dinero). No necesita caja vigente. Auditoría `Caja/AnularChequePendienteCajaCerrada`. |
| Acreditado (en su caja antes de cerrar, o por un asiento `AcreditacionCheque` en otra caja) | Un egreso `ReversionVenta` (`Revertido`, `TipoPago.Cheque`, no físico) en la caja **abierta del usuario**, con `MovimientoOrigenId` = ingreso original y `VentaId` = venta. El índice único `(MovimientoOrigenId, Concepto)` impide una segunda contrapartida. Auditoría `Caja/ReversionChequeCajaCerrada`. Sin caja vigente se rechaza antes de escribir y la cancelación entera (stock, factura, etc.) hace rollback. |
| Rechazado, o Acreditado y luego rechazado (egreso `RechazoCheque`) | No se agrega nada: la salida ya está asentada. |
| Anulado / venta ya cancelada | Idempotente: no se duplica nada (segunda cancelación: `VENTA_YA_CANCELADA`). |

- **Contabilidad.** El saldo real de la caja vigente cuenta la reversión de un cheque acreditado aunque su ingreso esté en otra caja o sea un asiento sin `VentaId` (`CalcularSaldoRealAsync` reconoce `MovimientoOrigenId`): si la acreditación se asentó en esa misma caja, el neto es 0; si se cobró en la caja cerrada, la caja vigente absorbe la devolución. El efectivo físico y el arqueo no cambian (contrapartida bancaria). La conciliación la clasifica como cuenta bancaria.
- **Orden de locks.** Crédito → Venta (la cancelación) → Apertura del cheque → Apertura vigente, el mismo del resto del circuito. Acreditar, rechazar y devolver revalidan bajo esos locks: acreditar×cancelar, rechazar×cancelar y devolución×cancelar dan un único resultado coherente (verificado con SQL Server, RCSI 0 y 1: `FinResidualSqlServerTests.ChequesCajaCerrada.cs`, B13a..B13l).
- **Sin migración**: usa `MovimientoOrigenId` y `ReversionVenta`, ya existentes.
- **Límite.** Solo cheque de venta. Una venta pagada con otro medio cuya caja cerró sigue rechazándose con el mensaje de «reversión auditada».

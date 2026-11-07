# Procedimiento supervisado: primer cheque bancario auténtico (V-02)

**Estado: NO EJECUTAR sin autorización expresa y por escrito del propietario.** Este documento solo describe el procedimiento. Mientras no se ejecute y verifique con un cheque real depositado y acreditado por el banco, V-02 queda **Bloqueado para cierre**.

Referencias: `docs/cheques-circuito-propuesta.md` (§6 diseño, §7 prueba en clon), `docs/cheques-migracion-autorizacion.md`, `docs/aceptacion-erp.md`.

## 1. Qué está verificado y qué no

| Verificado (sin cheque real) | Pendiente (solo con cheque real) |
|---|---|
| Venta con cheque, acreditación, rechazo (pendiente y acreditado), cancelación y devolución, caja abierta y cerrada, duplicados, permisos: tests de servicio, HTTP y SQL Server (RCSI 0 y 1) en CI, y operación desde la interfaz en clon aislado (2026-11-05). | Que el banco acredite o rechace el cheque y que el saldo bancario coincida con el asiento `AcreditacionCheque` del sistema. |
| Invariantes financieras (13) y controles de cheques (10) con 0 violaciones en el clon. | Que el operador real complete el flujo sin ayuda (configuración del medio, caja vigente, permisos). |
| Cajas cerradas inmutables. | Conciliación contra el extracto bancario real. |

## 2. Requisitos previos (todos obligatorios)

1. **Autorización escrita** del propietario: fecha/ventana, un operador, **una sola venta con un único cheque**, monto bajo y asumible.
2. **Caja:** el operador tiene una caja abierta a su nombre y vigente. Las cajas vencidas (turnos abiertos desde el 02/10 y el 13/10) las cierra el propietario con arqueo **antes**; este procedimiento no las toca.
3. **Permisos:** el operador tiene `caja.update` (rol Administrador, o concesión explícita). Quien acredita es titular de la caja del cheque o supervisor.
4. **Medio de pago (F-1):** el SuperAdmin crea/activa «Cheque» en Configuración → Medios de pago (H-15: queda inactivo y solo SuperAdmin hasta entonces). Registrar hora y quién lo activó; desactivarlo al terminar si no se continúa operando.
5. **Cheque:** banco, número, titular, CUIT, emisión (no futura), vencimiento (no anterior a hoy salvo a la vista) y monto **exactamente igual** al total a cobrar. Verificar que banco+número no exista ya en una venta vigente. Fotografiar anverso y reverso (guardar fuera del repositorio).
6. **Respaldo previo** (ver §3) verificado, y plan de reversión (§6) leído por el propietario.
7. **Aplicación:** una sola instancia, en la versión de `main` vigente con CI verde; sin despliegues ni migraciones durante la ventana.
8. **Sin fiscal:** no facturar durante la prueba (criterios de IVA siguen pendientes con el contador).

## 3. Respaldo (antes y después)

- Backup `COPY_ONLY` con `CHECKSUM` de `TheBuryProjectDb`, `RESTORE VERIFYONLY`, y registrar tamaño y hash del `.bak`. Copiarlo fuera del servidor si hay destino disponible.
- `DBCC CHECKDB` limpio.
- Línea base **solo lectura** (guardar el resultado): conteos de `Ventas`, `MovimientosCaja`, `AperturasCaja`, `CierresCaja`, `DatosCheque`, `Devoluciones`, stock del producto a vender y checksum de las cajas ya cerradas.
- Repetir la línea base al terminar y comparar: solo deben diferir las filas de la prueba.

## 4. Ejecución (un paso a la vez; detenerse ante cualquier diferencia)

| Paso | Acción | Verificación |
|---|---|---|
| 1 | Operador abre la sesión; confirma caja abierta propia. | Pantalla de Caja sin bloqueos. |
| 2 | Crear la venta de bajo monto con medio **Cheque** y confirmarla. | Venta/Details: «Cheque · Pendiente de acreditar»; `DatosCheque` coincide con el cheque físico; efectivo esperado de la caja **sin cambios**; stock −1. |
| 3 | Depositar el cheque en el banco. **No acreditar en el sistema todavía.** | Comprobante de depósito. |
| 4 | Esperar la acreditación bancaria **real** (plazo de compensación del banco; verificar en el extracto/homebanking que el crédito está disponible y no sujeto a rechazo). | Captura del movimiento bancario (importe, fecha, referencia). |
| 5 | **Recién entonces** acreditar en Caja/DetallesApertura (confirmación). | Estado `Acreditado`; si la caja original ya cerró, asiento `AcreditacionCheque` en la caja vigente con `MovimientoOrigenId`; reintento falla sin duplicar. |
| 6 | Conciliación (§5). | Monto del sistema = monto acreditado por el banco. |
| 7 | Cierre de caja con arqueo si corresponde; el cheque no altera el efectivo físico. | Diferencia de arqueo = 0 atribuible al cheque. |

Si el banco **rechaza** el cheque (impagado): registrar el rechazo con motivo en el sistema (Pendiente → Rechazado; Acreditado → egreso `RechazoCheque`), la venta queda «Cheque rechazado – pendiente de cobro» y se cobra por otro medio o se cancela. No devolver efectivo hasta tener un cobro verificable.

## 5. Conciliación contable (SQL Server, solo lectura)

Para la venta de prueba `@VentaId`, verificar y archivar el resultado:

1. Exactamente un `DatosCheque` con `Monto = Venta.TotalACobrar` (tolerancia 0,01) y estado esperado.
2. Exactamente un ingreso de cheque (`TipoPago = Cheque`, no físico) y, si fue acreditado en otra caja, exactamente un `AcreditacionCheque` con `MovimientoOrigenId` válido (índice único `(MovimientoOrigenId, Concepto)`).
3. Sin movimientos huérfanos ni duplicados para el mismo banco+número.
4. Cajas cerradas previas: checksums de movimientos y cierre idénticos a la línea base.
5. Suma de acreditaciones de cheque del día = créditos bancarios de cheques del extracto.
6. Auditoría: eventos `Caja/AcreditarCheque` (o `RechazarCheque`) con usuario y hora.

## 6. Plan de reversión

| Situación | Acción |
|---|---|
| Error antes de acreditar, sin dinero movido | Cancelar la venta desde la interfaz (cheque Pendiente: ingreso `Anulado`, stock +1). Devolver el cheque físico al cliente. |
| Cheque acreditado por error en el sistema, banco aún no acreditó | Rechazar con motivo (egreso `RechazoCheque` en la caja vigente). No editar filas por SQL. |
| Cheque acreditado por el banco y venta a deshacer | Cancelar la venta (contrapartida `ReversionVenta` en la caja vigente) o devolución con reembolso **solo** tras acreditación verificable; devolver el dinero por canal bancario fuera del sistema. |
| Datos inconsistentes o corrupción | Detener operación y **restaurar el backup previo** (§3) en una base nueva, comparar con la línea base, y recién entonces decidir el reemplazo con autorización del propietario. Se pierde cualquier operación posterior al backup. |
| Medio «Cheque» | Desactivarlo (SuperAdmin) si no se continúa operando. |

## 7. Criterios de cierre de V-02

V-02 pasa a **Finalizado** únicamente si: (a) se ejecutó §4 con autorización; (b) la acreditación bancaria real coincide con el asiento del sistema; (c) la conciliación §5 da 0 diferencias; (d) la comparación con la línea base §3 muestra solo las filas de la prueba; (e) el resultado se documenta con evidencia (capturas bancarias fuera del repositorio). Hasta entonces: **Bloqueado para cierre — falta validación bancaria real**.

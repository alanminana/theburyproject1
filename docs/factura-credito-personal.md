# Factura de una venta a crédito personal: estado verificado y decisiones pendientes (Hito 10)

Fecha: 2026-10-30. Base: `main` a49154d4 + rama `hito10/transferencia`. Método: clon de la base real (`_h10_transf`, ya eliminado), app en `127.0.0.1:6410`, recorrido por los endpoints HTTP reales y contraste con consultas SQL propias. La base real no se tocó.

**Este documento no afirma validez fiscal.** Describe lo que el sistema hace hoy, lo que se pudo cerrar con reglas ya existentes y lo que necesita una decisión del propietario o del contador. Las decisiones de IVA siguen siendo las de `docs/iva-recargos-verificacion.md` (D1).

## 1. Por qué figuraba como «no validada (exige contrato impreso)»

`docs/aceptacion-erp.md` y `docs/iva-recargos-verificacion.md` dejaron la factura de una venta a crédito personal sin validar «porque exige contrato impreso». La causa real, medida en el clon:

1. Facturar una venta a crédito personal exige, además del crédito generado y las cuotas, que el **contrato de venta esté emitido y su PDF exista en disco** (`VentaService.ValidarContratoCreditoPersonalGeneradoAsync` → `ContratoVentaCreditoService.ExisteContratoGeneradoAsync`: registro `Verificado` con ruta **y** `File.Exists`). Es un gate deliberado: el contrato y el pagaré se imprimen y firman antes de confirmar.
2. El clon se atendió desde otro directorio de contenido (`App_Data` no viajó con la base). Las ventas ya confirmadas tenían su registro de contrato en SQL, pero el PDF no estaba en disco. Facturar devolvía «Debe generar e imprimir el Contrato de Venta antes de continuar», como si nunca se hubiera generado, y desde el detalle de la venta **no había ningún camino para regenerarlo** («Ver contrato» devolvía «Contrato no encontrado. Genere el contrato antes de visualizarlo» y volvía al detalle, sin botón «Generar contrato» porque el registro existe).
3. No era un defecto de la factura: con el PDF regenerado la factura se emite y cierra (ver 2). Es la misma situación que se daría en producción tras restaurar una copia sin la carpeta `App_Data` (`docs/backup-restore.md` la marca «no regenerable»: el PDF se vuelve a producir desde el snapshot guardado en la base, pero no es el documento firmado original).

## 2. Recorrido verificado (clon)

Venta 2035 (crédito personal confirmado, 4 cuotas con 8 % de interés), con el estado real que traía la base:

| Paso | Resultado |
|---|---|
| Facturar sin el PDF del contrato | Rechazado, sin factura ni movimiento de caja (comportamiento seguro, el mismo de antes) |
| `Contrato/Generar` (regenera el PDF desde el snapshot) | Archivo recreado, registro intacto |
| Facturar | Factura `FA-B-202610-000005`: Subtotal 32.464,47 + IVA 6.817,53 = Total 39.282,00 (cierre 0,00) |
| Venta antes de facturar | `Subtotal 39.282,00` con IVA incluido (convención de la conversión de cotización); al facturar se normaliza a neto + IVA = total (`NormalizarNetoEIvaDesdeTotal`) |
| Interés del crédito | `Credito.TotalAPagar` = 42.424,56 (capital 39.282 + interés 3.142,56): **no** integra la factura, que cubre solo los ítems (`Venta.TotalFacturable`) |
| Caja | El crédito personal no registra cobro de venta al facturar (anticipo y cuotas se cobran por Caja del crédito) |

## 3. Qué se cerró técnicamente (sin inventar reglas)

- **Mensaje correcto** cuando el contrato figura emitido pero su PDF no está en el servidor: «El contrato de esta venta figura emitido, pero su archivo PDF no está disponible en el servidor … Abrí «Ver contrato» en el detalle de la venta, generalo de nuevo y reintentá». Si el contrato nunca se generó, el mensaje no cambia. El gate se mantiene: no se factura sin contrato.
- **Camino para regenerar**: «Ver contrato» con el PDF perdido lleva a la pantalla «Generar contrato» (`ContratoVentaCredito/Preparar`) con la explicación, en vez de volver al detalle sin salida.
- **Test que fija el comportamiento ya vigente**: la factura cubre solo los ítems (no el interés) y cierra neto + IVA = total incluso con una venta de cotización con Subtotal bruto.

No se tocó ninguna alícuota, ningún criterio de IVA ni la lógica del crédito.

## 4. Comportamiento actual seguro (se conserva)

1. No se factura una venta a crédito sin contrato emitido con PDF, sin crédito `Generado`/`Activo`/`Finalizado` del mismo cliente con cuotas, ni sin autorización vigente.
2. La factura cubre `Venta.Total` (ítems + armados + envío incluido). El interés de financiación queda en las cuotas del crédito.
3. Neto + IVA = total en todas las facturas emitidas (`docs/iva-recargos-verificacion.md`).
4. Con crédito vigente la devolución está bloqueada (#176).

## 5. Decisiones pendientes (propietario / contador)

### C1. ¿La factura de la venta debe incluir el interés de financiación?

Hoy el cliente paga `TotalAPagar` (42.424,56 en la venta 2035) y recibe una factura de 39.282,00. Lo que se cobra por encima de la factura es el interés.

| Opción | Efecto | Impacto en el ERP |
|---|---|---|
| A. Solo ítems en la factura (vigente) | La factura es la operación comercial; el interés se documenta con contrato, pagaré y recibos de cuota | Ninguno |
| B. Ítems + interés gravado a la alícuota de los ítems | Una factura por el total financiado | Cambio en `FacturarVentaBajoLocksAsync` y en el modelo (el interés se devenga en el tiempo, la factura se emite al confirmar) |
| C. Interés como concepto aparte (exento / no gravado) en la misma factura | Neto + IVA + concepto no gravado | Cambio de modelo (línea de concepto), como la opción D de IVA-D1 |
| D. Comprobante propio por cada cuota con interés | El interés se factura cuando se cobra | Cambio de modelo y de Caja/Crédito; el más trabajoso |

Recomendación: mantener A hasta que el contador defina el tratamiento del interés (es el mismo punto que IVA-D1; la decisión se toma una sola vez). Si el contador resuelve que el interés se factura, preferir C o D por sobre B, porque el interés se devenga cuota a cuota.

### C2. ¿Qué cubre la factura cuando hay anticipo y gastos administrativos?

La factura cubre el total de la venta; el anticipo se cobra por Caja del crédito y no se discrimina en el comprobante. Los gastos administrativos del crédito viven en `Credito` y no integran la factura. Recomendación: documentarlo en el contrato (ya lo hace el motor documental) y no tocar el comprobante hasta resolver C1.

### C3. ¿El contrato impreso debe seguir siendo condición para facturar?

Hoy sí (y también para confirmar). Opciones: mantenerlo (recomendado: es la regla vigente y el contrato fija los términos del crédito) o permitir facturar con excepción autorizada y registrada. Lo segundo cambia un control de cumplimiento: requiere decisión del propietario.

### C4. Copias de seguridad sin la carpeta de contratos

El PDF se regenera desde el snapshot de la base, pero no es el documento firmado. Recomendación: restaurar siempre `App_Data` junto con la base (ya incluido en `scripts/backup/backup.sh` y `restore-files.sh`) y no usar la regeneración como sustituto del original firmado. La copia fuera del servidor sigue pendiente del usuario.

## 6. Riesgos residuales

- La validez fiscal del comprobante de una venta financiada no está validada por un contador; este documento no la afirma.
- Una venta de crédito personal cuyo PDF se regenera no es el documento firmado original (C4).
- El interés queda fuera de la factura mientras no se resuelva C1.

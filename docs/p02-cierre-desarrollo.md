# P-02: cierre del desarrollo técnico (2026-11-20)

**Estado: P-02 — desarrollo técnico finalizado; pendiente exclusivamente de validación fiscal y puesta en producción.** P-02 **no** se declara finalizada: faltan las condiciones globales de §3 y §4. Complementa `docs/p02-procedimiento-activacion.md` (cómo se activa) y `docs/p02-respuestas-contador-20261119.md` (qué respondió el contador).

## 1. Desarrollo Finalizado (implementado, integrado y probado; no habilitado en producción)

| Componente | Estado | Evidencia |
|---|---|---|
| C1/C2 redondeo V2 | Finalizado | `CriterioFiscalV2.TruncarAPesos`; matriz `FiscalAlicuotasMixtasP02Tests` contra oráculo independiente |
| C5 descuento solo sobre productos | Finalizado | `DescuentoDeMedioSoloSobreProductos` (V2); venta, cotizador, conversión y devolución |
| C6 IVA de servicios (criterio implementado) | Finalizado | `ServiciosVentaIva`; alícuota congelada por operación |
| C14 política comercial informativa | Finalizado | `DevolucionPolitica`; nunca bloquea, deja constancia |
| C15 fechas argentinas | Finalizado | `FechaComercial`/`AHoraComercial`; reportes, período y día comercial |
| P4 comprobantes internos | Finalizado | `ComprobanteInternoLeyenda` (texto legal pendiente del contador, no de desarrollo) |
| P5 versionado fiscal | Finalizado | `Venta.CriterioFiscalVersion`, `CriterioFiscalRegistry.Resolver(venta)` |
| P3 vínculo de NC fiscal externa | Finalizado | migración `P02NotaCreditoFiscalExterna` preparada y ensayada (144 → 145); scripts up/down; concurrencia entre NC de una misma venta cerrada |
| P6 activación configurable | Finalizado | `Fiscal:CriterioV2Desde` / `FISCAL_CRITERIO_V2_DESDE`; resuelta una vez por proceso; inválida = no arranca; preflight |
| Ventas, cotizaciones, créditos, cuotas, recibos, Caja y devoluciones (alcance P-02) | Finalizado | ver §2 |

### 1.1 Correcciones de esta tanda de cierre

1. **Vista previa de devoluciones.** El modal «Crear devolución» mostraba el precio de lista (p. ej. $3.002,01) aunque lo reintegrable fuera lo cobrado ($2.851,90). Ahora el contexto (`GetQuickCreateContext`) sale de `DevolucionService.ObtenerPreviewDevolucionAsync`, que usa **la misma valuación** que `CrearDevolucionAsync` (extraída a `ValuarProductos` / `SubtotalDevolucion`, sin duplicar cálculo). Cada producto expone valor de lista, descuentos, ajuste del medio de pago, total reintegrable y el **importe exacto por cantidad** (la devolución que agota las unidades reintegra el remanente, sin centavos de redondeo). El modal muestra el desglose (valor de lista, descuentos, recargo/descuento por medio de pago, redondeo, importe a reintegrar) y aclara que el importe es el mismo con nota de crédito o con reembolso. Prueba `DevolucionPreviewImporteP02Tests`: parcial y total, descuento y recargo del medio de pago, descuento de línea, alícuotas mixtas; lo previsualizado = `Detalle.Subtotal` = `TotalDevolucion` persistido. No toca reglas fiscales ni V1 histórico (la valuación es la de siempre).
2. **Desglose del ajuste en Caja.** **No era un defecto de persistencia.** El modelo separa los conceptos: un **recargo** va en `MovimientoCaja.RecargoMedioPago` y un **descuento** en `MovimientoCaja.DescuentoMedioPago` (`RecargoMedioPago` queda vacío por diseño); `Cuota.RecargoMedioPago` lleva el signo. Se verificó que el total cobrado es correcto ($350,35 − $17,52 = $332,83), que no se duplican movimientos y que la conciliación muestra «Base $350,35 − desc. $17,52». Se fijó el contrato con pruebas exactas (`RegistrarMovimientoCuota_ConDescuentoConCentavos_…`, `Movimiento_ConDescuentoEnEfectivo_…`): `Monto = ImporteBase + recargo − descuento`.
3. **Integridad fiscal V1/V2.** Prueba `IntegridadFiscal_AjusteConAlicuotasMixtas_…` (10 casos): lo cobrado coincide con el oráculo y `Neto + IVA = Total` en V1 y V2; V2 reconstruye el neto con divergencia **0**; V1 puede diferir en centavos entre neto guardado y reconstruido, **medido hasta $0,71 (cota < $1,00)**, sin afectar lo cobrado; un comprobante V1 se interpreta idéntico con V2 vigente. Documentado en `p02-procedimiento-activacion.md` §5. V1 no se modifica ni se crea una versión por esta diferencia informativa.

## 2. Pendiente Fiscal (decisión/respuesta externa; no es trabajo de programación)

| Tema | Qué falta |
|---|---|
| C3 | tratamiento del redondeo en el IVA (hoy el residuo se absorbe en el IVA, igual que V1) |
| C4/C7/C8 | licitud y tratamiento de descuentos y recargos por medio de pago |
| C9/C10/C11 | documentación fiscal de cuotas, intereses y punitorios |
| C12 | crédito fiscal de compras |
| C13 | período fiscal de las NC fiscales (por eso el **neteo del IVA por NC fiscal sigue DESACTIVADO**) |
| C6 | posibles excepciones de alícuota de servicios |

## 3. Pendiente de Producción (condiciones operativas)

1. Base nueva (instalación limpia) con credenciales definitivas.
2. Fecha efectiva de puesta en marcha: valor real de `FISCAL_CRITERIO_V2_DESDE`.
3. Respaldo externo recuperable verificado (`backup.sh` + `restore-test.sh` + copia fuera del host).
4. Ejecución controlada de P3 (migración) y activación de P6 según `docs/p02-procedimiento-activacion.md` §1–§3. **P3 y P6 están autorizadas por el propietario**; no se ejecutan hasta cumplir 1–3.

## 4. Condiciones globales para declarar P-02 FINALIZADA

Todos los puntos de §2 resueltos por el contador/propietario y de §3 ejecutados y verificados. Hasta entonces: neteo de IVA por NC fiscal desactivado, V2 sin fecha configurada (V1), operación real del ERP sin iniciar.

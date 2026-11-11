# Facturación fiscal fuera del ERP: identificación y conciliación de comprobantes externos

Fecha: 2026-11-11. Base: `main` 58c12c2a. **Solo recomendaciones: este documento no implementa ningún campo, esquema, regla fiscal ni integración.** Todo lo que sigue está «PREPARADO, NO APLICADO». Complementa `docs/iva-preservacion-historica.md` (§4/F4.4), `docs/iva-matriz-diferencias.md` y `docs/iva-decisiones-contador.md`.

## 0. Decisión del propietario (2026-11-11)

- **No se integra ARCA/CAE por ahora.** La facturación fiscal se hace **fuera del ERP** (sistema externo o controlador fiscal).
- Los comprobantes que emite el ERP (`Factura` con número `<prefijo>-<AAAAMM>-<n>`, `NotaCredito` `NC-…`, recibos, presupuestos, contratos) son **documentos internos**. No se presentan como facturas fiscales.
- El IVA que muestran (neto, IVA, total) es una **discriminación informativa interna**, no la liquidación.
- Ninguna regla fiscal nueva se implementa sin validación del contador y autorización expresa.

Lo ya ejecutado en el código de esta rama (Parte A): el impreso de factura (`Views/Venta/ComprobanteFactura_tw.cshtml`) muestra, arriba y al pie, la leyenda **«COMPROBANTE INTERNO — DOCUMENTO NO VÁLIDO COMO FACTURA»** (constante única `Helpers/ComprobanteInternoLeyenda.Texto`), y el subtítulo pasó de «Comprobante de factura» a «Comprobante interno de venta». No cambió ningún importe, número, ruta ni id. El texto exacto es de producto; si el contador pide una redacción legal distinta, se cambia en un solo lugar.

## 1. Modelo operativo recomendado

Cada operación del ERP debería poder cruzarse con **un** comprobante fiscal del sistema externo. Clave natural del comprobante fiscal: **punto de venta + tipo de comprobante + número** (p. ej. `0003-B-00012345`). Datos de apoyo para validar el cruce: fecha de emisión, CUIT/documento del receptor, importe total y, si el sistema externo lo informa, CAE.

Relación esperada:

| Hecho en el ERP | Comprobante fiscal externo esperado |
|---|---|
| Venta Facturada / Entregada (con `Factura` interna) | 1 factura fiscal (o ninguna si el negocio decide no facturar esa venta: debe quedar visible) |
| Venta Confirmada sin facturar | Ninguno todavía (el ERP ya la trata como «informal»: no declara IVA; confirmar con el contador que ese circuito existe a propósito) |
| Cobro de cuota (recibo interno + Caja) | Según criterio del contador (C9/C10 de `iva-decisiones-contador.md` §5-a): interés, recargo, nada |
| Devolución con `NotaCredito` interna | 1 NC fiscal si la venta tenía factura fiscal (el ERP no la emite) |
| Anulación de `Factura` interna | Anulación/NC fiscal externa correspondiente |

### Comparación de opciones

| | (1) Campo «Nº de comprobante fiscal externo» en la venta | (2) Importación de export del sistema fiscal | (3) Solo reporte «ventas sin comprobante fiscal externo» |
|---|---|---|---|
| Qué es | Dato cargado a mano por el operador al facturar afuera | Archivo CSV/Excel del sistema fiscal que el ERP lee y cruza | Reporte que lista ventas facturables y su estado respecto de un cruce hecho fuera del ERP (planilla) |
| Esfuerzo | Medio (migración aditiva, formulario, validación, reportes, tests) | Alto (parser, formatos por sistema, matching, pantalla de excepciones) | Bajo si se apoya en Reporte de Ventas/IVA; sin esquema |
| Calidad de dato | Depende de la disciplina del operador (errores de tipeo) | Alta: viene del sistema fiscal | Baja: no vincula, solo señala |
| Pros | Vínculo explícito y consultable; base para NC y auditoría | Detecta diferencias de importe/fecha/receptor automáticamente; no depende del tipeo | Sin riesgo ni migración; sirve ya |
| Contras | Carga doble de datos; no verifica que el comprobante exista | Cada sistema fiscal exporta distinto; mantenimiento | No hay vínculo; la conciliación sigue siendo manual |
| Recomendación | **Fase 1 recomendada** (con 3 como complemento inmediato) | Fase 2, cuando haya volumen y un formato estable | Hacer ya, sin código nuevo si es posible (usar el Reporte de Ventas filtrado) |

Orden sugerido: **3 → 1 → 2**, cada paso con autorización por separado.

### 1.1 Diseño de la opción 1 (SIN implementar)

- **Dónde:** campos nuevos en `Factura` interna (no en `Venta`), porque la relación natural es factura interna ↔ factura fiscal. Para ventas sin `Factura` interna, el vínculo podría ir en `Venta`; decidir en el diseño detallado. Campos mínimos: `ComprobanteFiscalPuntoVenta` (5 dígitos), `ComprobanteFiscalTipo` (código ARCA o A/B/C/NC/ND), `ComprobanteFiscalNumero` (8 dígitos), `ComprobanteFiscalFecha`, `ComprobanteFiscalCuitReceptor` (opcional), `ComprobanteFiscalImporte` (opcional, para detectar diferencias), `ComprobanteFiscalCae` (opcional, texto de 14 dígitos).
- **Validación de formato:** punto de venta 1–5 dígitos numéricos, número 1–8 dígitos numéricos, tipo dentro de una lista cerrada; CAE de 14 dígitos si se informa; fecha no futura y no anterior a la fecha de la venta; importe ≥ 0. La validación es de forma, **no fiscal**: no decide qué tipo corresponde a cada cliente.
- **Unicidad:** índice único filtrado sobre (punto de venta, tipo, número) donde no sean nulos, para impedir que dos facturas internas apunten al mismo comprobante fiscal. Un comprobante fiscal por factura interna; si una venta se factura en dos comprobantes externos, hace falta una tabla hija (decisión de diseño; no se supone).
- **Permisos:** permiso propio (p. ej. `ventas.linkfiscal`) distinto de `ventas.invoice`; editar el vínculo ya cargado exige además motivo.
- **Auditoría:** cada alta/cambio/baja del vínculo registra usuario, fecha, valor anterior y motivo (reutilizar el mecanismo de auditoría existente).
- **Reportes:** columna «Comprobante fiscal externo» en el Reporte de Ventas y en el de IVA; filtro «sin comprobante fiscal externo»; export con la columna.
- **Devoluciones y NC:** la `NotaCredito` interna debería poder registrar el número de la NC fiscal externa (mismos campos). Una devolución sobre una venta con vínculo fiscal debe mostrar una advertencia «emitir NC fiscal en el sistema externo». No bloquea la devolución (decisión operativa).
- **Datos históricos:** el vínculo es nulo para todo lo emitido hasta hoy; no se rellena ni se recalcula nada.
- **Migración:** aditiva (columnas nullables + índice filtrado). No aplicar a la base real sin autorización y respaldo (ver reglas de migraciones del proyecto).

### 1.2 Diseño de la opción 2 (SIN implementar)

- **Formato de entrada:** CSV UTF-8 o Excel con columnas mínimas: `fecha`, `tipo`, `punto_venta`, `numero`, `cuit_receptor` (o documento), `razon_social` (opcional), `neto`, `iva`, `total`, `cae` (opcional), `estado` (emitida/anulada). El mapeo de columnas es configurable por sistema fiscal.
- **Matching, en orden de confianza:**
  1. Si la factura interna ya tiene el vínculo (opción 1): cruce exacto por punto de venta + tipo + número; se comparan total y receptor.
  2. Sin vínculo: coincidencia por **documento del receptor + total exacto + fecha dentro de ±N días** (N configurable, p. ej. 3). Si hay un solo candidato: «coincide sugerida»; si varios o ninguno: «excepción».
  3. Diferencia de total dentro de una tolerancia (ver §2.2, truncado a pesos) se marca «coincide con diferencia explicada»; fuera de tolerancia, «diferencia».
- **Salida:** tres listas: conciliadas, ventas del ERP sin comprobante externo, comprobantes externos sin venta en el ERP. Nada se escribe en el ERP sin que una persona confirme cada sugerencia.
- **Seguridad:** el archivo se trata como entrada no confiable (tamaño máximo, tipos permitidos, sin fórmulas ejecutables, vista previa antes de confirmar).

### 1.3 Opción 3 (hoy, sin esquema)

Reporte de ventas Facturadas/Entregadas del período con número interno, fecha, documento del cliente y total, para cruzar a mano contra el libro IVA ventas. Con la opción 1 implementada, el mismo reporte agrega el filtro «sin comprobante fiscal externo».

## 2. Conciliación mensual: ERP vs libro IVA ventas del sistema fiscal

### 2.1 Qué cruzar

1. **Conjunto:** ventas del ERP en estado Facturada/Entregada con `Factura` no anulada del mes (el Reporte de IVA ya usa ese criterio; las Confirmadas sin facturar no declaran IVA) contra los comprobantes del libro IVA ventas del mes.
2. **Período:** el Reporte de IVA asigna la venta al día **UTC** de `FechaVenta` (D-F4-2). Una venta del 31/10 22:30 (Argentina) cae en noviembre en el ERP; el sistema fiscal la fecha el 31/10. Conciliar con una tolerancia de 1 día en los bordes de mes.
3. **Importes:** total, neto e IVA por comprobante; después, totales por alícuota.
4. **Cantidad:** comprobantes del libro sin venta en el ERP y ventas del ERP sin comprobante.
5. **Anulaciones y NC:** el sistema fiscal informa NC fiscales; el ERP informa `NotaCredito` internas y devoluciones.

### 2.2 Diferencias esperables y por qué

| Fuente de diferencia | Comportamiento del ERP | Efecto en la conciliación |
|---|---|---|
| Truncado a pesos | El total se trunca (`Math.Floor`); el centavo se absorbe en IVA y luego en neto | Ítems 1.000,49 → ERP cobra 1.000 = neto 826,85 + IVA 173,15. Un sistema que redondea comercial (o no trunca) emite 1.000,49: diferencia de hasta $0,99 por comprobante. Con ítems 1.000,51 el ERP cobra 1.000 y el criterio comercial daría 1.001 (diferencia 1,00). |
| Recargo del medio de pago | Se grava a la alícuota de los ítems, prorrateado | Transferencia +3 % sobre A (21 %) + B (10,5 %): 1.500 → 1.545 = 1.317,30 + 227,70. Si el sistema fiscal factura el recargo como concepto aparte o a 21 %, el IVA difiere. Tarjeta +10 % sobre 1.000: 1.100 = 909,09 + 190,91. |
| Descuento en efectivo | Reduce base e IVA, también sobre envío/armado | 3.000 con −5 %: cobrado 2.850 = 2.355,37 + 494,63 (sin descuento 2.479,34 + 520,66). Si el sistema externo lo trata como descuento financiero, la base difiere. |
| Envío y armado | IVA 21 % incluido en el total | Envío 200 = 165,29 + 34,71; armado 450 = 371,90 + 78,10; producto 1.000 + servicios 650 = 1.650 = 1.363,64 + 286,36. Si el externo los factura aparte o a otra alícuota, difiere el reparto. |
| Venta legacy con envío fuera del total | Caja cobra Total + envío; factura y Reporte de Ventas solo el Total | Caja 1.200, factura 1.000: 200 sin IVA ni factura (D-F4-3). |
| Ventas a crédito | El interés vive en `Credito.TotalAPagar`, fuera de la factura y sin IVA | Venta 2.000, anticipo 500, 3 cuotas de 600 (capital 500 + interés 100): el cliente paga 2.300; la factura/IVA cubre 2.000 (IVA 347,11). Los 300 de interés no están en el comprobante interno; si el externo los factura, aparecen solo allá. |
| Cobro de cuotas | Recibo interno + Caja, sin factura | Cuota 600 por transferencia: Caja 618 (600 + 18 de recargo 3 %), sin comprobante en el ERP. Si el externo factura intereses/recargo por cuota, hay comprobantes sin contraparte (D-F4-5). |
| Devoluciones y NC | El Reporte de IVA **no netea** devoluciones; la `NotaCredito` interna es saldo a favor sin CAE | V1: 3 × 1.000 con −5 %, cobrado 2.850, IVA 494,63. Se devuelve 1 unidad = 950,00 (IVA 164,88). Reporte de IVA del ERP: 1.260,35 (no netea); libro fiscal con NC fiscal: 1.095,47. La diferencia 164,88 es esperable hasta que se autorice netear (fila 2.5a). |
| Venta Confirmada sin facturar | Cuenta en Ventas y Caja, no declara IVA | V5: 1.000 en Ventas y Caja, 0 en IVA. No debe esperarse comprobante externo todavía; si pasa de mes, es un pendiente de facturación. |
| Factura interna anulada | No declara IVA | Debe existir NC/anulación externa; verificarlo. |

### 2.3 Procedimiento mensual sugerido

1. Exportar del ERP el Reporte de Ventas y el de IVA del mes (con tolerancia de 1 día en los bordes).
2. Exportar el libro IVA ventas del sistema fiscal del mismo mes.
3. Cruzar por documento del receptor + total + fecha (o por número externo si ya está vinculado).
4. Clasificar cada diferencia con la tabla 2.2; solo lo que no encaje es un error a investigar.
5. Listar: ventas del ERP sin comprobante, comprobantes sin venta, NC fiscales sin devolución en el ERP, devoluciones del ERP sin NC fiscal.
6. Registrar el resultado y quién lo firmó. No corregir importes del ERP para que «cierren»: las diferencias explicadas por criterio se documentan, no se reescriben.

## 3. Riesgos de que el comprobante interno circule como fiscal, y controles

| Riesgo | Control |
|---|---|
| El cliente o un tercero toma el impreso (letra A/B/C, número, IVA) por una factura | **Hecho:** leyenda «COMPROBANTE INTERNO — DOCUMENTO NO VÁLIDO COMO FACTURA» arriba y al pie del impreso, con contrato de prueba que falla si se quita. Pendiente de producto: mismo texto en cualquier PDF/export nuevo de factura. |
| Numeración indistinguible de un número fiscal | Recomendación (no implementada): prefijo propio inequívoco en Configuración → Prefijos (p. ej. `INT-`), distinto de cualquier punto de venta fiscal; no usar formato `0001-00000001`. Cambiar el prefijo solo rige hacia adelante. |
| El operador entrega el impreso interno en lugar de la factura fiscal | Capacitación; en el flujo, recordatorio al facturar: «el comprobante fiscal se emite en el sistema externo». Con la opción 1, alerta de venta Facturada sin vínculo fiscal al cierre del día. |
| Pantalla «Emitir factura» / botón «Facturar» sugiere emisión fiscal | La UI operativa no se renombró (es terminología operativa interna). Si el propietario quiere, cambiar a «Registrar comprobante interno» es una decisión de producto aparte. |
| Venta Confirmada sin facturar queda sin comprobante fiscal por olvido | Reporte de ventas confirmadas sin factura con antigüedad (opción 3). |
| Anulación o devolución sin NC fiscal externa | Advertencia en devolución/anulación; revisión mensual (§2.3, punto 5). |

## 4. Datos mínimos a guardar para facilitar una futura integración ARCA (sin comprometerla hoy)

Si más adelante se decide integrar, conviene que ya estén disponibles (y no se pierdan) estos datos, todos opcionales y sin lógica fiscal asociada hoy:

- Punto de venta fiscal, tipo de comprobante (código ARCA) y número del comprobante externo.
- Fecha de emisión fiscal, CAE y fecha de vencimiento del CAE (si el sistema externo los informa).
- CUIT y condición frente al IVA del receptor; tipo y número de documento (hoy hay `Cliente.NumeroDocumento`).
- CUIT del emisor y domicilio (hoy en `EmpresaConfiguracion.Cuit` de configuración de documentos, no en la factura).
- Concepto (productos/servicios/ambos) y alícuota por línea (ya se guarda alícuota por línea).
- Referencia a comprobante asociado en NC/ND (qué factura externa corrige).
- Versión del criterio de cálculo usada (ver `iva-preservacion-historica.md` §2–3: hoy se recalcula en lectura).

Lo que **no** conviene hacer hoy: inventar CAE, códigos de barras o numeración con formato fiscal en comprobantes internos.

## 5. Requiere validación del contador antes de implementar cualquier cosa

1. Texto legal de la leyenda (¿alcanza «Comprobante interno — Documento no válido como factura»?).
2. ¿Qué se factura por cada cuota cobrada (interés, recargo, nada) y si los punitorios llevan IVA (C9, C10).
3. Criterio de truncado/redondeo a pesos y dónde se absorbe la diferencia (C1–C3).
4. Tratamiento del recargo del medio de pago y del descuento en efectivo (C4, C5, C7, C8).
5. Alícuota de envío y armado (C6).
6. Circuito de ventas Confirmadas sin facturar («informales»): ¿es válido que exista?
7. NC fiscal por devolución y período en que se reconoce el IVA (C13, C14).
8. Período fiscal: día comercial argentino vs día UTC (C15).
9. Si el vínculo con el comprobante externo debe guardar CAE y qué datos exige para conservación documental.

Hasta que haya respuesta y autorización: **no se implementa nada fiscal**.

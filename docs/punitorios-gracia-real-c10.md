# Punitorios: gracia real de 12 días (C10, 2026-11-20)

Decisión del propietario (opción B). Reemplaza el comportamiento de PUN-ML4. **No** cambia tasas, porcentajes, la fórmula monetaria ni PUN-ML5.

## Regla

`DiasPunitorio = Max(0, DiasAtraso - DiasGracia)`. El día del vencimiento es el día 0. Importe del tramo = `saldo × porcentaje / 100 × DiasPunitorio / PeriodoDias` (sin redondear por tramo; el total se redondea una vez).

| Días de atraso (gracia 12) | 0–12 | 13 | 14 | 15 | 20 | 30 |
|---|---|---|---|---|---|---|
| Días que generan punitorio | 0 | 1 | 2 | 3 | 8 | 18 |

- **Regla anterior (PUN-ML4):** superada la gracia, se cobraban TODOS los días desde el vencimiento (atraso 13 con gracia 12 = 13 días). Era retroactiva.
- **Tramos** (pagos parciales, cambio de configuración): cada tramo cuenta solo los días posteriores a `vencimiento + DiasGracia` de **su** configuración (`SegmentoPunitorio.DiasPunitorio`; `Dias` sigue siendo los días corridos del tramo). Un pago dentro de la gracia no genera punitorio sobre lo pagado; una cuota saldada dentro de la gracia no devenga nada (antes, un cálculo posterior a la gracia cobraba retroactivo aunque se hubiera pagado a tiempo).
- **La gracia es configuración**, no una constante: `ConfiguracionPunitorio.DiasGracia` (versionada, append-only). La instalación nueva debe cargar una versión con 12 días (`docs/p02-procedimiento-activacion.md` §3.1); nada se siembra.
- **Día comercial argentino:** el cálculo usa `IRelojComercial.HoyComercial` (31/10 22:30 ART sigue siendo 31/10), no la fecha UTC ni la zona del servidor.

## Un solo cálculo

`PunitorioCalculator` es la única fuente: lo usan la consulta de la cuota, el detalle, la aplicación (confirmación), el cobro (que solo toma lo ya aplicado) y la vista previa de configuración (`PreviewPunitorio`). Su texto de ayuda en Configuración describe la gracia real.

## PUN-ML5 se mantiene

El importe se calcula a la fecha de cobro, pero **solo se cobra lo aplicado y confirmado por un usuario autorizado** (`cobranzas.applyfine`). No hay cobro automático; permisos, auditoría y concurrencia (RowVersion, índice único de aplicación activa, transacción serializable) no cambian. Prueba: `C10_CobroSinConfirmacionAutorizada_NoCobraElPunitorioCalculado`.

## Compatibilidad con operaciones históricas

Los importes ya aplicados o cobrados **no se modifican nunca**: el cobro lee `PunitorioAplicado.Importe`, no recalcula. El riesgo real era otro: `AplicarAsync` calcula el teórico acumulado y le resta lo ya aplicado; con la regla nueva el teórico de una cuota con punitorio aplicado bajo la regla anterior sería menor que lo aplicado y la cuota quedaría bloqueada ("el cálculo actual es menor a lo ya aplicado").

Solución sin migración ni reinterpretación: cada aplicación nueva guarda en su snapshot (`DesgloseSnapshotJson.ReglaGracia`) `GraciaReal` o `Retroactiva`. Una cuota con alguna aplicación **no anulada** sin marca (snapshot anterior a C10, vacío o ilegible) o marcada `Retroactiva` sigue calculándose con la regla anterior hasta saldarse, de modo que el diferencial es coherente con lo confirmado y cobrado. Las cuotas sin historia (y las que solo tienen aplicaciones anuladas) usan la gracia real. Un snapshot **ilegible, vacío o con marca desconocida** no se interpreta como ninguna regla (suponer la anterior podría permitir cobrar de más): la cuota no admite nuevas aplicaciones (conflicto recuperable: revisar y, si corresponde, anular esa aplicación), la consulta sigue disponible con la regla vigente y los importes ya confirmados y su cobro no se tocan (hallazgo de la revisión de Codex, corregido). `PunitorioAplicado.DiasComputados` pasa a ser los días que devengaron (en cuotas con la regla anterior sigue siendo el atraso total).

Caso concreto a conocer: una cuota con punitorio aplicado antes de C10 conservará la regla retroactiva para sus aplicaciones sucesivas. No se inventó otra solución; si el propietario prefiere reexpresar esas cuotas (anular y reaplicar) es una decisión operativa por cuota. La base nueva de la puesta en marcha no tiene históricos, así que no le aplica.

## Evidencia

- `PunitorioGraciaRealTests` (26): tabla del propietario, gracias 0/3/30, mes/año/bisiesto, pagos parciales (dentro y después de la gracia), cuota no vencida o adelantada, cambio de configuración con otra gracia, inactiva.
- `PunitorioServiceTests.C10_*` (11, incluye 5 casos de snapshot dañado): aplicación y snapshot, historia conservada, anuladas, recálculo tras confirmar (sin redevengar), día comercial argentino.
- Pruebas previas actualizadas con importes recalculados a mano (atraso 11 con gracia 5 = 6 días = $300 sobre $10.000 al 10 %/20 días); el oráculo E2E `e2e-tx/financiero/lib/reglas.js` quedó alineado.

## No cubre

El IVA del punitorio con ventas de distintas alícuotas sigue sin definir (hoy el punitorio no lleva IVA ni comprobante en el ERP).

# Mora contractual: implementación A+B (2026-11-23)

Decisión del propietario: **A** para contrato y pagaré (el interés por mora coincide con el punitorio vigente, hoy 0 %), **B sin estimaciones monetarias** para alertas, **C** (crédito cerrado) hasta completar la validación. Diagnóstico de origen: `mora-contractual-diagnostico-20261123.md`.

**Este cambio no abre el crédito, no establece ninguna tasa, no toca contratos ni operaciones históricas y no genera ni cancela cuotas.**

## Qué cambió

### Contrato y pagaré (A)
- Nueva autoridad única: `InteresMoraContractual` (`Services/Documentos/InteresMoraContractual.cs`) lee la `ConfiguracionPunitorio` vigente a la fecha comercial de emisión (misma regla que `PunitorioCalculator`: última versión con `VigenteDesde <= hoy`).
- **0 % es válido**: el texto dice «a la fecha de emisión de este documento no se devenga interés punitorio por la falta de pago en término (0 %) (configuración de punitorios vigente desde el dd/MM/aaaa)».
- Con tasa positiva: «interés punitorio del X % cada N día(s), calculado día a día sobre el saldo impago de cada cuota vencida, a partir del día siguiente a los G día(s) de gracia posteriores al vencimiento (vigente desde …)». Refleja el prorrateo diario y la gracia real (C10) del calculador.
- **Sin configuración vigente o inactiva: no se emite** (error «No hay una configuración de punitorios vigente y activa…»). No hay fallback a ninguna tasa.
- Se eliminó todo fallback/semilla: `TasaMoraBase`, interés de empresa, interés de la plantilla (columnas conservadas como histórico sin efecto), `0,05` (`DbInitializer`), `0,20` (referencia de empresa y editor de plantilla).
- Se quitó la validación «interés diario > 0» (`ValidarPlantilla`, `[Range(0.0001…)]`, `min="0.0001"`).
- Variables nuevas: `contrato.interesMoraTexto` (+ `Porcentaje`, `PeriodoDias`, `GraciaDias`, `VigenteDesde`) y token `{{INTERES_MORA_TEXTO}}` para el contrato anterior. El texto de referencia del contrato (cláusula tercera) y el de la plantilla anterior ahora las usan, y el contrato sembrado las **requiere** (`VariablesRequeridas`).
- Variables antiguas (`{{contrato.interesMoraDiario}}`, `{{empresa.interesMoraDiario}}`, `{{INTERES_MORA}}`): solo se resuelven con punitorio 0 % (equivalente exacto = 0). Con tasa positiva quedan vacías y **el motor exige las variables de interés que use el contenido aunque la plantilla no las declare** (`DocumentoService.VariablesFaltantes(…, contenido, …)`); el contrato anterior rechaza la plantilla que todavía las usa. Así una plantilla vieja no puede emitir una cláusula en blanco ni un «diario» inexacto.
- Snapshot del contrato anterior: `InteresMoraTexto`, `PunitorioPorcentaje`, `PunitorioPeriodoDias`, `PunitorioDiasGracia` (los contratos históricos no los tienen y no se tocan). `InteresMoraDiarioPorcentaje` vale 0 con 0 %, o el equivalente aproximado (informativo) con tasa positiva.
- No se confunde con el interés financiero del plan (`Credito.TasaInteres`): prueba específica.
- Empresa: «Interés por mora» deja de ser un dato de la empresa (solo lectura con enlace a Configuración → Punitorios); guardar la empresa ya no copia ninguna tasa a la empresa ni a la plantilla.

### Alertas (B)
- `MoraService` ya no calcula ni persiste mora estimada (se retiró `CalcularMora`); `LogMora.TotalRecargosAplicados` queda en 0 y `AlertaCobranza.MontoMoraCalculada` en 0 (columnas conservadas por histórico).
- Se retiró de pantallas: «Mora calculada» (Mora/Index, Clientes en mora), «Mora acumulada» y columna «Mora» (Ficha del cliente), «Recargos aplicados» (Logs). Se mantienen capital vencido, cuotas vencidas y días de atraso.
- `TasaMoraBase` y `TipoTasaMora` quedan sin efecto (campo conservado en pantalla con aviso). El default 5 al crear la fila de configuración se eliminó (queda sin valor). Resuelve CFG-DEF-03 y CFG-DEF-04.

## Compatibilidad
- Contratos/pagarés ya emitidos: texto y snapshot congelados (pruebas: el documento emitido conserva su texto al cambiar la tasa; el snapshot del contrato anterior no cambia).
- Sin migración EF: todas las columnas se conservan.
- **Riesgo conocido:** `RegenerarAsync` de un documento vigente vuelve a renderizar con los punitorios de hoy; regenerar un contrato emitido después de un cambio de tasa imprimiría la tasa nueva. Con 0 % no hay diferencia hoy.
- **Acción en la base real antes de abrir crédito (no automática):** las plantillas documentales ya cargadas no se modifican por código (el seeder es no destructivo). La plantilla de contrato real conserva la cláusula antigua «interés diario … {{contrato.interesMoraDiario}}», que con 0 % imprime «0,00%» (coherente pero con el texto viejo) y que se bloquea sola si se activa una tasa positiva. Recomendado: editar la plantilla en Documentos para usar `{{contrato.interesMoraTexto}}` y declararla como variable requerida; hacer lo mismo con la plantilla del contrato anterior (`{{INTERES_MORA_TEXTO}}`). Verificar ambas con la vista previa.

## Pruebas
- Nuevas: `MoraContractualMotorTests` (0 % con `TasaMoraBase = 5`, plantilla antigua con 0 %, cambio de tasa con documento histórico intacto, plantilla antigua con tasa positiva bloqueada, sin configuración o inactiva bloqueado, interés financiero distinto de mora, guardar empresa no copia tasa, texto igual al cálculo real) y bloque `MoraContractual_*` en `ContratoVentaCreditoServiceTests` (0 %, la semilla no se imprime, cambio de tasa, ausente/inactivo, token antiguo).
- Reescritas: `CFG14`/`CFG16` (alertas sin estimación, conservan capital y días), enlace de Empresa, expectativas de empresa/plantilla; sembrado de punitorio 0 % en los arneses de documentos.
- e2e-tx `financiero.spec.js`: el chequeo 18 ahora espera mora estimada 0.

## Pendientes de validación legal/contable (no resolubles técnicamente)
1. **Redacción**: el texto de 0 % y el de tasa positiva debe validarlo el estudio o el contador antes de emitir contratos reales (en especial la base de cálculo —el calculador aplica el porcentaje sobre el **total de la cuota** (capital más interés financiero del plan), no solo sobre el capital— y la mención de la vigencia).
2. **Vigencia vs. cálculo**: el contrato fija la configuración vigente **al firmar**; el calculador aplica la configuración vigente **cada día** (por tramos), de modo que una versión posterior de punitorios afecta también deudas de contratos ya firmados. Hay que decidir si eso es admisible (cláusula de actualización con aviso) o si las versiones nuevas solo deben regir para contratos nuevos.
3. **Pagaré**: el pagaré sembrado del motor nuevo no lleva cláusula de interés (el de la plantilla anterior sí, vía `{{INTERES_MORA_TEXTO}}`). Confirmar si el pagaré debe consignar la tasa o remitir al contrato (impacta su carácter de título).
4. **Contabilidad**: con 0 % no hay devengamiento; si se aprueba una tasa positiva, definir imputación y reconocimiento del punitorio (la aplicación sigue requiriendo confirmación autorizada, PUN-ML5).
5. **Regeneración**: política para regenerar documentos firmados tras un cambio de tasa.

# Mora contractual: diagnóstico técnico y diseño de la solución (2026-11-23)

> **Estado:** el propietario decidió A+B (2026-11-23) y se implementó; ver `mora-contractual-implementacion-20261123.md`. Este documento queda como diagnóstico de origen; sus pruebas de caracterización se reemplazaron por pruebas del comportamiento nuevo.

Base: `main` 647207c1. Complementa la §3 de `puesta-en-marcha-estado-20261123.md`. **No cambia ninguna regla financiera**: el único código nuevo son pruebas de caracterización (§8). El crédito sigue cerrado y no se estableció ninguna tasa.

## 1. Resumen

`ConfiguracionMora.TasaMoraBase` (default 5, creado por `MoraService.GetConfiguracionAsync`) alimenta tres usos que no coinciden entre sí. Ninguno genera deuda real:

| # | Uso | Unidad con que se lee | Efecto |
|---|---|---|---|
| 1 | Estimación de mora en alertas (`MoraService.CalcularMora`) | **mensual** (`monto × tasa/100/30 × días`) | Solo se suma a `LogMora.TotalRecargosAplicados` |
| 2 | Texto contractual (`contrato.interesMoraDiario`, `empresa.interesMoraDiario`, `{{INTERES_MORA}}`) | **diaria** | Texto del contrato y del pagaré; es lo único que un cliente leería |
| 3 | Pantalla Mora (`ConfiguracionExpandida`, AutoMapper `TasaMoraDiaria`/`PorcentajeRecargo`) | sin unidad definida | Solo guarda/muestra |

La deuda punitoria real sale **únicamente** de `ConfiguracionPunitorio` + `PunitorioService`/`PunitorioCalculator` (aprobado: 0 % / 30 días / gracia 12). Esa entidad no lee `TasaMoraBase` ni el contrato la lee a ella (verificado: ningún `ConfiguracionPunitorio` en `ContratoVentaCreditoService`, `Documentos/*`).

### Correcciones a lo documentado en la §3 de puesta-en-marcha

1. **El monto de las alertas nunca se persiste.** `ProcesarMoraAsync` calcula `moraCalculada` pero crea la `AlertaCobranza` sin asignar `MontoMoraCalculada` (queda 0; no hay ningún escritor en `Services/`). Por lo tanto los KPIs `MoraTotal` / `MontoMora` de la bandeja y de Clientes en Mora **muestran $0 hoy**, no «mora estimada». La cifra basada en 5 % solo aparece en `LogMora.TotalRecargosAplicados` (pantalla Mora → Logs) y solo cuando se crea una alerta nueva. La exposición de alertas es menor a la descrita. (Ya conocido como CFG-DEF-03 en `qa-cfg-resultados.md`; falta la decisión.)
2. `TipoTasaMora` (Diaria/Mensual) se guarda pero **ningún cálculo lo lee** (CFG-DEF-04): con «Diaria» el cálculo es idéntico al mensual.
3. La ficha de cliente de Mora (`MoraService` ~L1193/1210) muestra `MontoMora = Cuota.MontoPunitorio`, campo legacy congelado: también $0.
4. Hay más semillas de interés que `TasaMoraBase`: `DbInitializer` (plantilla 0,05), `DocumentoConfiguracionCredito.EmpresaInicial.Referencia` (**0,20** diario, datos de referencia) y `PlantillaContratoCreditoService.CrearModeloInicial` (0,20). Con `TasaMoraBase = 0` el contrato imprime el valor semilla de empresa/plantilla, no «sin interés».

## 2. Rutas canónicas

| Salida | Fuente real del valor | Archivo |
|---|---|---|
| Deuda punitoria efectiva | `ConfiguracionPunitorio` vigente → `PunitorioCalculator` → `PunitorioAplicado` (aplicación siempre con confirmación autorizada) | `PunitorioService.cs`, `PunitorioCalculator.cs` |
| Contrato anterior (`ContratoVentaCredito`) | `TasaMoraBase` si `> 0`, si no `PlantillaContratoCredito.InteresMoraDiarioPorcentaje`; se congela en `DatosSnapshotJson` | `ContratoVentaCreditoService.cs` L337-343, L536, L936 |
| Motor documental (contrato/pagaré nuevos) | `TasaMoraBase` si `> 0`, si no `EmpresaConfiguracion.InteresMoraDiarioPorcentaje`, si no la plantilla legada; se congela en `DocumentoGenerado.ContenidoRenderizado` | `DocumentoContextoBuilder.cs` L75-143 |
| Espejo en empresa/plantilla legada | `TasaMoraBase > 0` pisa y persiste el valor en la empresa y en las plantillas activas al guardar la empresa | `DocumentoConfiguracionService.cs` L55, L123-142; `DocumentoConfiguracionCredito.cs` L151-167 |
| Estimación de alertas | `TasaMoraBase` mensual/30; `TipoTasaMora` ignorado | `MoraService.CalcularMora` L2537 |
| Texto con la cláusula | seed del contrato (`DocumentoConfiguracionCredito.TextoContrato` L327 «interés diario … del {{contrato.interesMoraDiario}}»); pagaré legado `{{INTERES_MORA}}% diario` (`DbInitializer` L471). El pagaré sembrado del motor nuevo **no** lleva cláusula de interés. | |

## 3. Qué impide un contrato «sin interés» (con `TasaMoraBase = 0`)

- `tasa is > 0` se evalúa en 4 sitios; con 0/null cae al valor de empresa/plantilla (semilla distinta de cero).
- Contrato anterior: `ContratoVentaCreditoService.ValidarPlantilla` L462 rechaza `InteresMoraDiarioPorcentaje <= 0` («interés por mora diario mayor a cero»).
- Editor de plantilla legada: `[Range(0.0001, 100)]` en `PlantillaContratoCreditoViewModel` y `min="0.0001"` en `Index_tw.cshtml`.
- El motor documental **no** valida: con empresa en 0 y tasa 0 imprime «0,00%» (comportamiento distinto entre los dos flujos).
- Pantalla Mora: `TasaMoraBase` admite 0–100; su ayuda dice «Vacío o 0 no calcula mora», pero no aclara que además deja de pisar el contrato.

## 4. Consumidores con autoridad sobre deuda real

Solo `PunitorioService` (vía `ConfiguracionPunitorio`). `TasaMoraBase` no crea cuotas, no mueve caja, no escribe punitorio. Su único efecto monetario es **textual** (contrato/pagaré) y puede entrar en conflicto con lo exigible: un contrato que diga «5 % diario» mientras el sistema cobra 0 % es una discrepancia entre lo pactado por escrito y lo exigido; en una disputa el texto firmado puede invocarse contra el comercio (y a la inversa si el texto dijera 0 con un punitorio activo).

## 5. Alternativas

Referencias: A, B y C de la §3 del documento de puesta en marcha.

| | A: contrato y alertas leen `ConfiguracionPunitorio` | B: `TasaMoraBase` solo para alertas (mensual, «estimación»); contrato lee punitorio | C: operativa sin código |
|---|---|---|---|
| Elimina discrepancia contrato↔deuda | Sí | Sí (contrato) | No; depende de que alguien fije la tasa a mano |
| Alertas vs punitorio | Coinciden | Siguen distintas (rotuladas) | Distintas |
| Código a tocar | Alto | Medio | Ninguno |
| Riesgo sobre contratos históricos | Bajo (snapshots) | Bajo | Nulo |
| Cambia reglas contables | Sí (alertas pasan a punitorio) | No | No |

Obstáculo común a A y B: el punitorio es «X % cada N días con gracia», no «% diario». `contrato.interesMoraDiario` no puede derivarse de él sin una convención (X/N, y qué hacer con la gracia). Con 0 % la conversión es trivial, con otra tasa es una decisión.

### Diseño evaluado (el que separa los tres conceptos)

1. **Interés contractual** (lo que dice el papel): sale de `ConfiguracionPunitorio` vigente en la fecha de emisión, expresado con sus propios términos (`%` cada `PeriodoDias`, gracia `DiasGracia`) en variables nuevas, p. ej. `contrato.punitorioTexto`, `contrato.punitorioPorcentaje`, `contrato.punitorioPeriodoDias`, `contrato.punitorioGraciaDias`. Si la configuración es 0 %, el texto se emite como «sin interés punitorio» (o la cláusula se omite) en vez de «0,00 % diario». Si no hay configuración vigente (`Ausente`/`Inactiva`) la emisión se bloquea con error claro (nunca se inventa una tasa).
2. **Importe informativo de alertas**: `TasaMoraBase` se renombra/rotula como «estimación mensual de mora para cobranza», con `TipoTasaMora` respetado o eliminado; el monto se persiste o se retira del KPI, pero siempre rotulado «estimado, no es deuda».
3. **Punitorio real**: sin cambios (`PunitorioService`).

**Recomendación: A en su forma de contrato (punto 1) + punto 2 de B para las alertas.** Es decir, el contrato deja de depender de `TasaMoraBase`, y `TasaMoraBase` queda como parámetro de estimación rotulado. Justificación: la fuente única de lo exigible es el punitorio, así que lo que se imprime tiene que salir de ahí; el texto por «% cada N días» evita inventar una conversión a diario; no hay que migrar datos (no se borra ninguna columna). Mientras no se decida: **C** (crédito cerrado).

## 6. Alcance exacto del cambio mínimo (cuando se decida A+B)

Sin migración EF (las columnas `EmpresaConfiguracion.InteresMoraDiarioPorcentaje`, `PlantillaContratoCredito.InteresMoraDiarioPorcentaje` y `ConfiguracionMora.TasaMoraBase` se conservan como legacy).

1. `Services/Documentos/DocumentoContextoBuilder.cs`: `AplicarInteresMoraAsync`/`AplicarEmpresa` leen `IConfiguracionPunitorioService.ObtenerVigenteAsync`; nuevas variables; **cambia el constructor** (hoy `new DocumentoContextoBuilder(context)` en 3 archivos de test y `Program.cs` L223) y registrar las variables en `DocumentoCatalogo.cs` (L122-128, L273 alias `INTERES_MORA`).
2. `Services/ContratoVentaCreditoService.cs`: L337-343 deja de leer `TasaMoraBase`; `ValidarPlantilla` L462 reemplaza el chequeo `> 0` por el estado de la configuración punitoria; `{{INTERES_MORA}}` L936 pasa a la variable nueva (el snapshot `Vendedor.InteresMoraDiarioPorcentaje` se conserva para contratos viejos).
3. `Services/Documentos/DocumentoConfiguracionService.cs` (L55, L123-142) y `DocumentoConfiguracionCredito.cs` (L151-167): dejar de espejar la tasa en empresa/plantillas.
4. Texto sembrado: `DocumentoConfiguracionCredito.TextoContrato` L327, `DbInitializer` L440/L471, `PlantillaContratoCreditoService` L102 (semillas 0,05/0,20 a retirar). Las plantillas ya cargadas en una base real son datos: se corrigen por pantalla o con un script revisado, no por código.
5. UI: `PlantillaContratoCreditoViewModel` (quitar `[Range(0.0001,…)]`), `Views/ConfiguracionContratoCredito/Index_tw.cshtml` (campo solo lectura/oculto), `Views/DocumentosConfig/Empresa.cshtml` (campo informativo), `Views/Mora/ConfiguracionExpandida.cshtml` (rotular y explicar la unidad de `TasaMoraBase`).
6. Mora: `MoraService.GetConfiguracionAsync` (default 5 → decidir), `CalcularMora` (+ `TipoTasaMora`), persistir o retirar `MontoMoraCalculada`; `Helpers/AutoMapperProfile.cs` L453-468 (alias `TasaMoraDiaria`/`PorcentajeRecargo`).
7. Pruebas: las de §8 pasan a afirmar el comportamiento nuevo (fallan a propósito al cambiar la fuente); sumar contrato con punitorio 0 %, con punitorio > 0, y con `Ausente`/`Inactiva` (bloqueo).

## 7. Compatibilidad con documentos existentes

- Contrato anterior emitido: texto y datos en `TextoContratoSnapshot`/`TextoPagareSnapshot`/`DatosSnapshotJson`; `{{INTERES_MORA}}` se resuelve desde el snapshot → no cambia.
- Motor nuevo: `DocumentoGenerado.ContenidoRenderizado` persistido → no cambia (prueba `DocumentoYaEmitido_ConservaElTextoCongelado…`).
- **Riesgo real:** `DocumentoService.RegenerarAsync` vuelve a renderizar con la configuración de hoy. Regenerar un contrato viejo después del cambio imprimiría la cláusula nueva. Debe quedar documentado y, si se quiere, bloquear/advertir la regeneración de contratos emitidos con la variable antigua.
- Hoy no hay contratos reales (crédito cerrado), así que no hay histórico que migrar; el riesgo existe solo hacia adelante.

## 8. Pruebas ejecutadas (datos sintéticos, no alteran reglas)

Nuevas, todas en verde:

- `MoraContractualCaracterizacionTests` (motor documental): con `TasaMoraBase = 5` y punitorio 0 %/30/12 el contrato imprime «5,00%»; con 0 o null imprime el valor de empresa (no «sin interés»); con tasa 0 y empresa 0 imprime «0,00%» sin validar; el documento ya emitido conserva su texto al cambiar la tasa.
- `ContratoVentaCreditoServiceTests.MoraContractual_*` (contrato anterior): tasa > 0 pisa la plantilla y queda en el snapshot; 0/null cae a la plantilla; tasa 0 + plantilla 0 bloquea con «interés por mora diario mayor a cero».
- `MoraConfigQaCfgTests.CFG14d`: con tasa 5 la alerta NO persiste `MontoMoraCalculada` (queda 0) aunque el log registra la mora estimada.

Regresión: 744 pruebas relacionadas (`ContratoVentaCredito`, `Documento*`, `MoraConfig*`, `MoraService*`, `ConfiguracionPunitorio*`) OK, 1 omitida previa (CFG14b, pendiente de decisión). Suite completa no ejecutada.

## 9. Riesgos

- **Contractual:** texto firmado con una tasa distinta de la exigible; con crédito abierto y tasa default 5 el contrato diría «5,00 % diario». Mitigación hoy: crédito cerrado.
- **Contable:** la mora «estimada» de Logs (basada en 5 %) no concilia con `PunitorioAplicado`; usarla como cifra de gestión sería incorrecto.
- **De cambio:** el constructor de `DocumentoContextoBuilder`, los textos sembrados y `RegenerarAsync` son los puntos de regresión; las pruebas de §8 los cubren.
- **De la solución C:** depende de una acción manual previa a abrir crédito (fijar tasa y revisar texto) sin guardia técnica.

## 10. Decisión que falta del propietario

1. **Qué interés debe decir el contrato/pagaré** una vez abierto el crédito: (a) lo mismo que el punitorio vigente (0 %: cláusula «sin interés punitorio»), o (b) otra tasa contractual distinta del punitorio (en ese caso hay que definir su fuente y unidad, y que el sistema la haga exigible; si no, queda una promesa escrita no cobrable).
2. **Si el importe de mora de alertas debe mostrarse** (rotulado «estimado») o retirarse hasta que coincida con el punitorio.
3. Alternativa elegida (A+B recomendada, o C mientras el crédito siga cerrado).

Con esas tres respuestas el cambio de §6 se implementa sin nueva investigación.

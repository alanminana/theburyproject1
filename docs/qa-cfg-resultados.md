# QA-CFG — Configuración de Mora: matriz CFG-01..CFG-16

**Versión probada:** `9504428` (rama `qa/config-tx-20261013`, worktree `theburyproject1-qa-cfg-tx`, limpio salvo el archivo de tests nuevo).
**Entorno:** `dotnet test`, SQLite en memoria (`CustomWebApplicationFactory` + `SqliteRowVersionRotationInterceptor`). No se tocó la base de desarrollo ni se iniciaron stacks Docker.
**Código corregido (WIP sin commit de `main`):** NO probado — ver "Pendiente de coordinación".
**Tests:** `TheBuryProyect.Tests/Integration/MoraConfigQaCfgTests.cs` (39 casos, `Trait QA=CFG`). Sólo usan HTTP real y la API ya existente de `MoraService`, así que compilan sin cambios contra el código corregido.
Comando: `dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~MoraConfigQaCfg"`.

## Comparación de los 16 escenarios por versión

Versiones: **A** = `9504428` (commit antiguo, imagen `qa-9504428`); **B** = `9504428` + WIP de preservación sin concurrencia ni validaciones (corrida `run-wip-preservacion`); **C** = corregida, rama `fix/mora-config-integridad` (`b2de9b62` código, `bb41cc54` imagen/spec `qa-bb41cc5`).

| ID | Escenario | A (antigua) | B (WIP preservación) | C (corregida) |
|---|---|---|---|---|
| CFG-01 | Valores iniciales | PASS | PASS | PASS |
| CFG-02 | Abrir ConfiguracionExpandida | PASS | PASS | PASS |
| CFG-03 | Guardar sin cambios | FAIL | PASS | PASS |
| CFG-04 | Ningún parámetro perdido (35 campos) | FAIL | PASS | PASS |
| CFG-05 | Modificar campo permitido | PASS | PASS | PASS |
| CFG-06 | Sólo cambian los previstos | FAIL | PASS | PASS |
| CFG-07 | Valores inválidos (rangos decimales) | FAIL (CFG-DEF-01) | FAIL | **PASS** (`Range(typeof(decimal))`, cultura invariante) |
| CFG-08 | Límites de acuerdos / condonación | FAIL (CFG-DEF-02) | FAIL | **PASS** (tope server-side + 9 casos de importes incoherentes) |
| CFG-09 | Permisos de acceso | PASS | PASS | PASS |
| CFG-10 | Guardar sin autorización | PASS | PASS | PASS |
| CFG-11 | Antiforgery | PASS | PASS | PASS |
| CFG-12 | Dos administradores | FAIL | FAIL | **PASS** (SQLite y SQL Server real) |
| CFG-13 | RowVersion / concurrencia | FAIL | FAIL | **PASS** (SQLite y SQL Server real) |
| CFG-14 | Tasa diaria / mensual | FAIL (14b) | FAIL (14b) | **BLOCKED**: 14a PASS; 14b omitido (decisión de negocio, ver Fase 4); 14c caracteriza el comportamiento actual |
| CFG-15 | Operaciones nuevas usan parámetros | PASS | PASS | PASS |
| CFG-16 | Operaciones existentes conservan histórico | PASS | PASS | PASS |

**Conteo:** A = 8 PASS / 8 FAIL · B = 11 PASS / 5 FAIL (07, 08, 12, 13, 14) · C = **15 PASS / 0 FAIL / 1 BLOCKED (14)**.
Suite C: `dotnet test --filter FullyQualifiedName~MoraConfig` = 59 superados, 1 omitido (CFG14b), 0 fallos. Filtro `~Mora` completo: 262 superados, 1 omitido, 0 fallos.
Corrida B (evidencia): 9 fallos / 35 superados / 1 omitido sobre 45 tests.

### SQL Server real (stack `cfg`, `BuryQA_CFG`, `127.0.0.1:18094`)

Spec `e2e-tx/config/mora-config-concurrencia.spec.js`, lanzado con `qa-run.ps1 cfg -- run-suite.ps1` (turno exclusivo). Dos sesiones de administrador reales (login con navegador), rowversion nativo.

| Paso | Versión antigua (`qa-9504428`) | Corregida (`qa-bb41cc5`, tras `reset`) |
|---|---|---|
| A guarda `TasaMoraBase=9` (RowVersion `…AF5`→`…B5A`) | guardado | guardado |
| B (formulario viejo) guarda `DiasGracia=20` | **aceptado** (`DiasGracia`=20, RowVersion rota; B no fue rechazado) | **rechazado**: "La configuración fue modificada por otro usuario. Recargá…"; `DiasGracia` sigue en 3, `TasaMoraBase` en 9, RowVersion sin cambio |
| POST sin campo `RowVersion` | — (no ejecutado: el test 1 falló antes) | rechazado: "Falta información de concurrencia"; fila idéntica |
| B recarga y reintenta `DiasGracia=12` | — | guardado (RowVersion `…B5A`→`…B5B`) |
| POST `TasaMoraBase=100.01` | — | "Datos inválidos"; fila idéntica |

Evidencia: `~/.bury-qa/state/release-evidence/cfg-fix-OLD2-*` y `cfg-fix-NEW-*` (00-pre, 02-identidad, 03-suite, 04-post-sql, `evidencia/sqlserver-evidencia.txt`).

## Resultado original contra 9504428 (detalle)

| ID | Escenario | Veredicto | Evidencia |
|---|---|---|---|
| CFG-01 | Valores iniciales | PASS | Dump de 49 campos en la salida de `CFG01_*` |
| CFG-02 | Abrir ConfiguracionExpandida | PASS | 200; publica campos editables + `Id`, `RowVersion`, token |
| CFG-03 | Guardar sin cambios | **FAIL** | 302 correcto, pero ver CFG-04 |
| CFG-04 | Ningún parámetro perdido | **FAIL (esperado, código viejo)** | 35 campos alterados: `TipoTasaMora`, `BaseCalculoMora`, `TasaPrimerMes`… → null; `EscalonamientoActivo`, `TopeMaximoMoraActivo`, `NotificacionesActivas`, `PermitirCondonacionMora`… → false; `MaximoCuotasAcuerdo` → null |
| CFG-05 | Modificar campo permitido | PASS | `TasaMoraBase=7.25`, `DiasGracia=10` persistidos |
| CFG-06 | Sólo cambian los previstos | **FAIL (esperado, código viejo)** | Mismos 35 campos colaterales |
| CFG-07 | Valores inválidos | **FAIL** | Rechazados: -1, 101, "abc", días 91/-1/1.5, puntos 1001, hora inválida, `MaximoCuotasAcuerdo` 0/61, etc. **Aceptados:** `TasaMoraBase=100.01` y `-0.01`, `TasaPrimerMes=100.5` (CFG-DEF-01) |
| CFG-08 | Límites de acuerdos | **FAIL** | 08a PASS (máx. cuotas, entrega mínima, condonación no permitida: exacto pasa, +1 rechaza). 08b FAIL: `PorcentajeMaximoCondonacion` no se valida en servidor (CFG-DEF-02) |
| CFG-09 | Permisos de acceso | PASS | admin 200, sin permisos 403 |
| CFG-10 | Guardar sin autorización | PASS | 403, configuración intacta |
| CFG-11 | Antiforgery | PASS | sin token y con token falso → 400, intacta |
| CFG-12 | Dos administradores | **FAIL** | A guarda tasa 9; B (formulario viejo) guarda días de gracia → 302 sin error |
| CFG-13 | RowVersion/concurrencia | **FAIL (reproducido)** | RowVersion rotó (`q9Zv…`→`TdzF…`) pero B no fue rechazado: `TasaMoraBase` final = 4.5, el cambio de A se perdió en silencio. Confirmado en SQLite; no confirmado en SQL Server real |
| CFG-14 | Tasa diaria/mensual | **FAIL** | 14a Mensual 3%×10d sobre 1000 = 10.00 PASS. 14b Diaria da 10.00 en vez de 300.00: `CalcularMora` ignora `TipoTasaMora` (CFG-DEF-04) |
| CFG-15 | Operaciones nuevas usan parámetros | PASS | 3%→10.00, 6%→20.00; acuerdo nuevo respeta el máximo vigente (12→10 cuotas OK; 4→rechaza) |
| CFG-16 | Operaciones existentes conservan histórico | PASS | `LogMora` previo y alerta existente no cambian al subir la tasa a 9%; acuerdo existente conserva 10 cuotas tras endurecer la config |

**Conteo:** 8 PASS (01, 02, 05, 09, 10, 11, 15, 16) · 8 FAIL (03, 04, 06, 07, 08, 12, 13, 14) · 0 BLOCKED en lo ejecutado.
Los FAIL 03/04/06/12/13 son los defectos ya documentados en `docs/mora-config-preservacion.md` y esperados en 9504428; 07, 08 y 14 son defectos nuevos que **no dependen de la versión**.

## Defectos confirmados

- **CFG-DEF-01 (nuevo, independiente de versión):** `[Range(0, 100)]` con límites enteros sobre `decimal?` redondea el valor antes de comparar (`100.01`→100, `-0.01`→0, `100.5`→100). Aislado sin HTTP en `CFG07c_*`. Afecta `TasaMoraBase`, `TasaPrimerMes/SegundoMes/TercerMes`, `PorcentajeMinimoEntrega`, `PorcentajeRecuperacionScore`. Corrección: `[Range(0.0, 100.0)]` o `typeof(decimal)`.
- **CFG-DEF-02 (nuevo):** `PorcentajeMaximoCondonacion` sólo alimenta una pista de UI (`MoraController.cs:361`); `CrearAcuerdoPagoAsync` acepta condonar 100% de la mora con tope 40%.
- **CFG-DEF-03 (nuevo):** `ProcesarMoraAsync` calcula `moraCalculada` pero nunca la asigna a `AlertaCobranza.MontoMoraCalculada`; sólo termina en `LogMora.TotalRecargosAplicados`. Las bandejas que suman `MontoMoraCalculada` muestran 0.
- **CFG-DEF-04 (nuevo):** `TipoTasaMora` (Diaria/Mensual) se guarda pero ningún cálculo lo lee; `CalcularMora` siempre usa `tasa/100/30`. Además `ContratoVentaCreditoService.cs:321` copia `TasaMoraBase` a `InteresMoraDiarioPorcentaje` (lo trata como diaria): mismo valor, dos interpretaciones distintas (evidencia estática, sin test).
- **Conocidos (doc de preservación):** pérdida de 35 campos al guardar; concurrencia last-write-wins ignorando `RowVersion`.

## Pendiente / BLOCKED

1. **Versión corregida (WIP de Mora sin commit en `main`):** no copié cambios sin commit al worktree. Para ejecutarla basta con commitear/coordinar ese WIP en una rama y correr el mismo comando; se espera que CFG-03/04/06 pasen y que CFG-13 siga FAIL hasta aplicar la corrección de RowVersion. Los resultados de esa corrida deben registrarse en una columna aparte.
2. **SQL Server real (BuryQA_CFG):** CFG-12/13 deben reconfirmarse con `rowversion` nativo (aquí lo emula `SqliteRowVersionRotationInterceptor`); además migraciones y comportamiento de `decimal` reales. No inicié ni detuve Docker (stack `bury-qa-cfg-*` está detenido; no hay protocolo de cola en el repo, coordinar con QA-RELEASE).
3. CFG-14: falta decisión de negocio sobre la semántica de `TasaMoraBase` antes de decidir cuál de los dos cálculos es el correcto.

## Recomendaciones

1. Aplicar la corrección de RowVersion propuesta en el doc de preservación (exigir y asignar `OriginalValue`, traducir `DbUpdateConcurrencyException`) y habilitar `CFG12_13_*` como test de regresión.
2. Corregir `Range` decimal (CFG-DEF-01) y agregar test de borde.
3. Validar `PorcentajeMaximoCondonacion` en `CrearAcuerdoPagoAsync`.
4. Persistir `MontoMoraCalculada` o retirar el campo de las bandejas.
5. Definir una única semántica de `TasaMoraBase`/`TipoTasaMora` y alinear alertas, contrato y punitorios.

## Fase 4 — Consecuencias financieras de implementar `TipoTasaMora` (NO implementado)

Estado actual (verificado, sin cambios en este lote): `CalcularMora` (`MoraService.cs`) trata `TasaMoraBase` siempre como
tasa **mensual prorrateada** (`monto × tasa/100/30 × días`). `TipoTasaMora` se persiste y se muestra pero ningún cálculo lo lee
(`CFG14c` fija este comportamiento). `ContratoVentaCreditoService` copia `TasaMoraBase` a `InteresMoraDiarioPorcentaje`
(la trata como **diaria**) al emitir el contrato. Un mismo número, dos interpretaciones.

Ejemplo sobre 1.000, 3 %, 10 días de atraso: mensual = **10,00**; diaria = **300,00** (30×).

| Si se decide… | Efecto | Riesgo |
|---|---|---|
| Honrar `TipoTasaMora=Diaria` en `CalcularMora` | Toda configuración con Diaria pasa a calcular 30× más en alertas/`LogMora`/punitorios. | Alto: importes visibles al cliente y logs históricos dejan de ser comparables. |
| Dejar `Mensual` como única semántica y retirar/ocultar `TipoTasaMora` | Sin cambio de importes; se alinea el contrato (convertir `TasaMoraBase` mensual → diaria `/30` al copiarlo). | Medio: cambia el texto/valor del contrato nuevo (los emitidos no se tocan). |
| Dejar todo como está | Sin cambio. | La UI promete un régimen que no existe. |

Condiciones para implementar cualquiera: decisión de negocio explícita sobre qué significa `TasaMoraBase` en producción
(consultar qué valores hay en las bases reales), migración sólo hacia adelante (sin recalcular `LogMora`, `AlertaCobranza`,
acuerdos ni contratos emitidos) y activar `CFG14b` como test de aceptación.

## Problemas de negocio todavía pendientes

1. **CFG-DEF-04** — semántica de `TasaMoraBase`/`TipoTasaMora` (arriba).
2. **CFG-DEF-03** — `ProcesarMoraAsync` calcula `moraCalculada` pero no la asigna a `AlertaCobranza.MontoMoraCalculada`.
3. `UpdateConfiguracionAsync` (`GuardarConfiguracion`, sin vista) sin control de concurrencia (ver `mora-config-preservacion.md` §2).
4. Valores que ya se perdieron en bases reales por el bug de preservación: no se reconstruyen (sin cambios retroactivos).
5. Precisión de escala (p. ej. 3 decimales en `PorcentajeMinimoEntrega` `decimal(5,2)`) se redondea en la base; sólo se validó el rango.

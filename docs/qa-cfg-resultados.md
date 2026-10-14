# QA-CFG — Configuración de Mora: matriz CFG-01..CFG-16

**Versión probada:** `9504428` (rama `qa/config-tx-20261013`, worktree `theburyproject1-qa-cfg-tx`, limpio salvo el archivo de tests nuevo).
**Entorno:** `dotnet test`, SQLite en memoria (`CustomWebApplicationFactory` + `SqliteRowVersionRotationInterceptor`). No se tocó la base de desarrollo ni se iniciaron stacks Docker.
**Código corregido (WIP sin commit de `main`):** NO probado — ver "Pendiente de coordinación".
**Tests:** `TheBuryProyect.Tests/Integration/MoraConfigQaCfgTests.cs` (39 casos, `Trait QA=CFG`). Sólo usan HTTP real y la API ya existente de `MoraService`, así que compilan sin cambios contra el código corregido.
Comando: `dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~MoraConfigQaCfg"`.

## Resultado contra 9504428

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

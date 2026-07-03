# Crédito FASE 11 — BCRA determinístico (último intento vs. último éxito)

> Estado: **FASE 11 CERRADA funcionalmente (11A-11D) + cierre documental (11E)**. main == origin/main al iniciar 11E. Fecha documental: 2026-07-03.
> Documentos hermanos: [`credito-flujo-final.md`](credito-flujo-final.md) (consolidado), [`credito-fase-10-mora-scoring.md`](credito-fase-10-mora-scoring.md).

## Objetivo de FASE 11

Cerrar la deuda explícita dejada en FASE 4-10D: "BCRA/aptitud no determinístico" — un error transitorio de la consulta BCRA (timeout, 429, 5xx, JSON inválido) pisaba la última situación crediticia conocida del cliente, haciendo que la aptitud cambiara de resultado según si la última consulta había fallado o no, sin relación con el riesgo real del cliente.

## FASE 11A — Diagnóstico

Confirmado (sin tocar código, sin commit propio):

- `SituacionCrediticiaBcraService.MarcarError` limpiaba `SituacionCrediticiaBcra`/`Descripcion`/`Periodo` en cada error, sin distinguir error transitorio de situación real desconocida.
- `ClienteAptitudService.ConstruirBcraDetalle` solo veía el último intento: si el intento anterior había sido exitoso (situación 0-5) y el siguiente fallaba por timeout, la aptitud pasaba de `Apto`/`NoApto` a `RequiereAutorizacion` sin que cambiara el riesgo real del cliente.
- No existía separación entre "último intento" y "última consulta exitosa" en el modelo (`Cliente`).
- El retry de red existente (`GetWithNetworkRetryAsync`) no cubría `TaskCanceledException` (timeout), solo `HttpRequestException`.

## FASE 11B — Política funcional

Decisión (sin commit propio; desbloqueó 11C):

- Un error transitorio **no** debe borrar la última situación BCRA válida conocida.
- La aptitud debe usar la **última consulta exitosa** cuando exista, aunque el último intento haya fallado.
- Si la última consulta exitosa tenía situación 3/4/5 (bloqueante), el cliente **se mantiene** `NoApto` — un error transitorio no lo "blanquea".
- Si el último intento falla y **no** hay ninguna consulta exitosa previa, el cliente queda `RequiereAutorizacion` (no `NoApto` directo, para no bloquear por un problema de infraestructura).
- Sin CUIL/CUIT, sigue siendo `NoApto` (regla preexistente, sin cambios).
- Se agrega 1 retry a los timeouts, igual que a los errores de red.

## FASE 11C — Implementación: último intento vs. último éxito

**Commit:** `ee3ea963f1ed3770517d721e00893b91fc17addc`

`Models/Entities/Cliente.cs` — 4 campos nuevos, separados de los campos existentes de "último intento":

- `SituacionCrediticiaBcraUltimoExito` (`int?`)
- `SituacionCrediticiaDescripcionUltimoExito` (`string?`, 100)
- `SituacionCrediticiaPeriodoUltimoExito` (`string?`, 10)
- `SituacionCrediticiaUltimoExitoUtc` (`DateTime?`)

Migración aditiva: `20260703161937_AgregarUltimoExitoBcraCliente` (solo agrega columnas nullable, sin tocar datos existentes).

`Services/SituacionCrediticiaBcraService.cs`:

- Nuevo método privado `MarcarExito(cliente, situacion, descripcion, periodo)`: escribe tanto los campos de "último intento" (`SituacionCrediticiaBcra`/`Descripcion`/`Periodo`/`UltimaConsultaUtc`/`ConsultaOk = true`) como los de "último éxito" (los 4 campos nuevos), en el mismo momento (`DateTime.UtcNow` una sola vez). Reemplaza los 3 bloques que antes asignaban esos campos manualmente (sin registro, sin deudas, con situación).
- `MarcarError(cliente, descripcion)` ahora **solo** actualiza los campos de "último intento" (`SituacionCrediticiaBcra = null`, `Descripcion`, `Periodo = null`, `UltimaConsultaUtc`, `ConsultaOk = false`). No toca los campos de "último éxito": si existían, se preservan intactos.
- `MaxNetworkAttempts` baja de 3 a 2 (1 retry, no 2), y `GetWithNetworkRetryAsync` ahora también reintenta ante `TaskCanceledException` (timeout), no solo `HttpRequestException`. `NetworkRetryBaseDelay` baja de 250ms a 200ms fijo (antes escalaba `attempt * delay`).

`Services/ClienteAptitudService.cs` — `ConstruirBcraDetalle` (lógica pura, sin DB) gana 3 parámetros opcionales (`situacionUltimoExito`, `descripcionUltimoExito`, `ultimoExitoUtc`):

- Si hay último éxito (`ultimoExitoUtc.HasValue && situacionUltimoExito.HasValue`), la clasificación (`Situacion`/`Descripcion`/`Evaluada`/`RequiereAutorizacion`/`EsBloqueante`) se calcula **siempre** a partir del último éxito, sin importar si el último intento fue exitoso o no.
- Si el último intento falló (`consultaOk != true`) pero hay último éxito, el mensaje agrega el sufijo `"Último intento BCRA falló. Se usa última consulta válida."` sin cambiar `EsBloqueante`/`RequiereAutorizacion` (la clasificación sigue siendo la del último éxito).
- Sin último éxito, se mantiene el comportamiento previo: sin CUIL/CUIT → bloqueante; sin ninguna consulta registrada → bloqueante; último intento fallido → `RequiereAutorizacion`; consulta exitosa sin situación informada → `RequiereAutorizacion`; situación 0/1 normal, 2 requiere revisión, ≥3 bloqueante.

Archivos: `Models/Entities/Cliente.cs`, `Services/ClienteAptitudService.cs`, `Services/SituacionCrediticiaBcraService.cs`, migración `20260703161937_AgregarUltimoExitoBcraCliente` (+ Designer + snapshot), `TheBuryProyect.Tests/Unit/ClienteAptitudServiceTests.cs`, `TheBuryProyect.Tests/Integration/SituacionCrediticiaBcraServiceTests.cs`.

## FASE 11D — UI: chip y refresh AJAX sincronizados

**Commit:** `c1a541cb19892b3b0f578e9f5f568234e11880ec`

Antes de FASE 11D, `Views/Cliente/Details_tw.cshtml` clasificaba el chip de BCRA con su propia copia de reglas (`bcraOk ? "Consulta OK" : "Pendiente"`), ignorando el último éxito agregado en 11C: un error transitorio mostraba "Pendiente" aunque hubiera una situación válida reciente, y el refresh AJAX (`cliente-details.js`) tenía la misma limitación.

- `Controllers/ClienteController.cs` (endpoint AJAX de refresh BCRA): reusa `ClienteAptitudService.ConstruirBcraDetalle` (el mismo clasificador puro que usa la tarjeta de aptitud) en lugar de duplicar reglas. Devuelve además `tieneCuil`, `nuncaConsultado`, `usandoUltimoExito` y `mensaje`, y usa `detalle.Situacion`/`detalle.Descripcion` (que ya resuelven al último éxito cuando corresponde) en vez de los campos crudos de "último intento". También propaga los 4 campos de último éxito desde `SituacionCrediticiaBcraService` hacia el `ViewModel` en el flujo de actualización manual.
- `ViewModels/ClienteViewModel.cs`: expone los 4 campos de último éxito BCRA (default `null`).
- `Views/Cliente/Details_tw.cshtml`: la situación/descripción mostradas salen de `apt?.Bcra?.Situacion`/`apt?.Bcra?.Mensaje` (mismo clasificador que la aptitud), y el chip de cabecera (`id="bcra-chip"`) distingue 4 estados en vez de 2:
  - `Sin CUIL` (sin CUIL/CUIT cargado);
  - `Sin consultar` (nunca se consultó y no hay último éxito);
  - `Consulta OK` (último intento exitoso);
  - `Usando ultima consulta valida` (último intento falló pero hay último éxito) — con aviso adicional (`id="bcra-aviso"`, `role="status"`) mostrando el mensaje de `apt.Bcra.Mensaje`;
  - `Error BCRA` (último intento falló y no hay último éxito).
- `wwwroot/js/cliente-details.js`: el refresh AJAX sincroniza el mismo chip (`bcra-chip`/`bcra-chip-icon`/`bcra-chip-label`) y el aviso (`bcra-aviso`) usando los campos nuevos del endpoint (`tieneCuil`, `nuncaConsultado`, `usandoUltimoExito`, `mensaje`), reemplazando la lógica previa que solo distinguía `data.ok` true/false.
- Test de contrato nuevo: `TheBuryProyect.Tests/Unit/ClienteDetailsBcraUiContractTests.cs` — verifica que el chip tenga los 5 labels esperados, que ya no exista el patrón `bcraOk ? "Consulta OK" : "Pendiente"`, que la vista use `apt?.Bcra?.Situacion`/`Mensaje` en vez de una copia local, que el JS tenga los selectores/campos de sincronización, y que el `ViewModel` exponga los 4 campos de último éxito con default `null`.

Archivos: `Controllers/ClienteController.cs`, `Services/Models/SituacionBcraResult.cs`, `Services/SituacionCrediticiaBcraService.cs`, `ViewModels/ClienteViewModel.cs`, `Views/Cliente/Details_tw.cshtml`, `wwwroot/js/cliente-details.js`, `TheBuryProyect.Tests/Unit/ClienteAptitudServiceTests.cs`, `TheBuryProyect.Tests/Unit/ClienteDetailsBcraUiContractTests.cs` (nuevo).

## Regla final implementada

- Consulta BCRA **exitosa** → actualiza tanto el "último intento" como el "último éxito" (mismo timestamp).
- Consulta BCRA **fallida** (error transitorio: timeout, 429, 5xx, JSON inválido) → actualiza **solo** el "último intento"; el "último éxito" previo, si existe, se preserva sin cambios.
- La aptitud crediticia (`ClienteAptitudService.ConstruirBcraDetalle`) clasifica **siempre** por el último éxito cuando existe, sin importar si el intento más reciente falló. Situación bloqueante (3/4/5) previa se mantiene `NoApto` aunque el último intento haya fallado.
- Error en el último intento **sin** ningún éxito previo registrado → `RequiereAutorizacion` (no bloquea directo por un problema de infraestructura).
- Sin CUIL/CUIT → `NoApto` (regla preexistente, sin cambios).
- Timeout de red reintenta 1 vez (2 intentos totales), igual que error de red — antes solo el error de red reintentaba, y hasta 3 intentos.
- La UI (`Cliente/Details`, chip + refresh AJAX) distingue los 4 estados (`Sin CUIL` / `Sin consultar` / `Consulta OK` / `Usando ultima consulta valida` / `Error BCRA`) reusando el mismo clasificador puro que la aptitud, en vez de duplicar reglas o colapsar todo error en "Pendiente". Cuando se usa la última consulta válida, se muestra un aviso explícito al usuario.

## Servicios y archivos involucrados

- `ClienteAptitudService.ConstruirBcraDetalle` — clasificador puro (sin DB), único punto de verdad para situación/mensaje efectivos; reusado tanto por la tarjeta de aptitud como por el endpoint AJAX y la vista de detalle.
- `SituacionCrediticiaBcraService` — consulta a la API pública de BCRA (Central de Deudores); `MarcarExito`/`MarcarError` separan qué campos toca cada resultado.
- `ClienteController` (acción de refresh AJAX de BCRA) — expone el resultado del clasificador puro al frontend.
- `Cliente/Details_tw.cshtml` + `cliente-details.js` — chip de cabecera y aviso sincronizados en carga inicial y en refresh AJAX.

## Validaciones

- FASE 11C: build principal y build de tests OK, 0 errores / 0 advertencias. Tests focalizados: **119/119 OK** (`ClienteAptitudServiceTests` + `SituacionCrediticiaBcraServiceTests`).
- FASE 11D: build principal y build de tests OK, 0 errores / 0 advertencias. Tests focalizados: **400/400 OK** (incluye `ClienteDetailsBcraUiContractTests` nuevo + `ClienteAptitudServiceTests` + contratos de `Cliente/Details` relacionados).
- FASE 11E (este cierre): `git diff --check` OK; build principal y build de tests OK, 0 errores / 0 advertencias. Sin tests focalizados porque el cambio es docs-only.

## Commits FASE 11

| Sub-fase | Commit | Descripción |
|---|---|---|
| 11A | Sin commit propio | Diagnóstico BCRA no determinístico |
| 11B | Sin commit propio | Política funcional: conservar último éxito |
| 11C | `ee3ea96` | fix(credito): conservar ultimo exito bcra ante errores |
| 11D | `c1a541c` | fix(ui): sincronizar estado bcra en detalle de cliente |
| 11E | Este documento | Cierre documental de FASE 11 |

## Deuda remanente

- Modo simulación BCRA (para QA/desarrollo sin depender de la API pública real) queda pendiente si Javo lo pide en una fase futura.
- `ImpactarScorePorMora` (`ConfiguracionMora`) sigue sin exposición en UI de configuración (deuda heredada de FASE 10, no tocada en FASE 11).
- Entidad/tabla histórica `EvaluacionCredito`/`EvaluacionesCredito` sigue pendiente de decisión futura (conservar datos o dropear en migración separada) — no se tocó en FASE 11.

---

## Estado de cierre documental

- Archivo documental creado: `docs/credito-fase-11-bcra-deterministico.md`.
- Archivo documental actualizado: `docs/credito-flujo-final.md`.
- No se modificó código productivo.
- No se modificaron tests.
- No se modificó UI.
- No se tocaron stashes.
- No se hizo push sin confirmación.
- No se avanzó a FASE 12.

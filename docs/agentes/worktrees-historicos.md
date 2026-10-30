# Worktrees históricos de `D:\git\theburyproject1-*`

Inventario del 2026-10-30 sobre `origin/main` = `216e8166` (Hito 9, M1 Git único). **No se retiró ningún worktree ni rama**: la columna
"Recomendación" es una propuesta y cada retiro requiere autorización del usuario (Bloque 3 del rescate Git, ver `STATE.md`).

Método: por cada rama, `git cherry origin/main HEAD` (commits cuyo parche no está en `main`; detecta squash-merges), `git merge-base --is-ancestor`,
`git status -s` (cambios sin guardar) y, para lo sin commit, comparación de líneas contra `main`. Las PR citadas se verificaron con `gh pr list`.

Estados: **contenido en main** (0 commits únicos), **integrado en esta PR**, **ajeno activo** (trabajo del usuario o de otro agente en curso), **incompleto**
(cambios sin guardar que no están en ningún commit), **redundante** (versión intermedia superada por `main`).

## 1. Contenido en `main` (0 commits únicos; retiro seguro)

| Ruta (`D:\git\...`) | Rama | Evidencia | Recomendación |
|---|---|---|---|
| theburyproject1-com-s-005 | fix/com-s-005-adjuntos-tickets-20261021 | 1 commit, cherry 0 (squash, PR #116) | retirar worktree y rama |
| theburyproject1-fin-s-006 | fix/fin-s-006-acreditar-movimiento-idor | 1 commit, cherry 0 (PR #106) | retirar |
| theburyproject1-integracion | integracion/qa-20261014 | 8 commits, cherry 0 | retirar |
| theburyproject1-integracion-v2 | integracion/qa-v2-20261014 | 21 commits, cherry 0 | retirar |
| theburyproject1-mora-cfg-fix | fix/mora-config-integridad | 3 commits, cherry 0 | retirar |
| theburyproject1-mora-clientes | qa/mora-clientes-ficha-20261014 | 1 commit, cherry 0 (PR #75) | retirar |
| theburyproject1-prd-p0 | fix/qa-prd-p0-permisos | 2 commits, cherry 0 | retirar |
| theburyproject1-prd-p1 | fix/qa-prd-p1-integridad | 4 commits, cherry 0 | retirar |
| theburyproject1-qa-fin-conc | qa/fin-conc-20261014 | 3 commits, cherry 0 | retirar |
| theburyproject1-qa-fin-conc-p0 | qa/fin-conc-p0-20261014 | 6 commits, cherry 0 | retirar |
| theburyproject1-qa-fin-tx | qa/financiero-tx-20261013 | 1 commit, cherry 0 | retirar |
| theburyproject1-qa-prd-tx | qa/productos-tx-20261013 | 1 commit, cherry 0 | retirar |
| theburyproject1-qa-v3 | integracion/qa-v3-runtime-b4-20261014 | 25 commits, cherry 0 | retirar |
| theburyproject1-sec-s-003 | fix/sec-s-003-autoregistro-20261021 | 2 commits, cherry 0 (PR #118) | retirar |
| theburyproject1-seg-p0 | fix/seg-p0-usuario-inactivo | 1 commit, cherry 0 | retirar |
| theburyproject1-h9-m2-cotizador, -h9-m4-iva-qa, -h9-m5-recup | task/hito9-m{2,4,5}-* | HEAD = `216e8166` (ancestro de main), limpios, sin commits propios | ajenos activos (Hito 9): no tocar; retirar al cerrar cada micro-lote |

Retiro preparado (Hito 10): `scripts/git/retirar-worktrees-redundantes.ps1` es DRY-RUN por defecto; con `-Ejecutar` retira solo el worktree de las 15 ramas de esta sección si en ese momento están limpias y sin commits únicos (`git cherry`). Conserva siempre la rama y sus commits y no usa `--force`. Verificado en dry-run el 2026-10-30: las 15 cumplen. Falta la autorización específica para ejecutarlo.

**Retirados el 2026-10-30 (autorizado por el propietario):** com-s-005, fin-s-006, integracion, integracion-v2, mora-cfg-fix, mora-clientes, prd-p0, prd-p1, qa-fin-conc, qa-fin-conc-p0, qa-fin-tx, qa-prd-tx, qa-v3, sec-s-003 y seg-p0 (15 de 15 superaron los controles: limpios, sin archivos sin seguimiento y sin commits únicos). Las ramas y sus commits se conservan. Los archivos ignorados por Git (evidencias de QA, uploads de prueba, claves de desarrollo; 345 archivos) se copiaron antes a `D:/tmp/worktrees-retirados-ignorados/<nombre>/`. Los demás worktrees de este inventario no se tocaron.

## 2. Trabajo único integrado en esta PR

| Ruta | Rama | Qué había sin integrar | Resultado |
|---|---|---|---|
| theburyproject1-sec-s-005 | fix/sec-s-005-sesion-bloqueo-20261021 (HEAD = `3ab74bf9`, ancestro de main) | `docs/sec-s-005-bloqueo-y-sesiones.md` (17 líneas) y +199 líneas de tests en `UsuarioInactivoRevocacionHttpTests.cs`, sin commit | **El doc NO se integra: contradice a `main`.** Afirma que `ActiveUserCookieValidator` no consulta `LockoutEnd`; `main` (PR #128, `Services/ActiveUserSignInManager.cs` L71-76 y `ActiveUserCookieValidatorTests`) expulsa la sesión con bloqueo vigente. Los tests se rescataron **invertidos** a la política vigente: sesión abierta + bloqueo automático/administrativo/temporal -> 401; nuevo login rechazado en bloqueo y permitido con bloqueo vencido o tras desbloqueo; SuperAdmin bloqueado conserva rol y `Activo` pero pierde la sesión; stamp rotado y usuario inexistente revocan. 26/26 verdes junto con `ActiveUserCookieValidatorTests`. Retirar worktree |
| theburyproject1-qa-fin-p0 | qa/fin-p0-integridad-20261014 (2 commits, cherry 0) | `e2e-tx/financiero/financiero-concurrencia.spec.js` (165 líneas) y `playwright.concurrencia.config.js`, sin commit | **Integrado.** Es la carrera a nivel HTTP contra el stack Docker de QA (`bury-qa-fin`, puerto 18093; cancelación doble y cobro vs cancelación). Complementa, no duplica, los tests SQL Server de servicios (`CancelarVsPago*SqlServerTests`): estos prueban el lock; el spec prueba el flujo completo por formularios. Solo se verificó `node --check`: requiere `financiero.spec.js` previo y el stack `~/.bury-qa`, y no es parte de CI. `evidencia/` ya está en `.gitignore`. Retirar worktree |

## 3. Redundantes (versión intermedia superada)

| Ruta | Rama | Evidencia | Recomendación |
|---|---|---|---|
| theburyproject1-qa-fin-sql-historico | qa/fin-sql-historico-r01-r10-20261017 | 1 commit (cherry 1, ~75% de líneas en main). Lo que falta en `main` es el texto "R-01 y R-10 DEFECTOS ABIERTOS, tests en rojo, fuera del CI": en `main` esos defectos (H-CAJA-4, H-FACT-1) están **corregidos** (#87 FIN-CAJA-4-P0, #88 FIN-FACT-REFERENCIA-P0) y `FinResidualRegistroDefectosConocidosTests` / el workflow `fin-dev-sqlserver.yml` ya reflejan eso | redundante: retirar |
| theburyproject1-qa-fin-residual | qa/fin-residual-20261014 | 7 commits, 6 en main (CONC-P0, P0); el último (`24973bfe`, solo tests+doc de auditoría) existe en `main` en versión posterior (`FinResidual*Tests`, `docs/qa-fin-residual-auditoria.md`); las líneas sin equivalente son código previo a #85-#88 | redundante: retirar |
| theburyproject1-diag-fin | (detached `f9e1440b`, ancestro de main) | sin commit: `FinResidualSqlServerTests.cs` (578 líneas vs 1726 en main), `FinResidualCajaDevolucionAnticipoTests.cs` (532 vs 1501), `docs/qa-fin-residual-auditoria.md` y un diff en `CancelarVsPagoServiciosRealesSqlServerTests.cs`: borradores anteriores de archivos que `main` ya tiene completos | redundante: retirar |

## 4. Con cambios sin guardar (no tocados)

| Ruta | Rama | Contenido sin commit | Estado / recomendación |
|---|---|---|---|
| theburyproject1 (checkout principal) | trabajo/cambios-pendientes (`c82c265d`, ancestro de main) | `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, skills `.agents/skills/*` (17), `.claude/skills/codex-review-protocolo/`, `.codex/agents/`, `h4e2-snap.yml` | ajeno activo (usuario); no tocar |
| theburyproject1/.claude/worktrees/agent-a1bd238bb586b3fb2 | worktree-agent-a1bd238bb586b3fb2 (`10eae410`, ancestro) | solo `.claude/settings.local.json` modificado | menor: retirar tras confirmar que el cambio es descartable |
| theburyproject1-agent-e1 | chore/agent-controles-e1-20261023 (`31e3dd16`, ancestro) | 169 archivos sin commit: guard de agentes E1 (`.claude/hooks/agent-guard.*`, `.codex/hooks.json`, `.codex/hooks/agent-guard.*`, `docs/agentes/CONTROLES-E1.md`), `.claude/settings.json` (+245 líneas), `settings.local.json`, skills y `.codex/agents` | **incompleto, trabajo único** (E1 no está en `main`); `STATE.md`: no tocar E1 sin autorización. Conservar hasta decidir E1 |
| theburyproject1-t-pilot-n0-doc | task/pilot-n0-doc (`31e3dd16`, ancestro) | 162 archivos sin commit: skills `.agents/skills/*`, `.codex/agents/*.toml`, `docs/agentes/PILOTO-N0.md` | **161 de sus 162 archivos son idénticos byte a byte a los de agent-e1** (sha1); difiere solo `PILOTO-N0.md` (únicamente aquí; 2 líneas, resultado del piloto N0) y los 8 archivos E1 existen solo en agent-e1. Redundante salvo `PILOTO-N0.md`. Retirar con el visto bueno del usuario |
| theburyproject1-coord-codex | chore/coordinacion-codex-claude-20261023 (PR #102 mergeada) | `docs/agentes/delegations/pilot-n0-doc/task.md` (tarea del piloto; no está en ningún otro worktree) | texto de delegación del piloto, sin valor de producto. Retirar tras guardar el `task.md` si se quiere |
| theburyproject1-qa-cfg-tx | qa/config-tx-20261013 (`95044284`, ancestro) | `MoraConfigQaCfgTests.cs` (545 líneas, 2 no están en main) y `docs/qa-cfg-resultados.md` (43, 1 no está) | contenido en main (versiones finales); retirar |
| theburyproject1-qa-seg | qa/seguridad-e2e (`95044284`, ancestro) | `e2e-tx/seguridad/` (2 archivos idénticos a main; el spec con 0 líneas faltantes) | contenido en main; retirar |
| theburyproject1-h9-m3-cheques | task/hito9-m3-cheques-20261030 | `CajaService.cs` y 2 tests modificados | ajeno activo (Hito 9 M3); no tocar |
| theburyproject1-h9-m1-git-unico | task/hito9-m1-git-unico-20261030 | esta PR | activo |

## 5. Trabajo del usuario: lotes de validación (solo lectura; sin PR propia)

| Ruta | Rama | PR | Contenido |
|---|---|---|---|
| theburyproject1-validacion-l7 | validacion/lote7-mercadolibre | #158 OPEN | VALID-L7 (6 commits; 1 único, el resto merges de main) |
| theburyproject1-validacion-l8 | validacion/lote8-reportes | #163 OPEN | VALID-L8 |
| theburyproject1-validacion-l9 | validacion/lote9-devolucion-ticket-alertas | #164 OPEN | VALID-L9 |
| theburyproject1-validacion-l10 | validacion/lote10-servicios-mantenimiento | #187 OPEN | VALID-L10, precios de servicios de venta |
| theburyproject1-validacion-l11 | validacion/lote11-documentos-archivos | #188 OPEN | VALID-L11, `NombreParaMostrar` independiente del SO |
| theburyproject1-validacion-l12 | validacion/lote12-identity-auditoria | #192 OPEN | VALID-L12, paginación y fechas de la auditoría |
| theburyproject1-validacion-int | validacion/integracion-l7-l12 | #193 OPEN | integración de l7..l12 (21 commits, 7 únicos) |
| theburyproject1-validacion-l13 | validacion/lote13-cotizacion (`8594ab9d`, 1 commit sobre `cdfe6113`) | **sin PR** | VALID-L13: observaciones/nombre/teléfono de Cotización ya no se recortan en silencio (se rechazan), teléfono solo números y separadores, vencimiento entre hoy y 2 años, cantidad por línea <= 999.999 y total <= 20M. Archivos: `Services/CotizacionPagoCalculator.cs` (+10), `Services/CotizacionService.cs` (+34), `TheBuryProyect.Tests/Unit/CotizacionCamposValidacionTests.cs` (+77, nuevo). **Independiente de #193**: #193 no toca ningún archivo de Cotización. Falta que el usuario decida abrir su PR |

Ninguno de estos se retira ni se fusiona hasta que el usuario cierre sus PR.

## 6. Resumen

- 40 worktrees (incluidos el checkout principal, éste y los 3 `h9-*` hermanos).
- 15 ramas con 0 commits únicos y limpias (retirables), 3 redundantes (`qa-fin-sql-historico`, `qa-fin-residual`, `diag-fin`), y `qa-cfg-tx` / `qa-seg` con sin-commit ya presente en main.
- Trabajo único rescatado: tests de sesión/bloqueo (adaptados a `main`) y spec e2e de concurrencia HTTP.
- Trabajo único NO rescatado por decisión: doc SEC-S-005 (contradice a `main`), E1 (`agent-e1`), `PILOTO-N0.md`.
- Pendiente del usuario: VALID-L13 sin PR; PR #158, #163, #164, #187, #188, #192, #193 abiertas.

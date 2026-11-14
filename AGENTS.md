# TheBuryProject — instrucciones compartidas para agentes

## Proyecto

ERP en ASP.NET MVC .NET 8 con C#, EF Core, Razor, Tailwind/CSS, JavaScript y Playwright.

Actuar como desarrollador senior. Priorizar mantenibilidad, estabilidad, accesibilidad y mejoras incrementales seguras.

## Método de trabajo

1. Revisar `git status --short`, `git diff --stat` y el diff relevante.
2. Identificar el camino canónico y clasificar lo tocado como canónico, legacy, duplicado o incierto.
3. Delimitar un único micro-lote y no mezclar problemas independientes.
4. Leer directamente los archivos afectados antes de modificar.
5. Reutilizar servicios, helpers, estilos y scripts existentes antes de crear otros.
6. Implementar el cambio mínimo que resuelva el pedido.
7. Validar según el impacto.
8. Revisar el diff final y reportar evidencia, riesgos y estado del working tree.

No afirmar que algo funciona sin evidencia suficiente.

## Scope y seguridad

- Mantenerse dentro del pedido.
- No borrar código legacy o incierto sin verificar referencias, DI, rutas, vistas, scripts y tests.
- No modificar reglas de negocio sin diagnóstico y cobertura adecuada.
- No introducir dependencias sin revisar primero las versiones y soluciones existentes.
- No tocar migraciones, entidades, backend, frontend y tests juntos salvo que el alcance funcional realmente lo exija.
- No revertir ni sobrescribir cambios previos del usuario.
- No usar archivos de credenciales, autenticación o uploads como contexto.
- Para datos locales de prueba, leer `.agent-local.md` solo cuando sea necesario. Nunca versionarlo.

## Arquitectura y backend

- El backend es la autoridad para cálculos y reglas sensibles.
- Mantener controllers orientados a coordinación.
- Evitar lógica de negocio en controllers, Razor o JavaScript.
- Reutilizar services, fachadas, helpers y mapeos.
- Verificar transacciones, concurrencia e idempotencia cuando corresponda.
- No crear abstracciones de un solo uso sin beneficio claro.
- No duplicar validaciones de negocio entre capas sin una razón explícita.

## Frontend y UI

- Preservar ids, nombres, bindings, rutas y atributos `data-*` que formen parte de contratos existentes.
- No cambiar reglas de negocio por una mejora visual.
- Mantener coherencia con el resto del ERP, legibilidad, contraste, foco visible y navegación por teclado.
- Evitar rediseños decorativos, animaciones innecesarias y estilos ajenos al sistema existente.
- Antes de tocar CSS o Razor visual, reproducir el problema en navegador cuando sea posible.
- No afirmar que una pantalla quedó responsive sin validarla en navegador real.
- No duplicar listeners ni mover lógica crítica al frontend.

Para cualquier trabajo UI/UX:

1. leer `docs/ui/ERP-UI-STANDARD.md`;
2. leer `docs/ui/UI-REFACTOR-STATUS.md`;
3. usar `.claude/skills/modulo-ui-refactor/SKILL.md`;
4. respetar pantallas marcadas como cerradas;
5. actualizar STATUS al cerrar una pantalla;
6. actualizar STANDARD solo ante reglas reusables nuevas.

Toda auditoría UI/UX distingue técnica, visual, flujo UX y estados reales (ver
`ERP-UI-STANDARD.md` §14).

## Herramientas

Usar la herramienta mínima necesaria:

- `codebase-memory-mcp`: dependencias cruzadas, símbolos, referencias, impacto y camino canónico incierto.
- Context7: documentación externa actual, después de verificar la versión instalada en el repositorio.
- Playwright MCP: reproducción y QA interactivo en navegador.
- Tests Playwright versionados: regresiones repetibles y flujos críticos.
- Lectura directa: autoridad final sobre el código real.

Ninguna herramienta reemplaza la lectura de los archivos afectados.

## Validación

Elegir validaciones proporcionales:

- build del proyecto afectado;
- tests focalizados;
- pruebas de integración cuando cambia una regla de negocio;
- Playwright para interacción, JavaScript, responsive o accesibilidad;
- consola y requests para flujos de navegador;
- `git diff --check` antes de cerrar.

No ejecutar la suite completa salvo pre-merge, cierre de fase o pedido explícito.

Si un comando queda colgado, detenerlo, diagnosticar y no repetirlo indefinidamente. Cerrar únicamente procesos iniciados por la ejecución actual.

## Autonomía y autorizaciones

Claude (coordinador y único escritor) ejecuta **sin pedir permiso por cada operación Git**, dentro del alcance técnico seguro de la tarea: commits (locales e intermedios), ramas y worktrees propios, push de la tanda validada, PR, correcciones, revisiones de Codex, CI y fusión normal (sin `--admin`).

**Requiere autorización explícita del usuario**: migraciones productivas sensibles, modificaciones destructivas de datos reales, credenciales o cuentas reales, decisiones funcionales o financieras críticas, cambios de alcance, y cambios de la protección de `main`. Ante duda de si algo entra en esa lista, se consulta.

**Prohibido siempre**: `--admin` (o cualquier bypass) para evitar protecciones, desactivar checks obligatorios, secret-scan o auditorías, y tocar trabajo ajeno (worktrees, ramas, WIP de otros). Una autorización vale solo para lo que nombra.

Roles: con Claude como interlocutor del usuario, **Claude coordina y es el único escritor; Codex solo revisa** (solo lectura). La sección "Coordinación Codex → Claude Code" aplica únicamente cuando el usuario instruye a Codex como interlocutor.

Esta sección rige sobre cualquier frase más antigua de AGENTS.md, CLAUDE.md o skills que exija autorización para commit, push, PR o merge.

## Integración por tandas y CI

Política para minimizar corridas de CI sin perder gates. Procedimiento: `.claude/skills/integracion-por-tandas/SKILL.md`; workflows y propuestas de tiempo: `docs/ci.md`.

- Agrupar tareas técnicas compatibles en una **tanda** con objetivo y criterios de aceptación. **Una PR por tanda compatible**, no por subtarea.
- Durante el desarrollo: pruebas **focalizadas** locales. Los **commits locales intermedios están permitidos y recomendados**; lo que se evita son los **pushes innecesarios**, porque cada push a una rama con PR dispara el CI completo. Se pushea una vez, con la tanda ya validada.
- Antes del push: pruebas focalizadas, `git diff --check`, análisis estático aplicable y control de secretos.
- **Una revisión de Codex por tanda** sobre el diff consolidado. Revisiones adicionales solo si el riesgo lo justifica (hallazgo `high`/`critical` corregido, nuevo código sensible); presupuesto en `codex-review-protocolo`.
- **CI completo sobre el commit final** de la PR. Si cambia el commit (push, rebase, amend, merge de `main`), se esperan los checks del SHA nuevo; nunca se justifica una fusión con checks de un commit anterior.
- Los cinco checks obligatorios de `main` (`build-test`, `docker-build`, `compose-validate`, `secret-scan`, `test`) no se omiten ni se debilitan.
- Ante un check rojo: workflow, job, paso y mensaje exacto → logs → causa raíz → clasificar (código, test, configuración, permisos Git, Docker, runner, dependencia externa) → corregir la causa real. Si parece intermitente, evidencia antes de reintentar. Prohibido ocultarlo con excepciones, `continue-on-error`, skips o allowlists.
- Fusión normal, limpiar solo worktrees y ramas propios y dejar `main` actualizado.
- Agentes/worktrees paralelos solo para tareas independientes, con archivos repartidos de antemano. Un único informe por tanda.

## Revisión independiente (Claude ↔ Codex)

- Claude es el único agente que escribe en un worktree; Codex revisa en solo lectura. Procedimiento, umbrales y registro: `.claude/skills/codex-review-protocolo/SKILL.md`.
- Si actuás como revisor: no modifiques archivos. Reportá solo hallazgos materiales, con severidad, archivo:línea y evidencia (código leído, no suposiciones).
- Prioridad en este repo: permisos/autorización, dinero (Caja, Crédito, Venta, cuotas, mora), transacciones/concurrencia/idempotencia, migraciones EF, hubs SignalR y contratos Razor/JS (ids, data-*).
- No marques estilo ni refactors fuera de alcance, ni repitas hallazgos ya resueltos que figuren en el pedido.

## Git

- No usar `git add -A` ni `git add --all`.
- Commits locales, push, PR, CI y fusión normal: ver "Autonomía y autorizaciones" (no se pide permiso por cada operación Git).
- No limpiar el working tree destruyendo trabajo previo.
- Agregar únicamente archivos del scope (staging selectivo; nunca archivos ajenos ni `.claude/settings.local.json`).
- Antes de cerrar, ejecutar:

```powershell
git diff --check
git diff --stat
git status --short
```

## Coordinación Codex → Claude Code

Aplica cuando el usuario da una instrucción corta ("continuá con los pendientes", "hacé QA", "revisá Ventas", "implementá la siguiente tarea aprobada"). Codex es analista, planificador y verificador; **Claude Code es el único escritor de código**. Si sos Claude ejecutando una delegación, no invoques Codex ni el wrapper, e ignorá `codex-review-protocolo`.

1. **Contexto**: leer `docs/agentes/STATE.md` (pendientes, decisiones, fuentes), `git status`, `git worktree list` y re-verificar lo que vaya a usar. STATE.md es un punto en el tiempo, no autoridad.
2. **Decidir** qué investigar o implementar. Lectura, análisis, planes y verificación los hace Codex; la edición de código la hace Claude. Para lo que no sea escribir (diagnóstico, auditoría), Codex no necesita delegar.
3. **Delegar**: escribir `docs/agentes/delegations/<id>/task.md` desde `docs/agentes/delegations/TEMPLATE.md` (objetivo, alcance, criterios de aceptación verificables, pruebas) y ejecutar, con esta forma exacta (única que la regla de ejecución autoriza fuera del sandbox):

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\git\_agent-coord\theburyproject1\delegate\bin\delegate-claude.ps1 -TaskFile <ruta-absoluta-del-task.md> [-Perfil docs|code] [-BaseRef origin/main] [-TimeoutSec 600] [-MaxBudgetUsd 1.5] [-ResumeSession <sessionId>]
   ```

   Si el sandbox bloquea el comando o pide aprobación, **detenerse e informar**; nunca ejecutar `claude` directo, ni reescribir el wrapper, ni abrir red, ni usar otro mecanismo.
4. **El wrapper** crea el worktree `D:\git\theburyproject1-t-<id>` (rama `task/<id>`) desde `BaseRef`, ejecuta Claude con `acceptEdits` y herramientas acotadas, aplica timeout y bloqueo por tarea, y deja en `D:\git\_agent-coord\theburyproject1\delegate\runs\<id>\` el `result-r<N>.json` (estado, `permissionModeObserved`, salida estructurada de Claude, evidencia git propia), el stream y el prompt.
5. **Verificar de forma independiente** (no basta el relato de Claude): leer `result-r<N>.json`; en el worktree de la tarea revisar con `git -c safe.directory='*' -C <worktree> status|diff` los archivos realmente cambiados contra el alcance; confirmar `headEqualsBase = true` (Claude no hizo commits); correr las pruebas de los criterios de aceptación; contrastar con la salida de Claude.
6. **Corregir**: si falla un criterio, reanudar con `-ResumeSession <sessionId>` y la corrección en el mismo `task.md`. Máximo 2 rondas de corrección (el wrapper lo impone); luego escalar al usuario.
7. **Informar** al usuario: qué se delegó, evidencia real (rutas de `result`, diff, pruebas), criterios cumplidos o no, bloqueos y estado Git. El commit, PR y merge de lo delegado siguen "Autonomía y autorizaciones"; el wrapper no commitea (`headEqualsBase`).

Prohibido: `bypassPermissions`/`--dangerously-skip-permissions`, escrituras simultáneas (un solo escritor por worktree), tocar E1, A5, otros worktrees, hooks Git compartidos o configuración global de Codex/Claude, e invocar delegaciones desde una delegación. Instalación y mantenimiento del wrapper: `docs/agentes/README.md`.

## Entregable

Responder de forma breve y operativa:

1. resumen y veredicto;
2. diagnóstico, solo si fue necesario;
3. cambios aplicados;
4. validaciones ejecutadas;
5. riesgos reales;
6. working tree;
7. commits realizados (hash) o, si no se commiteó, comando `git add` exacto;
8. siguiente micro-lote, solo si aporta valor.

Veredictos válidos:

- `Listo para commit`
- `Requiere ajuste`
- `Bloqueado`

---
name: ui-module
description: Pipeline manual completo de auditoría UX/UI, implementación, QA visual y polish de una pantalla existente del ERP. Solo se ejecuta cuando el usuario escribe /ui-module <módulo>.
argument-hint: "<módulo | pantalla | ruta>  (ej. Dashboard, Productos, Views/Dashboard)"
disable-model-invocation: true
---

# ui-module

Orquestador del pipeline UX/UI de una pantalla existente. **No define criterio de diseño propio**: coordina consultoras y `modulo-ui-refactor`. Las reglas viven en `CLAUDE.md`, `AGENTS.md`, `docs/ui/ERP-UI-STANDARD.md`, `docs/ui/UI-REFACTOR-STATUS.md` y `.claude/skills/modulo-ui-refactor/SKILL.md`; no se copian aquí. La jerarquía de autoridad es la de `CLAUDE.md` ("Criterio de autoridad visual"): `ux-heuristics` e `impeccable` quedan en el último nivel (consultivas).

Argumento: `$ARGUMENTS`.

## Autorización

`/ui-module <módulo>` autoriza, solo para esta ejecución, el uso de `ux-heuristics`, `impeccable` (critique y polish), `modulo-ui-refactor` y Playwright, sin pedir permiso por separado. También cuenta como pedido explícito de revisión/refactor: una pantalla cerrada en STATUS queda reabierta para este alcance. No hace commit salvo pedido expreso.

## Cómo se cargan las consultoras (mecanismo real)

- `impeccable` está `user-invocable-only`: la herramienta Skill no puede invocarla. Se aplica **leyendo sus archivos** con Read: `.claude/skills/impeccable/SKILL.md` (respetar su Setup) y `reference/critique.md` / `reference/polish.md`, y `reference/craft-floor.md` antes de editar UI. Los comandos que critique sugiera son solo orientación; no encadenarlos.
- `ux-heuristics` (Wondel): buscar `SKILL.md` en `.claude/skills/ux-heuristics/` o `~/.claude/skills/ux-heuristics/` y cargarlo con Skill o Read. **Si no existe: detener el pipeline en la Fase 1, informar que falta y dar el comando de instalación; no simularla ni sustituirla.** Instalación oficial (repo `wondelai/skills`): `npx skills add wondelai/skills/ux-heuristics --global`. No instalarla sin autorización.
- `modulo-ui-refactor`: invocable con Skill (o leer su SKILL.md).
- Playwright: seguir `.claude/skills/playwright-protocolo/SKILL.md` y `ui-responsive-protocolo`. Si hay una sesión Playwright ajena abierta, no cerrarla ni reutilizarla: abrir una propia.

## Pipeline

**Fase 0 — Contexto (sin editar).** Resolver `$ARGUMENTS` a pantalla(s) reales con el código (Views, controller, ViewModels, CSS/JS cargados, permisos, contratos); preguntar solo si la ambigüedad es irresoluble. Leer STANDARD, STATUS, `modulo-ui-refactor` y `CLAUDE.md` anidados aplicables. Registrar estado actual del módulo.

**Fase 1 — `ux-heuristics` (sin editar).** Solo usabilidad: Nielsen/Krug, navegación, feedback, estado, errores, recognition vs recall, carga cognitiva, vacíos, claridad de acciones/métricas/filtros/formularios/tablas. Evaluar la UI renderizada real cuando sea posible. Hallazgos: problema, heurística, severidad, evidencia, recomendación.

**Fase 2 — Impeccable critique (sin editar).** Sobre la misma pantalla, enfocado en jerarquía, composición, layout/grid, spacing, densidad, tipografía, cards, tablas, dataviz, viewport, responsive, consistencia con el ERP y estados visuales. No repetir la evaluación Nielsen; conservar discrepancias serias con la Fase 1.

**Fase 3 — Consolidación.** Unir ambos informes, deduplicar, resolver contradicciones con la autoridad de `CLAUDE.md`. Clasificar: funcional/datos, UX, UI, accesibilidad, responsive, opcional; priorizar P0–P3. Producir un único plan. Preguntar solo ante decisiones de producto genuinamente ambiguas.

**Fase 4 — Implementación con `modulo-ui-refactor`.** Es el implementador principal. Implementar P0, P1, P2 aceptados y P3 seguros y relacionados en una sola iteración coherente (sin micro-lotes artificiales). Preservar reglas de negocio, backend, permisos, contratos y datos; no inventar métricas, estados, permisos ni endpoints. Otro comando de Impeccable solo si `modulo-ui-refactor` no puede resolver una necesidad concreta, explicando por qué.

**Fase 5 — QA visual Playwright.** Pantalla real, mínimo `1440x900` y `390x844` más los viewports exigidos por `ui-responsive-protocolo` cuando corresponda. Capturas reales; revisar composición, jerarquía, spacing, alineación, densidad, overflow, tablas, formularios, KPIs, estados (vacío/loading/error/éxito), acciones, consola, requests fallidos y accesibilidad relevante. Comparar contra objetivo del usuario, referencia visual si existe y hallazgos de las Fases 1–3. Corregir en esta iteración lo relevante dentro del alcance.

**Fase 6 — Impeccable polish.** Pasada final de terminación (spacing, alineación, proporciones, tipografía, jerarquía, densidad, consistencia, detalles responsive) según `reference/polish.md`. Sin rediseñar ni tocar negocio, backend, contratos, permisos o datos.

**Fase 7 — QA final.** Repetir Playwright (desktop, mobile, capturas, overflow, consola, requests). Corregir regresiones del polish.

**Fase 8 — Estado y cierre.** Actualizar `UI-REFACTOR-STATUS.md` según reglas existentes; `ERP-UI-STANDARD.md` solo ante una regla nueva y reutilizable. Validación técnica proporcional (build; tests solo si protegen contrato real).

## Resumen final

Módulo; problemas principales; cambios; validaciones; viewports; archivos modificados; riesgos/deuda; estado: `Requiere ajuste` o `Listo para commit`. No usar "cerrado", "terminado", "10/10" ni "Listo para commit" sin evidencia renderizada final (ver "Cierre visual obligatorio" en `CLAUDE.md`). Sin commit automático.

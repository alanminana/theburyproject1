
# Claude Code

## Contexto

`AGENTS.md` es la fuente compartida para Claude Code, Codex y otros agentes. No duplicar aquí sus reglas.

Las instrucciones específicas por carpeta deben vivir en un `CLAUDE.md` anidado o en `.claude/rules/`. Los procedimientos ocasionales deben vivir en skills.

## Skills del proyecto

Claude puede invocar automáticamente estas skills cuando el pedido coincide claramente:

- `modulo-ui-refactor`
- `normalize-razor-structure`
- `clasificacion-warnings`
- `codebase-memory-protocolo`
- `context7-protocolo`
- `playwright-protocolo`
- `tailwind-protocolo`
- `timeouts-y-procesos`
- `ui-responsive-protocolo`
- `csharp-testing`

`ui-ux-pro-max`, `impeccable` y `dotnet-patterns` son consultoras manuales (`user-invocable-only` en `.claude/settings.json`): Claude no las invoca automáticamente, solo mediante `/ui-ux-pro-max`, `/impeccable` o `/dotnet-patterns` cuando el usuario lo pida expresamente. Ver reglas en "UX/UI especializado" y ".NET especializado" más abajo.

## UI/UX

Para cualquier trabajo UI/UX:

1. leer `docs/ui/ERP-UI-STANDARD.md`;
2. leer `docs/ui/UI-REFACTOR-STATUS.md`;
3. usar `.claude/skills/modulo-ui-refactor/SKILL.md`;
4. respetar pantallas marcadas como cerradas;
5. actualizar STATUS al cerrar una pantalla;
6. actualizar STANDARD solo ante reglas reusables nuevas.

Toda auditoría UI/UX distingue técnica, visual, flujo UX y estados reales (ver
`ERP-UI-STANDARD.md` §14).

## MCP

- `codebase-memory-mcp`: configuración personal del usuario.
- `context7`, `playwright` y `github`: configuración compartida del proyecto.
- No usar un MCP `filesystem`; Claude Code ya dispone de herramientas nativas de archivos.

Verificar primero la versión instalada y el código real antes de aplicar información externa.

## Cambios de configuración

`/doctor` sirve para auditar y proponer. No aplicar ni aceptar cambios de configuración sin revisar después `git diff` y los servidores o skills afectados.

### UX/UI especializado

Para refactor de módulos existentes:

- `modulo-ui-refactor` es el orquestador principal.
- `ui-ux-pro-max` e `impeccable` son herramientas consultivas y no reemplazan las reglas del proyecto, el diseño existente ni el alcance definido por `modulo-ui-refactor`.
- No combinar múltiples skills visuales salvo pedido explícito o necesidad concreta.
- `ui-ux-pro-max` puede usarse para heurísticas de UX, accesibilidad, responsive, tablas, formularios y visualización de datos.
- No usar `--persist` de `ui-ux-pro-max` ni crear `design-system/MASTER.md` o overrides de páginas salvo pedido explícito.
- `impeccable` debe invocarse con un comando concreto. Para este ERP priorizar `audit`, `critique`, `harden`, `adapt`, `clarify`, `optimize` y `polish`.
- No ejecutar automáticamente `impeccable init`, `document`, `extract`, `hooks`, `doctor` ni crear `PRODUCT.md` o `DESIGN.md`.
- Las recomendaciones externas no pueden reemplazar fuentes visuales existentes, introducir dependencias, cambiar tipografías, iconografía, navegación o identidad global sin evidencia y autorización explícita.

### .NET especializado

Componentes adaptados de `affaan-m/ECC` a la arquitectura real del ERP (services sobre `AppDbContext`, sin Repository Pattern; stack de test real sin FluentAssertions/Moq/NSubstitute/Testcontainers/Bogus):

- `dotnet-patterns`: referencia consultiva manual para patrones C#/.NET (`/dotnet-patterns`).
- `csharp-testing`: apoyo para tests C# con el stack real de `TheBuryProyect.Tests` (xUnit, `CustomWebApplicationFactory`, SQLite en memoria, stubs manuales); se activa solo ante tareas de testing C#.
- `.claude/agents/csharp-reviewer.md`: agente de revisión C#/.NET; invocación deliberada, no automática en todo cambio `.cs`.

El código real, `AGENTS.md`/`CLAUDE.md` y las skills propias del proyecto prevalecen siempre sobre estas recomendaciones adaptadas de ECC.


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

Las skills visuales genéricas o de estilo están configuradas como `user-invocable-only`. No combinarlas ni activarlas automáticamente. Usarlas solo mediante `/nombre-skill` cuando el usuario pida expresamente esa dirección visual.

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

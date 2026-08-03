
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

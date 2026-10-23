# Coordinación Codex → Claude Code: instalación y controles

Flujo: Usuario → Codex (coordina/verifica) → `delegate-claude.ps1` → Claude Code (escribe) → Codex (verifica) → Usuario. Instrucciones operativas: `AGENTS.md` § Coordinación Codex → Claude Code. Estado vigente: `STATE.md`.

## Por qué hace falta una excepción

El sandbox de Codex en Windows corre como `CodexSandboxOffline` con reglas de firewall que bloquean la red; `claude -p` no puede alcanzar `api.anthropic.com` y se cuelga (verificado 2026-10-23). La excepción es una regla de ejecución oficial de Codex (`prefix_rule` con `decision="allow"`: "ejecuta el comando fuera del sandbox sin preguntar"), limitada a **un solo comando**. El resto de las operaciones de Codex siguen dentro del sandbox, sin red.

## Piezas instaladas (fuera del repo)

| Pieza | Ubicación | Notas |
|---|---|---|
| Regla de ejecución | `C:\Users\c0sm3\.codex\rules\bury-delegate.rules` | Archivo nuevo; `default.rules` no se modificó (respaldo en `_agent-coord\...\backups\`). Solo coincide con `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <wrapper>` |
| Wrapper instalado | `D:\git\_agent-coord\theburyproject1\delegate\bin\delegate-claude.ps1` | Carpeta con ACL protegida: el sandbox solo lee (`CodexSandboxUsers:RX`); Codex no puede alterar el script que la regla autoriza |
| Registros | `D:\git\_agent-coord\theburyproject1\delegate\runs\<id>\` | Misma ACL (el sandbox no puede falsificar resultados) |
| Fuente versionada | `tools/agentes/delegate-claude.ps1` | Copia para revisión; si cambia, reinstalar a mano y comparar SHA-256 |

Reinstalar: copiar la fuente a `...\delegate\bin\` (requiere el usuario, el sandbox no puede escribir ahí) y verificar con `codex execpolicy check --pretty --rules ~/.codex/rules/bury-delegate.rules -- powershell.exe -NoProfile -ExecutionPolicy Bypass -File <wrapper> -TaskFile x`.

## Controles del wrapper

- `-TaskFile` solo acepta `D:\git\theburyproject1*\docs\agentes\delegations\<id>\task.md` (`<id>` = `[a-z0-9-]{3,48}`).
- Un worktree y una rama por tarea (`D:\git\theburyproject1-t-<id>`, `task/<id>`); nunca toca otros worktrees.
- Claude: `--permission-mode acceptEdits`, `--allowedTools` acotado (perfil `docs` o `code`), `--disallowedTools` (git add/commit/push/merge/reset…, `gh`, `codex`, PowerShell, web), `--max-budget-usd`. Sin `bypassPermissions`.
- Bloqueo por tarea (`.lock` con PID), timeout con `taskkill /T /F` solo del árbol lanzado, máximo 2 rondas de corrección, anti-recursión (`BURY_DELEGATION_DEPTH`).
- Evidencia git independiente en `result-r<N>.json`; marca `violacion-commit` si el `HEAD` cambió.

## Límites conocidos

- La regla autoriza el wrapper con cualquier argumento; el wrapper valida `-TaskFile`, `-BaseRef` y `-ResumeSession`. El prompt de la tarea lo escribe Codex: Claude queda acotado por herramientas, no por el texto.
- `~/.codex/rules/` es configuración de usuario (afecta a todos los proyectos de esta cuenta); la regla es específica de la ruta del wrapper.
- Las reglas de Codex son experimentales según su documentación; revisar tras actualizar Codex CLI.

## Revertir

Borrar `C:\Users\c0sm3\.codex\rules\bury-delegate.rules`; opcionalmente borrar `D:\git\_agent-coord\theburyproject1\delegate\`.

# Plantilla de tarea delegada (copiar a `docs/agentes/delegations/<id>/task.md`)

`<id>`: minusculas, digitos y guiones, 3-48 caracteres (ej. `ventas-qa-20261023`). Define el worktree `D:\git\theburyproject1-t-<id>` y la rama `task/<id>`.

```markdown
# Tarea <id>

## Objetivo
(una o dos frases)

## Alcance
Archivos/carpetas que PUEDE tocar:
- ...
Fuera de alcance (NO tocar):
- ...

## Contexto
(decisiones, restricciones, fuentes: archivos, PRs, STATE.md)

## Criterios de aceptacion (verificables por Codex)
1. ...
2. ...

## Pruebas a ejecutar
(comandos exactos; vacio si es solo documentacion)

## Entrega
Responder con status, summary, files_changed, tests_run y blockers reales. No hacer commit, push ni PR.
```

Perfil `docs` (por defecto): solo leer/editar archivos y `git status/diff/log/ls-files`. Perfil `code`: ademas `dotnet build|test|restore` y `npx.cmd playwright test`.

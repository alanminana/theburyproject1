---
name: codex-review-protocolo
description: Protocolo de revisión independiente Claude ↔ Codex (plugin oficial codex@openai-codex) en TheBuryProject: cuándo consultar a Codex, cómo invocarlo sin copiar/pegar, presupuesto de consultas, verificación de hallazgos y registro. Usar en tareas de lógica, seguridad/permisos, dinero, migraciones EF, SignalR o concurrencia, o cuando el usuario pida revisión independiente.
---

# Protocolo de revisión Claude ↔ Codex

Claude coordina e implementa. Codex solo revisa, en solo lectura. Este protocolo aplica la instrucción explícita del usuario y prevalece sobre la regla de la skill interna `codex-result-handling` ("detenerse y preguntar antes de corregir"): Claude corrige los hallazgos **verificados, dentro del alcance autorizado y no sensibles**; todo lo demás se consulta o se lista como pendiente.

## Prohibido siempre
- `--write` en cualquier llamada a Codex; `setup --enable-review-gate`; `transfer`.
- Commit, push, PR, merge, rebase, borrado de ramas: solo con autorización explícita del usuario (AGENTS.md → Git).
- Cambiar configuración global, hooks, otros worktrees o el plugin.
- Reemplazar una revisión de Codex por una opinión propia o simular su resultado. Si Codex falla: informar el error literal y continuar sin su revisión, marcándolo en el informe.

## Niveles
| Nivel | Tarea | Codex |
|---|---|---|
| N0 | Texto, doc, trivial, UI/CSS puro | Ninguno. Registrar igual "N0, sin consulta" |
| N1 | Lógica normal (services, controllers, JS con comportamiento, migraciones aditivas) | 1 `review` al terminar |
| N2 | Seguridad/permisos, dinero, EF con datos, SignalR, concurrencia | 1 consulta de plan (`task` solo lectura) antes de implementar + 1 `adversarial-review` con foco al terminar |

Presupuesto por tarea: ≤1 consulta de plan y ≤2 revisiones finales. La segunda solo si se corrigió algo `high`/`critical`, y enfocada en el delta. `task` de rescate solo tras 2 intentos fallidos o causa raíz desconocida. Ante la duda entre niveles, el menor que cubra el riesgo; no consultar por costumbre.

## Pasos
0. **Aislamiento.** `git status` del worktree de la tarea debe mostrar solo cambios de la tarea. Si hay trabajo ajeno (la revisión nativa lo incluiría), proponer un worktree limpio `D:\git\theburyproject1-t-<slug>` desde `origin/main` y esperar OK del usuario. Claude es el único escritor y no edita el worktree mientras corre una revisión.
1. **Registro.** Copiar `D:\git\_agent-coord\theburyproject1\reviews\PLANTILLA-TAREA.md` a `AAAAMMDD-HHMM-<slug>.md` y completarlo; agregar una línea a `INDEX.md`.
2. **Analizar y planificar** (leer código real, AGENTS.md, skills aplicables). Fijar nivel.
3. **Consulta de plan (solo N2).** Prompt autocontenido: tarea, plan, invariantes, qué riesgos buscar. Pedir respuesta breve con riesgos concretos.
4. **Implementar** solo lo autorizado. Ejecutar las pruebas relevantes antes de revisar.
5. **Revisión de Codex** (N1/N2) sobre el worktree de la tarea. **Antes y después** de cada job calcular un hash del estado del worktree y comprobar que coinciden (Codex no debe escribir); si difieren, detenerse e informar:
   ```powershell
   function Get-WtHash { $x = (git rev-parse HEAD) + (git status --porcelain=v1 | Out-String) + (git diff HEAD | Out-String); $b = [Text.Encoding]::UTF8.GetBytes($x); -join ([Security.Cryptography.SHA256]::Create().ComputeHash($b) | % { $_.ToString('x2') }) }
   $h0 = Get-WtHash   # antes del job
   # ... job ...
   $h1 = Get-WtHash; if ($h0 -ne $h1) { 'ALERTA: el worktree cambió durante la revisión' }
   ```
   Registrar ambos hashes en el registro. Se calcula en memoria (sin archivos intermedios codificados).
6. **Validar cada hallazgo** contra el código real y, cuando sea posible, con una prueba reproductora. Estados: `confirmado` (se corrige), `rechazado` (solo con contraevidencia escrita), `diferido` (real pero fuera de alcance → pendiente), `escalado` (sensible o desacuerdo sin evidencia → usuario decide).
7. **Corregir** confirmados en alcance; volver a probar; re-revisión solo si aplica el presupuesto.
8. **Informe único** al usuario: formato "Entregable" de AGENTS.md + sección "Revisión independiente" (jobs, hallazgos y disposición, pruebas, límites). Actualizar el registro con el estado final.

## Invocación (mecanismo oficial del plugin, sin copiar/pegar)
Localizar el runtime y llamarlo con la herramienta de shell de Claude:

```powershell
$cc = (Get-ChildItem "$env:USERPROFILE\.claude\plugins\cache\openai-codex\codex\*\scripts\codex-companion.mjs" | Sort-Object FullName | Select-Object -Last 1).FullName
Set-Location <worktree-de-la-tarea>
node $cc review --background                       # nativo: cambios sin confirmar (o --base <ref>)
node $cc adversarial-review --background "<foco>"  # con foco; salida JSON estructurada
node $cc task --background "<prompt de solo lectura>"   # consulta de plan / rescate (sin --write)
node $cc status <job-id> --wait --timeout-ms 240000  # repetir hasta completar
node $cc result <job-id>
node $cc status --json                              # comprobar write=false y stopReviewGate=false
```

`review` y `adversarial-review` **no aceptan `--background`**: corren en primer plano y duran minutos. Ejecutarlos con la salida redirigida a un archivo (`... *> $env:TEMP\x.txt`) y en segundo plano del arnés (`run_in_background`); **nunca** cortar su salida con `| Select-Object -First N` (mata el cliente, deja el job en `running` para siempre y `cancel` no puede limpiarlo). Solo `task` admite `--background`. Los ids reales salen de `%TEMP%\codex-companion\<repo>-<hash>\jobs\*.json` (campos `id`, `kind`, `status`, `write`).

Los slash commands `/codex:review` y `/codex:adversarial-review` son solo del usuario; Claude usa el runtime directamente. Los ids de job devueltos se anotan en el registro tal cual.

## Registro
Fuera del repo: `D:\git\_agent-coord\theburyproject1\reviews\` (`INDEX.md`, `PLANTILLA-TAREA.md`, un archivo por tarea). Cada tarea registra: tarea, worktree/rama/HEAD base, nivel, jobs (id, tipo, `write`, estado, veredicto), tabla de hallazgos (ID, severidad, archivo:línea, afirmación de Codex, evidencia de Claude, estado), pruebas, estado final.

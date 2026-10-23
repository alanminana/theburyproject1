# STATE — estado vigente para el coordinador (Codex)

Última actualización: 2026-10-23. Mantenido a mano por quien cierra una tarea; cada línea cita su fuente.
Todo lo de esta página es **punto en el tiempo**: re-verificar contra Git/GitHub antes de actuar (comandos al final).

## 1. Reglas vigentes (no negociables)

| Regla | Fuente |
|---|---|
| Commit, push, PR y merge solo con autorización explícita del usuario | `AGENTS.md` § Git; decisión del usuario 2026-10-23 |
| Claude es el único escritor de código; Codex coordina, planifica y verifica | decisión del usuario 2026-10-23; `AGENTS.md` § Coordinación Codex → Claude |
| Un worktree propio por tarea de código; no tocar worktrees existentes | decisión del usuario 2026-10-23 |
| Máximo 2 rondas de corrección automática por tarea; luego escalar | decisión del usuario 2026-10-23; lo aplica `delegate-claude.ps1` |
| No tocar E1 (guard de agentes), A5 (identidad/credenciales), hooks Git compartidos ni configuración global sin autorización | decisión del usuario 2026-10-23 |
| Sin recursión: una delegación de Codex no puede invocar a Codex | decisión del usuario 2026-10-23; wrapper (`BURY_DELEGATION_DEPTH`) |

## 2. Git (verificado el 2026-10-23)

- `origin/main` = `31e3dd16` (merge de PR #97, FIN-S-002). Verificado con `git fetch origin main` + `git rev-parse origin/main`.
- PRs abiertas (verificado con `gh pr list --state open`): **#98** `fix/com-xss-20261020` (COM-S-001/002 XSS), **#99** `fix/fin-r2-descuento-venta-20261021` (permiso `ventas.discount`), **#100** `fix/sec-credito-configurar-venta-20261022` (ConfigurarVenta), **#101** `chore/codex-review-protocolo` (protocolo Claude-Codex). #98-#100 son Draft.
- El checkout principal `D:\git\theburyproject1` está en `trabajo/cambios-pendientes` (c82c265d) con cambios sin commit de otros agentes (`AGENTS.md`, `CLAUDE.md`, skills): **no trabajar ahí**; usar worktrees.
- Hay ~50 worktrees `D:\git\theburyproject1-*` de tareas previas: no modificarlos. Retirarlos requiere autorización (Bloque 3 del rescate Git).

## 3. Pendientes abiertos (cada uno requiere decisión o autorización del usuario antes de ejecutar)

| Pendiente | Estado | Fuente |
|---|---|---|
| Publicar/mergear PRs de seguridad #98, #99, #100 | Drafts; CI verde reportado en su momento, re-verificar | `gh pr list`; memorias `prompt62`, `prompt76`, `prompt90` |
| FIN-S-002 (#97) ya mergeada; hallazgos A1 (CreditoId forjado en cupo), F1 (backdating `FechaVenta` en Edit), F2/F3 sin corregir | Esperan decisión de corregir | memoria `prompt84-pr97-actualizada-estado` |
| ConfigurarVenta: correr `ConfigurarVentaConcurrenciaSqlServerTests` (env `QA_SEC_CONFIGVENTA_SQL`) cuando el SQL Server local esté libre | Pendiente | memoria `prompt90-configurar-venta-estado` |
| Backlog de la auditoría QA integral (8 hallazgos Altos; varios ya corregidos en PRs #96-#100) | Re-verificar cuáles siguen abiertos antes de planificar | `D:\git\QA-AUDITORIA\10-backlog-correcciones.md`; memoria `qa-integral-prompt52-estado` |
| Rescate Git: Bloques 1-4 (fetch --prune, bundle no-EFS, retirar 18 worktrees limpios, borrar 2 ramas remotas) y decisiones D-1/D-2 | Sin autorización | memoria `rescate-git-p50-estado` |
| E1 (controles de agentes): integrar, A2 (hooks Git compartidos), A5 (identidad/credenciales separadas) | Pendiente de decisión; **no tocar** | memoria `agent-guard-e1-estado` |
| Servidor/Offsite, secretos productivos (host/offsite sin verificar) | Pendiente | memorias `backup-restore-docker-estado`, `secrets-config-productiva-estado` |

## 4. Decisiones funcionales vigentes (resumen; el detalle está en la fuente)

- Descuentos comerciales: permiso independiente `ventas.discount`; validación server-side (PR #99). Fuente: memoria `prompt76`.
- Crédito: el plan de cuotas es la única autoridad del porcentaje; mora nunca exceptuable en el cotizador. Fuentes: memorias `porcentaje-plan-ml-estado`, `cotizador-excepcion-documental-estado`.
- Venta: `TotalACobrar = Total + CostoEnvio`; `TotalFacturable = Total`. Fuente: memoria `venta-envio-total-a-cobrar-estado`.
- Caja: IVA discriminado solo para ventas Facturada/Entregada. Fuente: memoria `caja-iva-discriminado-confirmada-facturada-estado`.
- Pantallas cerradas de UI: ver `docs/ui/UI-REFACTOR-STATUS.md` (autoridad), no esta página.
- Las "memorias" citadas son notas de Claude en `C:\Users\c0sm3\.claude\projects\d--git-theburyproject1\memory\` (índice `MEMORY.md`); son contexto, no autoridad: manda el código y Git.

## 5. Cómo re-verificar antes de actuar

```powershell
git -C D:\git\theburyproject1 fetch origin main
git -C D:\git\theburyproject1 rev-parse origin/main
gh pr list --repo alanminana/theburyproject1 --state open
git -C D:\git\theburyproject1 worktree list
```

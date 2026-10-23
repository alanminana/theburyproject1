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

- `origin/main` = `4ed1f405` (merge de PR #99, permiso `ventas.discount`). Fusionadas en esta secuencia: #98 (XSS, `814c20eb`), #100 (ConfigurarVenta, `ddc890d8`) y #99. Verificado con `git fetch origin main` + `git rev-parse origin/main`.
- PRs abiertas (verificado con `gh pr list --state open`): ninguna. Fusionadas: #98, #99, #100, #101, #102, #103 y #104.
- El checkout principal `D:\git\theburyproject1` está en `trabajo/cambios-pendientes` (c82c265d) con cambios sin commit de otros agentes (`AGENTS.md`, `CLAUDE.md`, skills): **no trabajar ahí**; usar worktrees.
- Hay ~50 worktrees `D:\git\theburyproject1-*` de tareas previas: no modificarlos. Retirarlos requiere autorización (Bloque 3 del rescate Git).

## 3. Pendientes abiertos (cada uno requiere decisión o autorización del usuario antes de ejecutar)

| Pendiente | Estado | Fuente |
|---|---|---|
| ~~Publicar/mergear PRs de seguridad #98, #99, #100~~ | **Hecho** (CI 12-14 checks en verde cada una, revisión adversarial de Codex por PR) | PRs #98, #99, #100 |
| Descuentos (#99): documentos y contratos emitidos ANTES de la marca `conDescuentos` siguen clasificándose por el estado actual de la venta (decisión del usuario, no se cierran en bloque). Las vistas previas de `DocumentosConfigController` aceptan `ventaId` y renderizan datos de esa venta: sin revisar | Abierto; decidir si se revisa | `Services/DescuentoVisibilidadService.cs`; PR #99 |
| Roles personalizados con `creditos.view` pierden "Configurar" crédito hasta que un administrador les asigne `creditos.configure` (#100) | Operativo: avisar al administrador | PR #100 |
| **FE-D-001 (cerrado, PR #103)**: deuda del servicio conservado H4-H6 (ValorPermitido/TipoOperacion del request, sin índice único de umbral, comentarios sin contrato), D2 (lectura sin filtro de propietario) y D3 (autoaprobación posible). Deben resolverse antes de reactivar cualquier endpoint | Diferidas por decisión del usuario | `docs/autorizaciones-circuito-generico-retirado.md` |
| FIN-S-002 (#97) ya mergeada; hallazgos A1 (CreditoId forjado en cupo), F1 (backdating `FechaVenta` en Edit), F2/F3 sin corregir | Esperan decisión de corregir | memoria `prompt84-pr97-actualizada-estado` |
| ConfigurarVenta: `ConfigurarVentaConcurrenciaSqlServerTests` | Cubierto por el job `sqlserver-configventa` del CI (RCSI apagado y encendido, verde en #100 y #99); falta solo correrlo contra el SQL Server local si se quiere | `.github/workflows/sec-configurar-venta-sqlserver.yml` |
| Backlog de la auditoría QA integral (8 hallazgos Altos; varios ya corregidos en PRs #96-#100) | Re-verificar cuáles siguen abiertos antes de planificar | `D:\git\QA-AUDITORIA\10-backlog-correcciones.md`; memoria `qa-integral-prompt52-estado` |
| Rescate Git: Bloques 1-4 (fetch --prune, bundle no-EFS, retirar 18 worktrees limpios, borrar 2 ramas remotas) y decisiones D-1/D-2 | Sin autorización | memoria `rescate-git-p50-estado` |
| E1 (controles de agentes): integrar, A2 (hooks Git compartidos), A5 (identidad/credenciales separadas) | Pendiente de decisión; **no tocar** | memoria `agent-guard-e1-estado` |
| Servidor/Offsite, secretos productivos (host/offsite sin verificar) | Pendiente | memorias `backup-restore-docker-estado`, `secrets-config-productiva-estado` |

## 4. Decisiones funcionales vigentes (resumen; el detalle está en la fuente)

- Autorizaciones genéricas (FE-D-001, 2026-10-23): D1.B, se retiraron los endpoints `/Autorizacion/*`; entidades, tablas, datos y servicio se conservan; el circuito de Venta/Crédito no cambia. U1/D4.A: solo el solicitante cancela su solicitud pendiente, sin excepción administrativa. Sobre `565cea8c` esas rutas respondían 500 (medido con datos sintéticos). Fuente: PR #103 y `docs/autorizaciones-circuito-generico-retirado.md`.

- Descuentos comerciales: permiso independiente `ventas.discount`; validación server-side (PR #99). Un documento emitido con descuentos queda restringido para quien no tiene el permiso aunque después se quiten de la venta (marca `conDescuentos` en su metadata). Fuente: PR #99, memoria `prompt76`.
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

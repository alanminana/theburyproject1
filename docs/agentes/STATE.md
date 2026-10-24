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

- `origin/main` = `3ccb2efd` (merge de PR #116, COM-S-005). Verificado con `git fetch origin main` + `git rev-parse origin/main`. (El SHA cambia con cada merge, incluido el de este archivo: re-verificar.)
- PRs abiertas (verificado con `gh pr list --state open`): ninguna. Fusionadas desde la auditoría QA: #96-#100, #103, #106, el backlog #108-#113 y #115 (FIN-S-007), #116 (COM-S-005) (ver sección 3).
- El checkout principal `D:\git\theburyproject1` está en `trabajo/cambios-pendientes` (c82c265d) con cambios sin commit de otros agentes (`AGENTS.md`, `CLAUDE.md`, skills): **no trabajar ahí**; usar worktrees.
- Hay ~50 worktrees `D:\git\theburyproject1-*` de tareas previas: no modificarlos. Retirarlos requiere autorización (Bloque 3 del rescate Git).

## 3. Pendientes abiertos (cada uno requiere decisión o autorización del usuario antes de ejecutar)

| Pendiente | Estado | Fuente |
|---|---|---|
| ~~Publicar/mergear PRs de seguridad #98, #99, #100~~ | **Hecho** (CI 12-14 checks en verde cada una, revisión adversarial de Codex por PR) | PRs #98, #99, #100 |
| Descuentos (#99): documentos y contratos emitidos ANTES de la marca `conDescuentos` siguen clasificándose por el estado actual de la venta (decisión del usuario, no se cierran en bloque). Las vistas previas de `DocumentosConfigController` aceptan `ventaId` y renderizan datos de esa venta: sin revisar | Abierto; decidir si se revisa | `Services/DescuentoVisibilidadService.cs`; PR #99 |
| Roles personalizados con `creditos.view` pierden "Configurar" crédito hasta que un administrador les asigne `creditos.configure` (#100) | Operativo: avisar al administrador | PR #100 |
| **FE-D-001 (cerrado, PR #103)**: deuda del servicio conservado H4-H6 (ValorPermitido/TipoOperacion del request, sin índice único de umbral, comentarios sin contrato), D2 (lectura sin filtro de propietario) y D3 (autoaprobación posible). Deben resolverse antes de reactivar cualquier endpoint | Diferidas por decisión del usuario | `docs/autorizaciones-circuito-generico-retirado.md` |
| ~~FIN-S-002 (#97): A1, A2, F1, F2, F3~~ | **Corregidos y fusionados** (commit `5f88c7ff`, dentro de #97); cubiertos por FS2-A1/A2a/A2b/F1/F2/F3 en el job `sqlserver-fin-s-002`. Esta fila decía "sin corregir" por una nota anterior al arreglo final: era un error | `git show 5f88c7ff` |
| ~~FIN-S-006 (IDOR `Caja/AcreditarMovimiento`, P1)~~ y fuga de datos de apertura ajena con modelo inválido en `Cerrar`/`RegistrarMovimiento` | **Corregidos y fusionados** (PR #106, CI 14/14, escenarios `S006-01..05` en SQL Server con RCSI 0 y 1, revisión adversarial de Codex). Residuales: la regla de pertenencia vive en el controller y no en `CajaService`; el endpoint no tiene botón en la UI (¿código huérfano? confirmar con negocio); política de Gerente sin cambios | PR #106; `D:\git\QA-AUDITORIA\FIN-S-006-correccion-prompt62.md` |
| **Backlog de la auditoría QA (2026-10-20), verificado contra el código el 2026-10-23 y corregido en #108-#113** | CORREGIDOS: COM-S-003 (#108), FIN-S-005 (#108), COM-S-018 (#108), COM-S-006 y COM-S-007-permisos (#109), COM-S-008 y COM-S-014 (#110), precios <=0 + COM-S-009 filtro vacío (#111), COM-S-004 parte verificable (#112), FIN-S-017 (#113); FIN-S-011 ya estaba corregido. Todos con revisión adversarial de Codex y tests cuya mutación falla | PRs #108-#113 |
| Backlog **pendiente de decisión de negocio** (no se tocó a propósito): FIN-S-004 (¿qué permiso exige `ContratoVentaCredito/Generar`? hoy basta `ventas.view`; Cajero y Contador quedarían sin acceso con `ventas.create`); `AplicarRapido` autoaprueba con solo `precios.apply` (la infraestructura de tests lo modela como rol legítimo); tope de cantidad de productos en el ajuste rápido; restringir `NivelRiesgo` manual a un permiso; ingreso de stock al marcar una OC como Recibida desde `CambiarEstado`; `RevertirBatch` con precios heredados <=0 | Abierto: decidir | PRs #108, #109, #110, #111 |
| Backlog **pendiente de implementación**: FIN-S-018 (doble submit de `Venta/CreateAjax` crea 2 ventas; requiere clave de idempotencia enviada desde la UI), COM-S-010/SEC-D-001 (antiforgery en `[ApiController]` y `Cliente/ActualizarBcra`; requiere que el JS envíe el token), SEC-S-003 (no determinado: probar `Register`/`ForgotPassword` en un stack aislado), firma/secreto y retención de `MercadoLibreWebhookEvents`, montos en JSON `[FromBody]` que no pasan por el binder | Abierto | `D:\git\QA-AUDITORIA\10-backlog-correcciones.md`; PR #112, #113 |
| Backlog corregido después: **FIN-S-007** (#115: el servidor exige el catálogo tipo/concepto manual que ofrece la UI; **el chequeo de saldo/tope en egresos NO se implementó: decisión de negocio pendiente**) y **COM-S-005** (#116: `/uploads/tickets` bloqueado, incluidas variantes `%5c`/`..`/8.3, y descarga autenticada `Ticket/DescargarAdjunto/{id}` con `tickets.view`, sin seguir symlinks). Ambos con revisión adversarial de Codex (COM-S-005: 3 rondas, 2 bypass reales corregidos) | CERRADO (salvo saldo de egresos) | PRs #115, #116 |
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

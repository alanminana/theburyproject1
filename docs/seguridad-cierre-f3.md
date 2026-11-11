# F3 — Seguridad y permisos: evidencia de la ronda de cierre funcional

Fecha: 2026-11-10. Base: `main` cc44bbfc. Entorno: réplica local del stack CI (SQL Server + app Docker en `Production`, 7 usuarios de prueba sintéticos), **nunca `TheBuryProjectDb`**. No se cambiaron usuarios, permisos, credenciales ni datos de la base real.

## 1. Qué se probó y resultado

| Área | Prueba | Resultado | Evidencia |
|---|---|---|---|
| Autorización por URL forzada | Cada rol pide como GET las 30 pantallas que ve el administrador (menú + un nivel de detalle), sin seguir redirecciones | **Sin anomalías.** Anónimo: 30 de 30 → 302 a Login. Ningún rol recibe 5xx ni abre rutas prohibidas por la matriz (Seguridad, Usuarios, Roles, Configuración de pagos para Vendedor/Cajero/Repositor/Técnico; Caja/Crédito/Venta para Repositor/Técnico, etc.). Gerente 19 accesos, Vendedor 13, Cajero 16, Repositor 3, Técnico 3, Contador 11 | `scripts/qa/seguridad/sondeo-url-forzada.js` |
| Revocación de sesión en caliente | Un usuario con sesión abierta; el administrador lo bloquea (`Usuarios/Bloquear`) y luego lo desbloquea | **Correcto.** Antes: 200. Con el usuario bloqueado: la misma sesión recibe 302 a Login en la solicitud siguiente. Tras desbloquear, **la sesión vieja sigue invalidada** (302) y un login nuevo funciona (200) | sondeo ad hoc; respaldado por `ActiveUserCookieValidatorTests` (SEC-S-005) |
| Cambio de permisos con sesión abierta | Retiro de `ventas.authorize` del Vendedor con sesiones abiertas | Ya verificado y documentado: denegado en la solicitud siguiente | `docs/vendedor-sin-ventas-authorize.md` |
| CSRF | `[AutoValidateAntiforgeryToken]` en las APIs y `[ValidateAntiForgeryToken]` en acciones mutantes; contrato que impide agregar una acción mutante sin él | Sin regresiones: las pruebas de contrato corren en cada PR (`build-test`) | `AccionesMutantesTodosLosControllersContractTests`, `ApiAntiforgeryHttpTests`, PR #119 |
| Concurrencia de permisos (H-35) | Guardados y copias simultáneos de permisos de un mismo rol | Sin regresión: los 3 escenarios `H-35` corren en el job `sqlserver-superadmin` (RCSI apagado y encendido) y pasan en los PR #273–#275 | `UltimoSuperAdminConcurrenciaSqlServerTests` |
| IDOR | Caja (`AcreditarMovimiento`, FIN-S-006), descargas de adjuntos de tickets (COM-S-005) y contratos | Ya cerrados con pruebas (PR #106, #116, #125). No se repitió la auditoría: no hay evidencia de regresión | `docs/agentes/STATE.md` |
| Validación de nombres de usuario | Alta por HTTP con nombres límite (tildes, espacios, símbolos, 51 caracteres, salto de línea, duplicados por mayúsculas) | **Defecto real:** `...` y `---` se aceptaban. Corregido en el PR #275 (regla única `UserNameRules`, 14 casos, mutación comprobada). El resto de los casos ya se rechazaba, incluida la duplicación sin distinguir mayúsculas | PR #275 |

Límites honestos: el sondeo de URL forzada cubre 30 rutas GET (lo que el administrador ve en el menú y un nivel de detalle) y **no** POST ni rutas con parámetros de otro usuario; la autorización completa por acción sigue en `docs/permisos-matriz-roles.md` (verificada con 4.469 solicitudes en la ronda anterior) y en los tests HTTP. **P-01** figura en la tarea del tablero maestro y no pude leerlo (no hay conector de Notion en esta sesión): no se afirma nada sobre él.

## 2. Decisión pendiente del propietario: rol `vendedor2`

Hecho (verificado en `docs/vendedor-sin-ventas-authorize.md`, solo lectura el 2026-11-07): el rol `vendedor2` (RolPermisos Id 3042, claim espejo Id 241) **conserva `ventas.authorize`**, **sin usuarios asignados**. El retiro autorizado del 2026-11-07 alcanzó solo al rol Vendedor. **No se modificó.**

Riesgo: hoy es nulo (sin usuarios), pero cualquier alta o asignación futura de ese rol concedería autorizar ventas propias y ajenas, contra la regla vigente (autorizar se reserva a Gerente y superiores).

| Opción | Efecto | Cuándo conviene |
|---|---|---|
| A. Retirar `ventas.authorize` de `vendedor2` (mismo script que el de Vendedor, con su Id) | El rol queda alineado con Vendedor | Si el rol se usa o se podría usar |
| B. Eliminar el rol (sin usuarios, sin dependencias) | Desaparece la ambigüedad | Si es un duplicado de prueba |
| C. Mantenerlo | Sin cambios; riesgo latente | Solo si se asignará a alguien que deba autorizar |

Recomendación: B si es un duplicado de prueba; si no, A. Requiere autorización expresa y respaldo previo (es la base real).

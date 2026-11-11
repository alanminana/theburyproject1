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

---

# Ronda 2026-11-11 — autorización de operaciones que modifican datos (F3.1–F3.5)

Base: `main` 29dff9d9. Entorno: stack Docker propio (proyecto `bury-mc`, app en `Production`, 7 usuarios sintéticos + SuperAdmin `admin_ci` sembrado por el overlay de CI, dataset de los 4 seeders E2E), **nunca `TheBuryProjectDb`**. Evidencia cruda fuera del repo (`D:\tmp\mision\c\`).

## 1. Inventario cubierto

Generado por reflexión sobre los descriptores del runtime y versionado en `scripts/qa/seguridad/inventario-acciones-mutantes.json` (el test `ElInventarioVersionado_EsElActual` falla si una acción mutante cambia y el JSON no se regenera con `BURY_INVENTARIO_OUT`).

- **270 acciones mutantes** (POST/PUT/PATCH/DELETE); 263 evaluadas (7 excluidas por diseño: `NotaRapidaApi` dueño del recurso, `MercadoLibreWebhook` firma externa, `Home`).
- Módulos del alcance: Ventas 16 (Venta, VentaApi, VentaCostoServicios, ServiciosVenta), Caja 9 (incluye cheques: AcreditarCheque/RechazarCheque), Créditos y cuotas/cobros 20, Devoluciones 6, Usuarios 9, Roles/Seguridad 8, Configuración 27 (Pago, Cliente, Rentabilidad, Prefijos, Documentos, Mantenimiento), Acuerdos de pago y mora 14.
- **Las 263 tienen test con huella de base** (antes `AutorizacionMatrizPorRolHttpTests` y los contratos miraban solo el código de respuesta y con ids inexistentes) **y sondeo en vivo**.
- Hallazgos del inventario: ninguna acción mutante de negocio con solo `[Authorize]` (las únicas con solo `*.view` son las excepciones documentadas); ningún GET con permiso más fuerte que su POST salvo los stubs de solo redirección (`Mora/MarcarLeida` y `Mora/Resolver` por GET responden "no disponible por GET"); `Credito/Create|Edit` POST solo redirigen (retirados).

## 2. Pruebas y resultados

**xUnit (`AutorizacionMutantesSinEfectosHttpTests`; SQLite en memoria con cliente, crédito+cuota, caja abierta de otro usuario, venta confirmada, devolución y un SuperAdmin como objetivo):** huella SHA-256 de todas las filas de todas las tablas antes y después.
- 9 roles (Administrador, Gerente, Vendedor, Cajero, Repositor, Técnico, Contador, rol sin permisos, rol solo-`view`): de 31 a 263 acciones denegadas esperadas por rol; **todas 403/AccessDenied y 0 tablas modificadas**.
- CSRF: 187 acciones que el Administrador SÍ puede ejecutar × (sin token, token de otra sesión): **374/374 → 400 y 0 cambios en base**.
- SuperAdmin: control positivo (nunca denegado) y "nunca 500" con ids inexistentes, negativos y fuera de rango (213 acciones × 3 variantes); las acciones con id son no-op en base (excepción: la configuración singleton de crédito se crea con valores por defecto en el primer acceso). Ver defecto 1.

**Sondeo en vivo (`scripts/qa/seguridad/sondeo-post-autorizacion.js`)**: sesiones reales con cookie, token antiforgery válido obtenido de una página accesible, ids reales del dataset, huella `COUNT_BIG` + `CHECKSUM_AGG(BINARY_CHECKSUM(*))` de todas las tablas por `sqlcmd`.

| Identidad | POST prohibidos | Resultado | Cambios en base |
|---|---|---|---|
| Administrador | 30 | 30 AccessDenied | 0 |
| Gerente | 133 | 133 | 0 |
| Vendedor | 215 | 215 | 0 |
| Cajero | 238 | 238 | 0 |
| Repositor | 246 | 246 | 0 |
| Técnico | 245 | 245 | 0 |
| Contador | 246 | 246 | 0 |
| SuperAdmin `admin_ci` | 0 (todo permitido) | n/a | n/a |
| Anónimo | 263 | 263 → Login | 0 |
| CSRF (Administrador, ids inexistentes) | 188 × 2 | 376 → 400 | 0 |

Controles anti falso verde: una redirección a Login con sesión cuenta como anomalía (solo AccessDenied prueba rechazo por autorización); se verifica la sesión viva al final (`/Dashboard` 200); inventario < 150 → exit 3; fallo de login → exit 4; sin datos reales → exit 5; el detector de huella se probó con un `UPDATE` deliberado. **Matriz efectiva (F3.2)**: con token válido e ids inexistentes, cada identidad ejecuta lo que sus permisos sembrados permiten **sin denegaciones inesperadas** (Administrador 188, Gerente 130, Vendedor 48, Cajero 25, Repositor 17, Técnico 18, Contador 17, SuperAdmin 213) y sin 5xx; ninguna discrepancia matriz ↔ comportamiento. Total de la corrida final: 2.648 POST, **0 anomalías**.

## 3. Defectos y correcciones

1. **Reproducido y corregido (bajo): `POST /Caja/Cerrar` con una apertura inexistente devolvía HTTP 500** a un supervisor (SuperAdmin/Administrador pasan la guarda aun con apertura inexistente; luego `TryPopulateCerrarModelAsync` lanzaba "Apertura no encontrada" fuera del `try`). Visto en vivo contra la imagen anterior (Administrador y SuperAdmin) y en xUnit. Corrección mínima en `CajaController.Cerrar`: si la apertura no existe, redirige a Caja con error. Regresión: `CajaCerrar_ConAperturaInexistente_NoDevuelve500` (falla sin la corrección con 500, pasa con ella) y el barrido de SuperAdmin. Sin efectos en base; no explotable (exige rol supervisor).
2. **Observación / decisión pendiente (medio-bajo): el logout no revoca la cookie.** Una cookie copiada antes de cerrar sesión sigue devolviendo 200 tras el logout (la sesión del navegador sí pierde acceso). Es el comportamiento estándar de Identity sin rotar el `SecurityStamp`. La revocación SÍ existe al desactivar, bloquear (manual y por intentos), retirar permisos o rol y resetear contraseña (verificado). Rotar el stamp al cerrar sesión cierra las sesiones del usuario en todos los dispositivos: es un cambio de producto, **no se aplicó**. `SONDEO_ESTRICTO_LOGOUT=1` lo convierte en falla del sondeo.
3. Sin hallazgos en autorización por permiso, CSRF, IDOR por id inexistente, escalada y autootorgamiento (cubiertos por `PrivilegeEscalationHttpTests`, `RolesReservadosHttpTests`, `SuperAdminProteccionHttpTests`, re-ejecutados).

## 4. F3.3 regresiones conocidas (re-ejecutadas)

`VendedorSinAutorizarVentasHttpTests`, `PrivilegeEscalationHttpTests`, `RolesReservadosHttpTests`, `SuperAdminProteccionHttpTests`, `BulkActionAtomicidadHttpTests`, `UsuarioInactivoRevocacionHttpTests`, `ActiveUserCookieValidatorTests`, `UserNameRules`, contratos de acciones mutantes, matriz por rol, Caja/Venta/ML, Devolución y la clase nueva: **659 ejecutadas, 646 correctas, 0 fallidas, 13 omitidas** (las omitidas son las `SqlServerConcFact`, que exigen `QA_SEC_SUPERADMIN_SQL`; H-35 y último SuperAdmin con SQL Server real siguen en el job `sqlserver-superadmin` de CI y no se repitieron aquí). P-01: `ventas.authorize` sembrado solo en SuperAdmin, Administrador y Gerente (consulta SQL en el stack: Vendedor 0) y denegado al Vendedor en el sondeo.

## 5. F3.4 identidades y sesiones (`scripts/qa/seguridad/sondeo-sesiones.js`)

Verificado en vivo: login emite cookie `HttpOnly` y `SameSite=Lax`; `Secure` sigue la política `SameAsRequest` (false sobre el http del stack local; sobre https no medible aquí); retiro de permiso y de rol con sesión abierta → denegado en la solicitud siguiente y restituido al devolverlo sin nuevo login; usuario desactivado y usuario bloqueado → sesión previa 302 y no inicia sesión nuevo aunque la clave sea correcta; 5 intentos fallidos (`MaxFailedAccessAttempts=5`, 5 min) → bloqueo, la clave correcta tampoco entra; reset de contraseña por administrador rota el stamp e invalida la sesión previa. Abierto: defecto 2. Unicidad y validación de nombre de usuario: PR #275. Roles reservados y propio rol: tests de gobernanza (§4). El sondeo restaura el estado del stack al terminar.

## 6. F3.5 rol `vendedor2` — recomendación (decisión del propietario; no se modificó nada)

Hechos del código: `vendedor2` **no existe en la siembra** (`RolesPermisosSeeder` crea solo los roles de sistema y no retira ni recrea roles ajenos); existe únicamente en la base real (rol inactivo, sin usuarios, conserva `ventas.authorize`). `Seguridad/DeleteRol` es baja lógica y solo se bloquea con usuarios **activos**; la siembra no lo recrearía. Su nombre no es reservado (no da autoridad por nombre), pero `PermissionClaimsTransformation` no filtra por el estado del rol: si se le asignara un usuario, `ventas.authorize` entraría en vigor (no verifiqué si la asignación a roles inactivos está bloqueada). **Recomendación: B (eliminar el rol)** si no tiene uso previsto: no hay usuarios ni dependencias, no se recrea y desaparece el riesgo latente; si hay un uso previsto, A (retirar el permiso con el script transaccional del Vendedor adaptando el Id). Evitar C. Ambas exigen autorización expresa y respaldo previo.

## 7. Límites

- Las Razor Pages de Identity (login, cambio de clave propio) no están en el inventario de controllers; las cubren los tests de login y bloqueo existentes.
- Los sondeos prueban **denegación** con ids reales y **aceptación** solo con ids inexistentes: no se ejecutaron en vivo acciones destructivas permitidas sobre datos del dataset.
- Overposting: los view models de Usuarios, Roles, Caja, Devolución y Configuración no exponen campos sensibles (`Estado`, `Total`, `Aprobado`, `IsDeleted`, `UsuarioId`); Venta y Crédito están cubiertos por FIN-S-002 y PROMPT 90. No se escribió un test nuevo de overposting por acción.
- Idempotencia y concurrencia de cobros: ya cubiertas por `CajaMovimientoManualIdempotenciaHttpTests`, `VentaIdempotenciaHttpTests`, `QaPrdP1IdempotenciaConcurrenciaTests` y los `SqlServerConcFact`; no se duplicó.
- No se probó H-35 con SQL Server real ni `Secure` sobre https.

---

## 8. Actualización 2026-11-11 (ronda 2): rol `vendedor2` — eliminación PREPARADA, NO APLICADA

Se preparó y ensayó en un clon la opción B de §2 y §6. Detalle, evidencias y procedimiento en `docs/vendedor2-eliminacion.md`; scripts `scripts/sql/vendedor2-preflight.sql`, `vendedor2-eliminar.sql`, `vendedor2-rollback.sql` y `vendedor2-huella-global.sql`. **No se aplicó nada a `TheBuryProjectDb`; falta autorización expresa del propietario y una ventana.**

- **Veredicto: eliminar.** Rol inactivo, 0 usuarios (incluidos inactivos), sin referencias en ninguna otra tabla; solo 5 eventos de auditoría (texto, sin FK) que se conservan. Reversión exacta probada.
- **Corrección a §2/§6:** `vendedor2` **no es un duplicado exacto** de `Vendedor` (13 permisos solo en `vendedor2`, entre ellos `ventas.delete` y `ventas.authorize`; 19 solo en `Vendedor`) y **sí tuvo un usuario** (JAVIER, 2026-09-29/30, vuelto luego a `Cajero + Vendedor`).
- **Corrección a §6:** mientras el rol esté inactivo, asignarlo **no** hace efectivo `ventas.authorize`: `GetUserEffectivePermissionsAsync` descarta los roles con `RolesMetadata.Activo = 0`. El riesgo real es su **reactivación** (`ToggleRolActivo`, permiso `roles.update`).
- **Vía:** SQL transaccional con respaldo, no la UI: `DeleteRol` no registra auditoría de éxito y borra por cascade sin copia (medido en el clon). El estado final es idéntico al del script.
- El historial de este documento (secciones 1-7) se conserva sin cambios.

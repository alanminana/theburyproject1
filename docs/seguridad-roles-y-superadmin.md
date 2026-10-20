# Gobernanza de roles y cuentas SuperAdmin

Reglas que aplica el **backend** (`GuardiaPrivilegiosService`) en Seguridad → Usuarios/Roles. La UI solo refleja el rechazo.

## Roles reservados

`SuperAdmin`, `Administrador` y `Gerente` conceden autoridad **por nombre** (`IsInRole` en Caja, Venta, MercadoLibre,
autorizaciones), fuera de la matriz de permisos. Rango de autoridad: SuperAdmin 3 · Administrador 2 · Gerente 1.

| Operación | Quién puede |
|---|---|
| Crear un rol con nombre reservado, renombrar un rol hacia ese nombre, duplicar hacia ese nombre | Solo SuperAdmin (sin distinguir mayúsculas ni espacios laterales) |
| Renombrar, eliminar, activar o desactivar un rol reservado existente | Solo SuperAdmin |
| Asignar o quitar `Administrador`/`Gerente` a un usuario | SuperAdmin, o un actor con rango ≥ al del rol, **además** de `usuarios.assignroles` |
| Gestionar (editar, resetear clave, bloquear, etc.) a un usuario que tiene un rol reservado | SuperAdmin, o un actor con rango ≥ al del rol del usuario, además de los permisos que ya se exigían |
| Roles personalizados (cualquier otro nombre) | Sin cambios: se rigen por la matriz de permisos y el control de "permisos que no tenés" |

Consecuencias visibles respecto del comportamiento previo:

- Un Administrador ya no puede eliminar, renombrar ni desactivar `Administrador`/`Gerente`.
- Un rol personalizado con `usuarios.assignroles` (aunque tenga los mismos permisos que Administrador) ya no puede asignar
  `Administrador`/`Gerente` ni administrar cuentas que los tengan.
- Editar la descripción de un rol reservado (sin cambiar nombre ni estado) sigue permitido a quien tenga `roles.update`.

## Operaciones masivas (`Usuarios/BulkAction`)

Son **todo o nada**: si falla cualquier usuario (validación de Identity, bloqueo, asignación de rol, concurrencia,
id inexistente o autorización), se revierte la transacción completa y se descarta el `ChangeTracker`. La auditoría
registra `Acción masiva` solo tras el commit y `Revertido` tras un rollback; nunca se muestra éxito si hubo rollback.

## Invariante del último SuperAdmin

- **Estructural** (siempre): no se puede dejar al sistema sin ninguna cuenta activa con el rol `SuperAdmin` asignado y
  activo (desactivar, eliminar, quitar el rol). Un bloqueo de login (lockout) **no** apaga esta protección.
- **Habilitado** (si hoy se cumple): tampoco se puede dejar sin ningún SuperAdmin que pueda iniciar sesión ahora.
- Las operaciones se serializan con un lock sobre el rol SuperAdmin (`UPDLOCK, HOLDLOCK`) y se revierten por completo
  si violan la invariante.
- La invariante solo se defiende si hoy se cumple: una instalación recién sembrada, o que ya perdió todas sus cuentas,
  no queda trabada.

### Recuperación

No existe un camino administrativo genérico para saltear la invariante. Si la instalación quedó sin ningún SuperAdmin
activo, `DbInitializer` (al iniciar) reactiva y desbloquea **solo** la cuenta administradora configurada
(`Admin:UserName` / `Admin:Email`) si conserva el rol SuperAdmin. Con al menos un SuperAdmin activo no toca nada
(una baja deliberada no se revierte en cada arranque). No crea cuentas ni contraseñas.

## Pruebas

`BulkActionAtomicidadHttpTests`, `RolesReservadosHttpTests`, `SuperAdminProteccionHttpTests`,
`GuardiaPrivilegiosTrackerTests`, `DbInitializerRecuperacionSuperAdminTests` (SQLite, corren siempre) y
`UltimoSuperAdminConcurrenciaSqlServerTests` (SQL Server real; requiere `QA_SEC_SUPERADMIN_SQL` apuntando a una BD
descartable `qa_sec_superadmin*`).

## Riesgos conocidos

- `CopyPermisosRol`/`SavePermisosRol` sobre `Administrador`/`Gerente`: un actor puede agregar o quitar permisos que él
  mismo posee (control existente de "permisos que no tenés"); no escala por encima de sus propios permisos.
- Otros nombres (`Vendedor`, `Cajero`, …) solo definen umbrales configurables de autorización, no autoridad ilimitada.

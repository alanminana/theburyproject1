# Eliminación del rol `vendedor2` — PREPARADO, NO APLICADO

**Estado: PREPARADO, NO APLICADO.** Nada de lo descrito aquí se ejecutó contra `TheBuryProjectDb`. La base real solo se leyó con `SELECT` y se respaldó con `COPY_ONLY` para crear un clon; todo lo que escribe se ensayó en ese clon (`TheBuryProjectDb_v2`, ya eliminado). Fecha de la preparación: 2026-11-11. Base de código: `main` 58c12c2a.

## 1. Veredicto: ELIMINAR (con una salvedad)

Recomendación: **eliminar el rol `vendedor2`** con el procedimiento de este documento (respaldo + reversión exacta probada).

La salvedad es una corrección de lo que se venía diciendo: **no es un duplicado exacto de `Vendedor`**. Es un rol de prueba inactivo, creado por `admin` el 2026-09-29 con la descripción «prueva, STOCK SOLO EN VER, ME PERMITE INGRESAR CANTIDADES, CREAR PRODUDTOS PERO NO ELIMINARLOS», **usado brevemente por el usuario JAVIER** (2026-09-29 y 2026-09-30, según la auditoría) y luego reemplazado por `Cajero + Vendedor`. Hoy no tiene usuarios, está inactivo y nada lo referencia. Sus permisos difieren de los de `Vendedor` (ver 2.3) y varios son **más amplios** (`ventas.delete`, `ventas.authorize`, `cobranzas.applyfine`/`revertfine`/`payinstallment`), así que reactivarlo o asignarlo por error daría más poder que el rol estándar.

| Opción | Efecto | Veredicto |
|---|---|---|
| **Eliminar el rol** | Desaparece la ambigüedad; se respaldan íntegras todas sus filas y se puede restaurar idéntico | **Recomendada** |
| Retirar `ventas.authorize` | El rol seguiría existiendo con `ventas.delete` y permisos de cobranzas que `Vendedor` no tiene | Solo si el propietario quiere conservar el rol |
| Mantener | Riesgo latente: un rol inactivo que alguien puede reactivar | No recomendada |

Corrección a `docs/seguridad-cierre-f3.md` §6: el riesgo «si se le asignara un usuario, `ventas.authorize` entraría en vigor» **no se cumple mientras el rol esté inactivo**: `PermissionClaimsTransformation` calcula los permisos con `GetUserEffectivePermissionsAsync`, que descarta los roles con `RolesMetadata.Activo = 0`. El riesgo real es la **reactivación** del rol (`ToggleRolActivo` exige `roles.update`; `ValidarActivacionRolAsync` impide activar un rol que otorga permisos que el actor no tiene, pero un SuperAdmin puede), no la asignación.

## 2. Evidencias (solo lectura sobre la base real, 2026-11-11)

Todas las consultas fueron `SELECT` sobre `TheBuryProjectDb` (LocalDB `(localdb)\MSSQLLocalDB`). El preflight completo contra la real quedó con veredicto **GO**.

| # | Verificación | Consulta / comando | Resultado |
|---|---|---|---|
| 1 | El rol existe una sola vez | `SELECT * FROM AspNetRoles` (preflight G2) | `Id 2736ed6d-1886-403c-b032-dbe839a87fef`, `Name vendedor2`, `NormalizedName VENDEDOR2`, `ConcurrencyStamp f8d4a412-3690-46a8-b3df-b27d3b8532af`; una coincidencia |
| 2 | No es rol de sistema | Lista de reservados (preflight G3) | Los 8 roles de sistema son otros: SuperAdmin, Administrador, Gerente, Vendedor, Cajero, Repositor, Tecnico, Contador |
| 3 | Cero usuarios, incluidos inactivos | `AspNetUserRoles WHERE RoleId = …` (G4) | **0** asignaciones. (De 12 asignaciones totales, ninguna es de este rol) |
| 4 | Sin FK desconocidas | `sys.foreign_keys` hacia `AspNetRoles` (G5) | Solo 4, todas `ON DELETE CASCADE`: `AspNetUserRoles`, `AspNetRoleClaims`, `RolPermisos`, `RolesMetadata` |
| 5 | Sin columnas de otras tablas que lo referencien | `sys.columns` tipo `%Rol%`/`%Role%` (G6) | Nada. Las únicas columnas «rol» ajenas son `SolicitudesAutorizacion.RolSolicitante` (0 filas con `vendedor2`) y `UmbralesAutorizacion.Rol` (tabla vacía) |
| 6 | Sin referencias por contenido | Barrido de **todas** las columnas de texto de las 133 tablas por `%vendedor2%` y por el Id (G7) | Solo aparece en `AspNetRoles`, `RolPermisos`, `AspNetRoleClaims`, `RolesMetadata` (las propias) y `SeguridadEventosAuditoria` |
| 7 | Entidad extendida | `RolesMetadata` (G8, G10) | 1 fila (Id 1003): `Activo = 0`, `CreatedAt 2026-09-29 19:56:22`, `CreatedBy admin`, `UpdatedAt 2026-09-30 22:50:29`, `UpdatedBy Javier2`, `IsDeleted 0`. Esos datos se respaldan íntegros |
| 8 | Permisos y claims espejo | `RolPermisos`, `AspNetRoleClaims` (G9) | 35 `RolPermisos` (35 vigentes, 0 bajas lógicas) y 35 claims `Permission`; los dos conjuntos coinciden exactamente. Incluye `ventas.authorize` (RolPermisos Id 3042, claim Id 241) |
| 9 | Auditoría histórica | `SeguridadEventosAuditoria` (G11) | 5 eventos mencionan el rol (Ids 2013, 2014, 2015, 2023, 2030: alta, 35 permisos, dos ediciones de JAVIER con `Cajero, vendedor2`, edición del rol). La tabla guarda **texto**, sin FK: la eliminación **no los toca** |
| 10 | Roles parecidos | `Name LIKE '%vendedor%'` (G13) | `Vendedor` y `vendedor pobre` (2 permisos, creado 2026-10-02): **no se tocan** |

### 2.3 Diferencias con `Vendedor` (no es un duplicado)

`Vendedor` tiene 41 permisos vigentes (hoy), `vendedor2` 35; coinciden 22.

- **Solo `vendedor2` (13):** `cobranzas.applyfine`, `cobranzas.payinstallment`, `cobranzas.revertfine`, `cobranzas.view`, `cobranzas.viewalerts`, `cobranzas.viewarrears`, `cotizaciones.cancel`, `creditos.simulate`, `creditos.viewinstallments`, `movimientos.view`, `precios.view`, `ventas.authorize`, `ventas.delete`.
- **Solo `Vendedor` (19):** `caja.close`, `caja.history`, `caja.movements`, `caja.open`, `clientes.create`, `clientes.viewdocs`, `creditos.configure`, `dashboard.view`, `documentos.generate`, `documentos.reprint`, `documentos.sign`, `documentos.view`, `notificaciones.delete`, `notificaciones.update`, `notificaciones.view`, `tickets.create`, `tickets.update`, `tickets.view`, `ventas.requestexception`.

### 2.4 En el código

| Verificación | Resultado |
|---|---|
| `vendedor2`/`Vendedor2` en `*.cs`, `*.cshtml`, `*.js`, seeders, `DbInitializer`, scripts, tests | Solo el test `VendedorSinAutorizarVentasHttpTests` (`Assert.DoesNotMatch(... vendedor2 ...)` sobre el código del sembrador, es decir, comprueba que el sembrador **no** lo menciona) y documentos/scripts de esta serie. **Ningún código lo crea, asigna ni referencia por nombre** |
| ¿Lo recrea el próximo arranque o la siembra? | No. `RolesPermisosSeeder.SeedRolesAsync` crea solo los 8 roles de `Roles.GetAllRoles()`. **Demostrado en el clon:** tras eliminarlo, reinicio de la app con `Database:InitializeOnStartup=true` (migraciones + seeds ejecutados: «Migraciones aplicadas exitosamente», «Roles, módulos, permisos y sucursales inicializados»): el rol **no reaparece** |
| ¿Hay caché de roles/permisos? | No. `GetUserActiveRolesAsync` y `GetUserEffectivePermissionsAsync` consultan la base en cada solicitud. **Demostrado:** con sesiones abiertas de SuperAdmin, Vendedor y Gerente, la eliminación (script) se refleja en la solicitud siguiente, sin reiniciar y sin que se pierdan las sesiones |

## 3. Vía de eliminación: SQL transaccional (no la UI)

La aplicación sí puede eliminar el rol (`Seguridad/DeleteRol` → `RolService.DeleteRoleAsync`; guardas de rol SuperAdmin/reservado y de usuarios **activos**; permiso `roles.delete`). Se ensayó en el clon y se comparó con el script. Se prefiere el script por tres razones medidas:

1. **No deja rastro de éxito.** `DeleteRol` solo registra auditoría cuando *deniega*; al eliminar con éxito el recuento de `SeguridadEventosAuditoria` no cambió (57 → 57). El script registra el evento «Eliminar rol».
2. **No es reversible.** `DeleteRoleAsync` da de baja lógica los permisos, pero luego el `DELETE` del rol dispara los `ON DELETE CASCADE` de las 4 tablas: las 35 filas de `RolPermisos`, los 35 claims, la fila de `RolesMetadata` y las asignaciones de usuarios desaparecen físicamente, sin copia.
3. **Mismo estado final.** Medido en el clon: el borrado de la app y el del script dejan las 5 tablas del rol con huellas idénticas (`AspNetRoles` 9, `RolesMetadata` 9, `RolPermisos` 590, `AspNetRoleClaims` 64). El script hace explícito, con respaldo y verificación, lo que el cascade hace a ciegas.

## 4. Procedimiento (ventana futura, con autorización expresa)

Scripts en `scripts/sql/` (todos con `sqlcmd -I`, `-b`, base explícita):

| Script | Rol | Escribe |
|---|---|---|
| `vendedor2-preflight.sql` | GO / NO-GO por guarda + huella | No (solo `SELECT`) |
| `vendedor2-huella-global.sql` | Filas y suma de verificación de las 133 tablas (para `diff` antes/después) | No |
| `vendedor2-eliminar.sql` | Respalda y elimina | Solo con `Modo=Aplicar` y token |
| `vendedor2-rollback.sql` | Reinserta exactamente lo respaldado | Solo con `Modo=Aplicar` y token |

Seguridad por construcción (probada): falta cualquier `-v`/variable → sqlcmd aborta sin ejecutar nada; `BaseEsperada` distinta de `DB_NAME()` → aborta; `Modo` distinto de `DryRun`/`Aplicar` → aborta; `Aplicar` exige el token con **la fecha de hoy** (`ELIMINAR-VENDEDOR2-AAAAMMDD` / `RESTAURAR-VENDEDOR2-AAAAMMDD`); sobre `TheBuryProjectDb` se exige además `ConfirmarBaseReal=SI` (incluso en `DryRun`). La ruta del archivo de reinsercion va en la variable de entorno `RutaReinsercion` (el sqlcmd 15 rechaza el `:` de `D:\…` dentro de `-v`; sin espacios ni comillas).

Variables comunes (PowerShell): `$S = '(localdb)\MSSQLLocalDB'`, `$D = 'D:\respaldo-vendedor2'`, `$H = (Get-Date).ToString('yyyyMMdd')`.

1. **Respaldo previo de toda la base y verificación** (obligatorio):
   ```powershell
   New-Item -ItemType Directory -Force $D | Out-Null
   sqlcmd -S $S -E -C -I -b -Q "BACKUP DATABASE TheBuryProjectDb TO DISK=N'$D\antes-vendedor2.bak' WITH COPY_ONLY, CHECKSUM, INIT, STATS=10"
   sqlcmd -S $S -E -C -I -b -Q "RESTORE VERIFYONLY FROM DISK=N'$D\antes-vendedor2.bak' WITH CHECKSUM"
   ```
2. **Huella global y preflight** (solo lectura):
   ```powershell
   cd <repo>\scripts\sql
   sqlcmd -S $S -E -C -I -W -h -1 -s "|" -f 65001 -d TheBuryProjectDb -i vendedor2-huella-global.sql -v BaseEsperada=TheBuryProjectDb | Where-Object { $_ -and $_ -notmatch '^\(' } | Sort-Object | Set-Content "$D\huella-antes.txt"
   sqlcmd -S $S -E -C -I -b -W -f 65001 -d TheBuryProjectDb -i vendedor2-preflight.sql -v BaseEsperada=TheBuryProjectDb
   ```
   Debe terminar en `VEREDICTO: GO` (código 0). Leer los AVISO (en particular G10: descripción y datos propios; G11: eventos de auditoria; G8: si el rol apareciera **activo**, confirmar con el propietario antes de seguir). Cualquier `NO-GO` (usuarios asignados, referencias, FK nuevas) **detiene** el procedimiento.
3. **Recomendado:** ventana sin tráfico o con la aplicación detenida (la eliminación dura milisegundos, pero la verificación de «nada más cambió» incluye `AspNetUsers`, que un inicio de sesión simultáneo reescribe; un falso aborto solo exige repetir).
4. **Ensayo sobre la real (DryRun: revierte todo, incluida la tabla de respaldo; el archivo `.sql` sí se escribe para revisarlo):**
   ```powershell
   $env:RutaReinsercion = "$D\vendedor2-reinsertar.sql"
   sqlcmd -S $S -E -C -I -b -W -f 65001 -d TheBuryProjectDb -i vendedor2-eliminar.sql -v BaseEsperada=TheBuryProjectDb Modo=DryRun Token=NO ConfirmarBaseReal=SI
   ```
   Esperado: respaldadas 1 + 1 + 35 + 35 filas, «DryRun: se REVIRTIO todo».
5. **Aplicar:**
   ```powershell
   sqlcmd -S $S -E -C -I -b -W -f 65001 -d TheBuryProjectDb -i vendedor2-eliminar.sql -v BaseEsperada=TheBuryProjectDb Modo=Aplicar "Token=ELIMINAR-VENDEDOR2-$H" ConfirmarBaseReal=SI
   ```
   Esperado: «APLICADO», lote de respaldo impreso (anotarlo), archivo `vendedor2-reinsertar.sql` creado.
6. **Verificar:**
   - `vendedor2-preflight.sql` ahora da `NO-GO` en G2 (el rol ya no existe): es lo esperado.
   - Huella global: repetir el paso 2 hacia `huella-despues.txt` y `Compare-Object`. Deben diferir **solo** `AspNetRoles` (−1), `RolesMetadata` (−1), `RolPermisos` (−35), `AspNetRoleClaims` (−35) y `SeguridadEventosAuditoria` (+1). Cualquier otra tabla distinta (salvo `AspNetUsers` por inicios de sesión) es una alerta.
   - En la app: `/Seguridad` (pestañas Roles y Permisos por rol) ya no lista `vendedor2`; los demás roles y sus permisos intactos; un usuario Vendedor entra y opera; la pestaña Auditoría conserva los 5 eventos históricos y suma «Eliminar rol». No hace falta reiniciar.
   - Opcional: `DBCC CHECKDB (TheBuryProjectDb) WITH NO_INFOMSGS`.
7. **Rollback (si hiciera falta):**
   ```powershell
   sqlcmd -S $S -E -C -I -b -W -f 65001 -d TheBuryProjectDb -i vendedor2-rollback.sql -v BaseEsperada=TheBuryProjectDb Modo=DryRun Token=NO ConfirmarBaseReal=SI
   sqlcmd -S $S -E -C -I -b -W -f 65001 -d TheBuryProjectDb -i vendedor2-rollback.sql -v BaseEsperada=TheBuryProjectDb Modo=Aplicar "Token=RESTAURAR-VENDEDOR2-$H" ConfirmarBaseReal=SI
   ```
   Reinserta con los mismos Id y `ConcurrencyStamp`, verifica **fila a fila** (cada fila reinsertada serializa idéntica al texto respaldado) y la huella de las 5 tablas del rol, registra «Restaurar rol» y conserva el respaldo marcado como restaurado. Si entre la eliminación y el rollback se editaron otros roles o permisos, la huella ya no puede coincidir y el script **aborta sin forzar nada**: en ese caso usar el archivo autónomo (`sqlcmd -S $S -E -C -I -b -f 65001 -d TheBuryProjectDb -i $D\vendedor2-reinsertar.sql`), que reinserta las filas sin comparar el resto y se niega si el rol ya existe.
   Último recurso: `RESTORE` del `.bak` del paso 1 (en una base nueva, no sobre la real, y comparar).
8. **Limpieza (solo cuando el propietario confirme que no hace falta revertir):** la tabla `dbo.__Respaldo_Rol_vendedor2` queda en la base como evidencia (no la usa la aplicación ni EF). Conservarla junto con el `.bak` y el `.sql` de reinsercion el tiempo que se decida; para retirarla: `DROP TABLE dbo.__Respaldo_Rol_vendedor2;` (acción separada, no incluida en los scripts).

## 5. Ensayo en el clon (resultados)

Clon `TheBuryProjectDb_v2` restaurado desde un `COPY_ONLY` de la real (`RESTORE VERIFYONLY` válido; huella inicial idéntica a la real tabla por tabla). `sqlcmd 15.0.1300.359`, SQL Server 2025 LocalDB.

| Prueba | Resultado |
|---|---|
| Preflight sobre real y sobre clon | GO en ambos, mismas 13 guardas y misma huella |
| Falta una variable (`Token`, `RutaReinsercion`) | sqlcmd aborta: `'X' scripting variable not defined`, nada se ejecuta |
| `BaseEsperada` incorrecta / `Modo` inválido / `Aplicar` sin token / token de otra fecha / ruta relativa | Errores 52001 / 52002 / 52004 / 52004 / 52005, base intacta |
| Guarda de base real (copia del script que trata al clon como «real», nunca la real) con `ConfirmarBaseReal=NO` y token correcto | Error 52003, base intacta |
| Usuario asignado sembrado en el clon | Preflight `NO-GO` (G4) y eliminar aborta (52011) |
| Referencia sembrada (`UmbralesAutorizacion.Rol = 'vendedor2'`) | Preflight `NO-GO` (G6/G7) y eliminar aborta (52015) |
| DryRun eliminar | Respaldó 1+1+35+35 filas, escribió el `.sql`, revirtió: roles 10, sin tabla de respaldo, auditoría 55 |
| Aplicar eliminar | Roles 10 → 9. `vendedor2-huella-global` (133 tablas) antes/después: difieren **solo** `AspNetRoleClaims` 99→64, `AspNetRoles` 10→9, `RolPermisos` 625→590, `RolesMetadata` 10→9 y auditoría 55→56. Las otras 128 tablas idénticas |
| Segunda ejecución de eliminar | Aborta (52010, el rol ya no existe) |
| DryRun y Aplicar del rollback | Huella de las 5 tablas del rol idéntica a la previa (filas y sumas); `RolPermisos` 625, claims 99, `ventas.authorize` otra vez en Id 3042, descripción y `ConcurrencyStamp f8d4a412-…` idénticos. Único cambio de `BINARY_CHECKSUM(*)`: la columna `RowVersion`, que SQL Server regenera (sumas por columnas funcionales idénticas) |
| Segundo rollback | Aborta (52206, no hay lote sin restaurar) |
| Archivo de reinsercion autónomo, tras eliminar | Restaura la misma huella funcional; con el rol existente se niega (53002) |
| App contra el clon (Development, luego Staging con `InitializeOnStartup=true`), usuarios sintéticos SuperAdmin/Vendedor/Gerente | **Antes:** Roles y Permisos por rol listan `vendedor2` (4 y 2 menciones), detalle y edición 200. **Después de eliminar (sesiones abiertas):** 0 menciones, detalle/edición del Id borrado → 404 limpio, los demás roles (incl. `Vendedor`, `vendedor pobre`) iguales, Vendedor `/Venta` 200 y `/Cliente` 200, Gerente `/Venta` 200, sin 500 ni excepciones en el log. Tras reinicio con migraciones y seeds: el rol **sigue ausente** |
| Tras el rollback (app viva) | `vendedor2` vuelve a listarse (4 y 2 menciones) con detalle y edición 200 |
| Eliminación por la propia app (`POST /Seguridad/DeleteRol`) y luego reinserción con el archivo autónomo | La app dejó el mismo estado final que el script, **sin evento de auditoría de éxito**; el archivo lo restauró |

Límites honestos: la comprobación de pantallas fue por HTTP (HTML y códigos de estado), no con capturas de navegador. No se midió concurrencia con tráfico real. El ensayo usó el esquema del clon al nivel de la real (136 migraciones) y, para la parte de seeds, al nivel de `main` (137); los scripts solo tocan tablas de Identity/`RolPermisos`/`RolesMetadata`/auditoría, que no cambian entre ambos. El respaldo/DR en Windows de la base real sigue la línea de `docs/` de backups (P-06); esta tarea no lo modifica.

## 6. Qué comunicar

- A quien administre la seguridad: «Se elimina el rol de prueba `vendedor2` (inactivo, sin usuarios, creado el 2026-09-29). Los 5 eventos de auditoría que lo mencionan se conservan. El rol `Vendedor` y `vendedor pobre` no cambian. Hay respaldo completo y reversión probada.»
- A JAVIER: ningún efecto; ya no tiene el rol desde 2026-09-30 y hoy opera con `Cajero + Vendedor`.
- Registrar en el acta de la ventana: fecha, lote de respaldo, ruta del `.bak` y del `.sql` de reinsercion.

## 7. Autorización que se pide al propietario

1. Confirmar que **`vendedor2` no tiene uso previsto** (es de prueba; difiere de `Vendedor`).
2. Autorizar expresamente, para una ventana acordada, la ejecución en `TheBuryProjectDb` de: respaldo `COPY_ONLY` + `VERIFYONLY`, `vendedor2-eliminar.sql` en `DryRun` y luego en `Aplicar` (con `ConfirmarBaseReal=SI` y el token del día), y la creación de la tabla `dbo.__Respaldo_Rol_vendedor2` en la base real.
3. Decidir por separado, fuera de esta tarea: el rol hermano `vendedor pobre` (2 permisos, creado 2026-10-02, sin evidencia de uso aquí) y la limpieza posterior de la tabla de respaldo.

## 8. Hallazgo aparte (no es parte de este procedimiento)

El rol `Vendedor` tiene 5 `RolPermisos` vigentes sin claim espejo en `AspNetRoleClaims` (`documentos.view`, `documentos.generate`, `documentos.reprint`, `documentos.sign`, `creditos.configure`; 41 permisos vigentes frente a 36 claims). No tiene efecto funcional (los permisos efectivos se leen de `RolPermisos`), pero indica que algún cambio de permisos no pasó por `SyncRoleClaimsAsync`. No se modificó.

/*
  Hito 11 - D-8: asignacion de las 10 asignaciones de permisos faltantes de Precios y Mora a Gerente, Cajero y Contador.

  Por que existe: la base real no ejecuta los sembradores al arrancar (Database:InitializeOnStartup=false), de modo que estas
  asignaciones iniciales (Data/Seeds/RolesPermisosSeeder.cs) nunca se crearon. Reproduce EXACTAMENTE la politica del sembrador:

    Gerente  -> precios.view, precios.simulate, precios.approve, precios.apply, precios.revert, precios.history,
                mora.view, mora.manage            (NO precios.create/update/delete ni mora.config: administrativos)
    Cajero   -> mora.view
    Contador -> mora.view

  Solo inserta filas en RolPermisos. NO crea acciones (deben existir y estar activas: si falta alguna, aborta), no toca otros
  permisos, roles, usuarios ni datos, no ejecuta el inicializador ni crea usuarios de prueba.
  Es idempotente (volver a correrlo no inserta nada), transaccional y serializado ante ejecuciones simultaneas.

  Uso (sqlcmd; -v es obligatorio para evitar correrlo sin querer):
    sqlcmd -S <servidor> -d <base> -I -b -i h11-d8-permisos-precios-mora.sql -v DryRun=1   (ensayo: revierte)
    sqlcmd -S <servidor> -d <base> -I -b -i h11-d8-permisos-precios-mora.sql -v DryRun=0   (aplica)

  Hacer un respaldo verificado antes de aplicar (BACKUP ... WITH CHECKSUM; RESTORE VERIFYONLY).
  Despues de aplicar: los usuarios afectados deben volver a iniciar sesion para que sus claims incluyan los permisos nuevos.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @Autor nvarchar(256) = N'H11-D8';
DECLARE @Ahora datetime2 = SYSUTCDATETIME();

BEGIN TRANSACTION;

-- 0) Serializa ejecuciones simultaneas del script (bloqueo de aplicacion ligado a la transaccion).
DECLARE @Lock int;
EXEC @Lock = sp_getapplock @Resource = N'h11-d8-permisos', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @Lock < 0
    THROW 51013, 'No se pudo obtener el bloqueo de aplicacion: hay otra ejecucion en curso.', 1;

-- 1) Modulos que deben existir (si falta alguno, aborta sin cambiar nada).
DECLARE @ModPrecios int = (SELECT Id FROM dbo.ModulosSistema WHERE Clave = N'precios' AND IsDeleted = 0);
DECLARE @ModMora int = (SELECT Id FROM dbo.ModulosSistema WHERE Clave = N'mora' AND IsDeleted = 0);
IF @ModPrecios IS NULL OR @ModMora IS NULL
    THROW 51010, 'Falta el modulo precios o mora en ModulosSistema: no se modifica nada.', 1;

-- 1b) Los roles se resuelven UNA sola vez, bajo bloqueo, y de ahi en mas se usan solo esos Id: debe existir exactamente un
--     rol por cada nombre requerido (ni ausentes ni homonimos), o se aborta sin cambiar nada.
DECLARE @Roles TABLE (Rol nvarchar(256) PRIMARY KEY, RoleId nvarchar(450) NOT NULL);
INSERT @Roles (Rol, RoleId)
SELECT r.Name, MIN(r.Id) FROM dbo.AspNetRoles r WITH (UPDLOCK, HOLDLOCK) WHERE r.Name IN (N'Gerente', N'Cajero', N'Contador') GROUP BY r.Name HAVING COUNT(*) = 1;
IF (SELECT COUNT(*) FROM @Roles) <> 3
    THROW 51014, 'Falta alguno de los roles requeridos o hay roles homonimos: no se modifica nada.', 1;

-- 2) Asignaciones iniciales (politica del sembrador).
DECLARE @Asignaciones TABLE (Rol nvarchar(256), ModuloId int, Clave nvarchar(100));
INSERT @Asignaciones VALUES
    (N'Gerente',  @ModPrecios, N'view'),
    (N'Gerente',  @ModPrecios, N'simulate'),
    (N'Gerente',  @ModPrecios, N'approve'),
    (N'Gerente',  @ModPrecios, N'apply'),
    (N'Gerente',  @ModPrecios, N'revert'),
    (N'Gerente',  @ModPrecios, N'history'),
    (N'Gerente',  @ModMora,    N'view'),
    (N'Gerente',  @ModMora,    N'manage'),
    (N'Cajero',   @ModMora,    N'view'),
    (N'Contador', @ModMora,    N'view');

-- 2b) Todas las acciones deben existir, activas y no eliminadas (este script NO las crea).
IF EXISTS (
    SELECT 1 FROM @Asignaciones s
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AccionesModulo ac WHERE ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave AND ac.IsDeleted = 0 AND ac.Activa = 1))
    THROW 51011, 'Falta alguna de las acciones (o esta inactiva/dada de baja): no se modifica nada.', 1;

-- 2c) Una asignacion existente pero dada de baja indica una remocion deliberada: no se revive aqui (decision manual).
IF EXISTS (
    SELECT 1 FROM @Asignaciones s
    JOIN @Roles r ON r.Rol = s.Rol
    JOIN dbo.AccionesModulo ac ON ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave
    JOIN dbo.RolPermisos x ON x.RoleId = r.RoleId AND x.ModuloId = s.ModuloId AND x.AccionId = ac.Id AND x.IsDeleted = 1)
    THROW 51012, 'Una asignacion existe pero esta dada de baja: no se modifica nada.', 1;

INSERT dbo.RolPermisos (RoleId, ModuloId, AccionId, ClaimValue, CreatedAt, CreatedBy, IsDeleted)
SELECT r.RoleId, s.ModuloId, ac.Id,
       CASE WHEN s.ModuloId = @ModPrecios THEN N'precios.' ELSE N'mora.' END + s.Clave,
       @Ahora, @Autor, 0
FROM @Asignaciones s
JOIN @Roles r ON r.Rol = s.Rol
JOIN dbo.AccionesModulo ac ON ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave AND ac.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolPermisos x WITH (UPDLOCK, HOLDLOCK) WHERE x.RoleId = r.RoleId AND x.ModuloId = s.ModuloId AND x.AccionId = ac.Id);
DECLARE @AsignacionesInsertadas int = @@ROWCOUNT;

-- 3) Verificacion previa al commit: lo que debe quedar.
DECLARE @Esperadas int = (SELECT COUNT(*) FROM @Asignaciones);   -- siempre las 10 de la politica, nunca "las que se pudieron resolver"
DECLARE @Presentes int = (
    SELECT COUNT(*) FROM @Asignaciones s
    JOIN @Roles r ON r.Rol = s.Rol
    JOIN dbo.AccionesModulo ac ON ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave
    JOIN dbo.RolPermisos rp ON rp.RoleId = r.RoleId AND rp.ModuloId = s.ModuloId AND rp.AccionId = ac.Id AND rp.IsDeleted = 0);
IF @Presentes <> @Esperadas
    THROW 51015, 'La verificacion de asignaciones no coincide: se revierte todo.', 1;

SELECT @AsignacionesInsertadas AS AsignacionesInsertadas, @Esperadas AS AsignacionesEsperadas, @DryRun AS DryRun;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'DryRun=1: se revirtio todo (ensayo).';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT 'DryRun=0: cambios confirmados.';
END

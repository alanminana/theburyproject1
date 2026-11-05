/*
  Hito 11 - D-1: alta acotada de los permisos ventas.discount y creditos.configure y sus asignaciones iniciales.

  Por que existe: la base real no ejecuta los sembradores al arrancar (Database:InitializeOnStartup=false), de modo que las
  dos acciones agregadas despues de 2026-10-04 nunca se crearon y solo SuperAdmin (bypass) puede usarlas.
  Reproduce EXACTAMENTE lo que hace Data/Seeds/RolesPermisosSeeder.cs para estas dos acciones, y nada mas:

    ventas.discount     -> Administrador, SuperAdmin   (Gerente y los demas NO: decision vigente, se asigna a mano)
    creditos.configure  -> Administrador, SuperAdmin, Gerente, Vendedor

  No toca otros permisos, roles, usuarios ni datos. No crea usuarios de prueba. No ejecuta el inicializador.
  Es idempotente (volver a correrlo no inserta nada) y transaccional (todo o nada).

  Uso (sqlcmd, modo script; -v es obligatorio para evitar correrlo sin querer):
    sqlcmd -S <servidor> -d <base> -I -b -i h11-d1-permisos-ventas-discount-creditos-configure.sql -v DryRun=1   (ensayo: revierte)
    sqlcmd -S <servidor> -d <base> -I -b -i h11-d1-permisos-ventas-discount-creditos-configure.sql -v DryRun=0   (aplica)

  Hacer un respaldo verificado antes de aplicar (BACKUP ... WITH CHECKSUM; RESTORE VERIFYONLY).
  Despues de aplicar: los usuarios deben volver a iniciar sesion para que sus claims incluyan los permisos nuevos.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @Autor nvarchar(256) = N'H11-D1';
DECLARE @Ahora datetime2 = SYSUTCDATETIME();

BEGIN TRANSACTION;

-- 0) Serializa ejecuciones simultaneas del script (bloqueo de aplicacion ligado a la transaccion).
DECLARE @Lock int;
EXEC @Lock = sp_getapplock @Resource = N'h11-d1-permisos', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @Lock < 0
    THROW 51003, 'No se pudo obtener el bloqueo de aplicacion: hay otra ejecucion en curso.', 1;

-- 1) Modulos que deben existir (si falta alguno, aborta sin cambiar nada).
DECLARE @ModVentas int = (SELECT Id FROM dbo.ModulosSistema WHERE Clave = N'ventas' AND IsDeleted = 0);
DECLARE @ModCreditos int = (SELECT Id FROM dbo.ModulosSistema WHERE Clave = N'creditos' AND IsDeleted = 0);
IF @ModVentas IS NULL OR @ModCreditos IS NULL
    THROW 51000, 'Falta el modulo ventas o creditos en ModulosSistema: no se modifica nada.', 1;

-- 1b) Los roles se resuelven por nombre: si un nombre es ambiguo (roles homonimos) se aborta sin cambiar nada.
IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name IN (N'Administrador', N'SuperAdmin', N'Gerente', N'Vendedor') GROUP BY Name HAVING COUNT(*) > 1)
    THROW 51004, 'Hay roles homonimos entre Administrador/SuperAdmin/Gerente/Vendedor: no se modifica nada.', 1;

-- 2) Acciones (misma definicion que el sembrador: nombre, clave y orden).
DECLARE @Acciones TABLE (ModuloId int, Nombre nvarchar(200), Clave nvarchar(100), Orden int);
INSERT @Acciones VALUES (@ModVentas, N'Aplicar descuentos', N'discount', 11), (@ModCreditos, N'Configurar', N'configure', 8);

INSERT dbo.AccionesModulo (ModuloId, Nombre, Clave, Orden, Activa, CreatedAt, CreatedBy, IsDeleted)
SELECT a.ModuloId, a.Nombre, a.Clave, a.Orden, 1, @Ahora, @Autor, 0
FROM @Acciones a
WHERE NOT EXISTS (SELECT 1 FROM dbo.AccionesModulo x WITH (UPDLOCK, HOLDLOCK) WHERE x.ModuloId = a.ModuloId AND x.Clave = a.Clave);
DECLARE @AccionesInsertadas int = @@ROWCOUNT;

-- Una accion existente pero dada de baja no se revive aqui: se avisa y se aborta (decision manual).
IF EXISTS (SELECT 1 FROM dbo.AccionesModulo x JOIN @Acciones a ON a.ModuloId = x.ModuloId AND a.Clave = x.Clave WHERE x.IsDeleted = 1 OR x.Activa = 0)
    THROW 51001, 'Una de las acciones existe pero esta inactiva o dada de baja: no se modifica nada.', 1;

-- 3) Asignaciones iniciales a roles (por nombre de rol). Un rol inexistente simplemente no recibe la fila.
DECLARE @Asignaciones TABLE (Rol nvarchar(256), ModuloId int, Clave nvarchar(100));
INSERT @Asignaciones VALUES
    (N'Administrador', @ModVentas,   N'discount'),
    (N'SuperAdmin',    @ModVentas,   N'discount'),
    (N'Administrador', @ModCreditos, N'configure'),
    (N'SuperAdmin',    @ModCreditos, N'configure'),
    (N'Gerente',       @ModCreditos, N'configure'),
    (N'Vendedor',      @ModCreditos, N'configure');

INSERT dbo.RolPermisos (RoleId, ModuloId, AccionId, ClaimValue, CreatedAt, CreatedBy, IsDeleted)
SELECT r.Id, s.ModuloId, ac.Id,
       CASE WHEN s.ModuloId = @ModVentas THEN N'ventas.' ELSE N'creditos.' END + s.Clave,
       @Ahora, @Autor, 0
FROM @Asignaciones s
JOIN dbo.AspNetRoles r ON r.Name = s.Rol
JOIN dbo.AccionesModulo ac ON ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave AND ac.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolPermisos x WITH (UPDLOCK, HOLDLOCK) WHERE x.RoleId = r.Id AND x.ModuloId = s.ModuloId AND x.AccionId = ac.Id);
DECLARE @AsignacionesInsertadas int = @@ROWCOUNT;

-- 4) Verificacion previa al commit: lo que debe quedar, y nada fuera de lo esperado.
DECLARE @Esperadas int = (SELECT COUNT(*) FROM @Asignaciones s JOIN dbo.AspNetRoles r ON r.Name = s.Rol);
DECLARE @Presentes int = (
    SELECT COUNT(*) FROM @Asignaciones s
    JOIN dbo.AspNetRoles r ON r.Name = s.Rol
    JOIN dbo.AccionesModulo ac ON ac.ModuloId = s.ModuloId AND ac.Clave = s.Clave
    JOIN dbo.RolPermisos rp ON rp.RoleId = r.Id AND rp.ModuloId = s.ModuloId AND rp.AccionId = ac.Id AND rp.IsDeleted = 0);
IF @Presentes <> @Esperadas
    THROW 51002, 'La verificacion de asignaciones no coincide: se revierte todo.', 1;

SELECT @AccionesInsertadas AS AccionesInsertadas, @AsignacionesInsertadas AS AsignacionesInsertadas,
       @Esperadas AS AsignacionesEsperadas, @DryRun AS DryRun;

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

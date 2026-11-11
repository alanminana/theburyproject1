/*
  vendedor2-rollback.sql  --  PREPARADO, NO APLICADO.  Reinserta EXACTAMENTE las filas que borro vendedor2-eliminar.sql.

  QUE HACE (una transaccion, XACT_ABORT; cualquier guarda o verificacion que falle revierte TODO):
    1. Valida parametros y guardas: existe un lote de respaldo sin restaurar; el rol (Id y nombre) NO existe hoy.
    2. Reinserta desde dbo.__Respaldo_Rol_vendedor2, con los MISMOS Id (IDENTITY_INSERT) y ConcurrencyStamp:
       AspNetRoles, RolesMetadata, RolPermisos (incl. bajas logicas), AspNetRoleClaims.
    3. Verifica fila a fila (cada fila reinsertada serializa IDENTICA al texto respaldado) y la huella de las 5 tablas del rol
       (AspNetRoles, AspNetUserRoles, RolPermisos, AspNetRoleClaims, RolesMetadata): conteos y sumas IDENTICOS a los
       registrados ANTES de la eliminacion. (RowVersion no forma parte: SQL Server lo regenera al reinsertar.)
    4. Marca el lote como restaurado (RestauradoEn) y registra el evento de auditoria "Restaurar rol". El respaldo se CONSERVA.
    5. DryRun: ROLLBACK de todo. Aplicar: COMMIT.
  Si el respaldo en la base se perdio, usar el archivo de reinsercion .sql que genero la eliminacion (es autonomo).
  Si cambiaron otras filas en la base entre la eliminacion y el rollback (p. ej. se creo un usuario o un rol), la huella ya no
  puede ser identica: el script lo informa y ABORTA (no fuerza nada); en ese caso usar el archivo de reinsercion autonomo.

  PARAMETROS (sqlcmd -v; todos obligatorios, si falta uno sqlcmd aborta sin ejecutar nada):
    BaseEsperada      base a la que debe estar conectado; si DB_NAME() es otro, aborta.
    Modo              DryRun | Aplicar.
    Token             solo en Aplicar: RESTAURAR-VENDEDOR2-AAAAMMDD con la fecha de HOY (hora del servidor). En DryRun: cualquier texto.
    ConfirmarBaseReal SI | NO.  Sobre la base real "TheBuryProjectDb" (DryRun o Aplicar) se exige SI ademas del token.

  USO:
    sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -I -b -W -f 65001 -d <base> -i vendedor2-rollback.sql -v BaseEsperada=<base> Modo=DryRun Token=NO ConfirmarBaseReal=NO
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Base sysname = N'$(BaseEsperada)';
DECLARE @Modo nvarchar(20) = N'$(Modo)';
DECLARE @Token nvarchar(100) = N'$(Token)';
DECLARE @ConfReal nvarchar(10) = N'$(ConfirmarBaseReal)';

IF DB_NAME() <> @Base
    THROW 52201, 'Base de destino incorrecta (BaseEsperada no coincide con DB_NAME()). No se modifica nada.', 1;
IF @Modo NOT IN (N'DryRun', N'Aplicar')
    THROW 52202, 'Modo invalido: usar DryRun o Aplicar. No se modifica nada.', 1;
IF LOWER(DB_NAME()) = N'theburyprojectdb'
BEGIN
    IF @ConfReal <> N'SI'
        THROW 52203, 'Base real TheBuryProjectDb: se requiere ConfirmarBaseReal=SI ademas del token. No se modifica nada.', 1;
END;
IF @Modo = N'Aplicar' AND @Token <> N'RESTAURAR-VENDEDOR2-' + FORMAT(SYSDATETIME(), 'yyyyMMdd')
    THROW 52204, 'Token invalido para Aplicar (debe ser RESTAURAR-VENDEDOR2-AAAAMMDD con la fecha de hoy). No se modifica nada.', 1;
IF OBJECT_ID(N'dbo.__Respaldo_Rol_vendedor2', N'U') IS NULL
    THROW 52205, 'No existe dbo.__Respaldo_Rol_vendedor2: no hay respaldo en la base. Usar el archivo de reinsercion. No se modifica nada.', 1;

-- Lote a restaurar: el mas reciente sin restaurar.
DECLARE @Lote uniqueidentifier = (SELECT TOP 1 Lote FROM dbo.__Respaldo_Rol_vendedor2 WHERE Tabla = N'AspNetRoles' AND RestauradoEn IS NULL ORDER BY FechaRespaldo DESC, Id DESC);
IF @Lote IS NULL
    THROW 52206, 'No hay ningun lote de respaldo sin restaurar. No se modifica nada.', 1;
IF (SELECT COUNT(DISTINCT BaseOrigen) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote) <> 1
   OR (SELECT TOP 1 BaseOrigen FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote) <> DB_NAME()
    THROW 52207, 'El lote de respaldo proviene de otra base. No se modifica nada.', 1;

DECLARE @RoleId nvarchar(450) = (SELECT PkOrigen FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoles');
IF @RoleId IS NULL
    THROW 52208, 'El lote no contiene la fila de AspNetRoles. No se modifica nada.', 1;
IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @RoleId OR NormalizedName = N'VENDEDOR2' OR Name = N'vendedor2')
    THROW 52209, 'El rol vendedor2 (o su Id) ya existe: no hay nada que restaurar. No se modifica nada.', 1;
IF (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'__HUELLA') <> 1
    THROW 52210, 'El lote no tiene la huella previa registrada. No se modifica nada.', 1;

CREATE TABLE #Ctx (Lote uniqueidentifier NOT NULL, RoleId nvarchar(450) NOT NULL, Modo nvarchar(20) NOT NULL);
INSERT #Ctx VALUES (@Lote, @RoleId, @Modo);
BEGIN TRANSACTION;
GO

DECLARE @Lote uniqueidentifier, @RoleId nvarchar(450), @Modo nvarchar(20);
SELECT @Lote = Lote, @RoleId = RoleId, @Modo = Modo FROM #Ctx;
DECLARE @nRoles int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoles');
DECLARE @nMeta int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolesMetadata');
DECLARE @nPerm int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolPermisos');
DECLARE @nClaim int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoleClaims');
DECLARE @d int;

INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT j.Id, j.Name, j.NormalizedName, j.ConcurrencyStamp
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id nvarchar(450), Name nvarchar(256), NormalizedName nvarchar(256), ConcurrencyStamp nvarchar(max)) j
WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoles';
SET @d = @@ROWCOUNT; IF @d <> @nRoles THROW 52220, 'AspNetRoles: filas reinsertadas distintas de las respaldadas. Se revierte.', 1;

SET IDENTITY_INSERT dbo.RolesMetadata ON;
INSERT dbo.RolesMetadata (Id, RoleId, Descripcion, Activo, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted)
SELECT j.Id, j.RoleId, j.Descripcion, j.Activo, j.CreatedAt, j.UpdatedAt, j.CreatedBy, j.UpdatedBy, j.IsDeleted
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), Descripcion nvarchar(500), Activo bit, CreatedAt datetime2(7),
     UpdatedAt datetime2(7), CreatedBy nvarchar(max), UpdatedBy nvarchar(max), IsDeleted bit) j
WHERE b.Lote = @Lote AND b.Tabla = N'RolesMetadata';
SET @d = @@ROWCOUNT; SET IDENTITY_INSERT dbo.RolesMetadata OFF;
IF @d <> @nMeta THROW 52221, 'RolesMetadata: filas reinsertadas distintas de las respaldadas. Se revierte.', 1;

SET IDENTITY_INSERT dbo.RolPermisos ON;
INSERT dbo.RolPermisos (Id, RoleId, ModuloId, AccionId, ClaimValue, Observaciones, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted)
SELECT j.Id, j.RoleId, j.ModuloId, j.AccionId, j.ClaimValue, j.Observaciones, j.CreatedAt, j.UpdatedAt, j.CreatedBy, j.UpdatedBy, j.IsDeleted
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), ModuloId int, AccionId int, ClaimValue nvarchar(200),
     Observaciones nvarchar(500), CreatedAt datetime2(7), UpdatedAt datetime2(7), CreatedBy nvarchar(max), UpdatedBy nvarchar(max), IsDeleted bit) j
WHERE b.Lote = @Lote AND b.Tabla = N'RolPermisos';
SET @d = @@ROWCOUNT; SET IDENTITY_INSERT dbo.RolPermisos OFF;
IF @d <> @nPerm THROW 52222, 'RolPermisos: filas reinsertadas distintas de las respaldadas. Se revierte.', 1;

SET IDENTITY_INSERT dbo.AspNetRoleClaims ON;
INSERT dbo.AspNetRoleClaims (Id, RoleId, ClaimType, ClaimValue)
SELECT j.Id, j.RoleId, j.ClaimType, j.ClaimValue
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), ClaimType nvarchar(max), ClaimValue nvarchar(max)) j
WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoleClaims';
SET @d = @@ROWCOUNT; SET IDENTITY_INSERT dbo.AspNetRoleClaims OFF;
IF @d <> @nClaim THROW 52223, 'AspNetRoleClaims: filas reinsertadas distintas de las respaldadas. Se revierte.', 1;

-- Verificacion fila a fila: cada fila reinsertada, serializada igual que en el respaldo, es IDENTICA al texto respaldado.
IF EXISTS (SELECT 1 FROM dbo.__Respaldo_Rol_vendedor2 b WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoles' AND b.Fila <> ISNULL((SELECT x.Id AS Id, x.Name AS Name, x.NormalizedName AS NormalizedName, x.ConcurrencyStamp AS ConcurrencyStamp FROM dbo.AspNetRoles x WHERE x.Id = b.PkOrigen FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N''))
   OR EXISTS (SELECT 1 FROM dbo.__Respaldo_Rol_vendedor2 b WHERE b.Lote = @Lote AND b.Tabla = N'RolesMetadata' AND b.Fila <> ISNULL((SELECT x.Id AS Id, x.RoleId AS RoleId, x.Descripcion AS Descripcion, x.Activo AS Activo, x.CreatedAt AS CreatedAt, x.UpdatedAt AS UpdatedAt, x.CreatedBy AS CreatedBy, x.UpdatedBy AS UpdatedBy, x.IsDeleted AS IsDeleted FROM dbo.RolesMetadata x WHERE x.Id = CAST(b.PkOrigen AS int) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N''))
   OR EXISTS (SELECT 1 FROM dbo.__Respaldo_Rol_vendedor2 b WHERE b.Lote = @Lote AND b.Tabla = N'RolPermisos' AND b.Fila <> ISNULL((SELECT x.Id AS Id, x.RoleId AS RoleId, x.ModuloId AS ModuloId, x.AccionId AS AccionId, x.ClaimValue AS ClaimValue, x.Observaciones AS Observaciones, x.CreatedAt AS CreatedAt, x.UpdatedAt AS UpdatedAt, x.CreatedBy AS CreatedBy, x.UpdatedBy AS UpdatedBy, x.IsDeleted AS IsDeleted FROM dbo.RolPermisos x WHERE x.Id = CAST(b.PkOrigen AS int) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N''))
   OR EXISTS (SELECT 1 FROM dbo.__Respaldo_Rol_vendedor2 b WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoleClaims' AND b.Fila <> ISNULL((SELECT x.Id AS Id, x.RoleId AS RoleId, x.ClaimType AS ClaimType, x.ClaimValue AS ClaimValue FROM dbo.AspNetRoleClaims x WHERE x.Id = CAST(b.PkOrigen AS int) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N''))
    THROW 52226, 'Alguna fila reinsertada no es identica a la respaldada. Se revierte.', 1;

-- Verificacion de huella de las tablas del rol: igual a la registrada ANTES de eliminar (filas y suma de verificacion).
-- AspNetUsers queda fuera a proposito: cada inicio de sesion reescribe ConcurrencyStamp/UltimoAcceso y no depende del rol.
CREATE TABLE #Ahora (Tabla sysname NOT NULL, Filas int NOT NULL, Suma int NULL);
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'INSERT #Ahora SELECT N''' + t.Tabla + N''', COUNT(*), CHECKSUM_AGG(CHECKSUM(' + t.Cols + N')) FROM dbo.' + QUOTENAME(t.Tabla) + N';' + CHAR(10)
FROM (VALUES (N'AspNetRoles', N'Id,Name,NormalizedName,ConcurrencyStamp'), (N'AspNetUserRoles', N'UserId,RoleId'),
    (N'RolPermisos', N'Id,RoleId,ModuloId,AccionId,ClaimValue,Observaciones,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted'),
    (N'AspNetRoleClaims', N'Id,RoleId,ClaimType,ClaimValue'),
    (N'RolesMetadata', N'Id,RoleId,Descripcion,Activo,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted')) t(Tabla, Cols);
EXEC sp_executesql @sql;

DECLARE @malas nvarchar(400) = (
    SELECT STRING_AGG(a.Tabla, N', ')
    FROM #Ahora a
    JOIN (SELECT j.Tabla, j.Filas, j.Suma FROM dbo.__Respaldo_Rol_vendedor2 b
          CROSS APPLY OPENJSON(b.Fila) WITH (Tabla sysname, Filas int, Suma int) j
          WHERE b.Lote = @Lote AND b.Tabla = N'__HUELLA') h ON h.Tabla = a.Tabla
    WHERE a.Filas <> h.Filas OR ISNULL(a.Suma, 0) <> ISNULL(h.Suma, 0));
IF @malas IS NOT NULL
BEGIN
    DECLARE @m nvarchar(500) = CONCAT(N'La huella tras restaurar NO es identica a la previa en: ', @malas, N'. Se revierte (usar el archivo de reinsercion autonomo).');
    THROW 52224, @m, 1;
END;
IF (SELECT COUNT(*) FROM #Ahora) <> 5
    THROW 52225, 'Huella incompleta. Se revierte.', 1;

UPDATE dbo.__Respaldo_Rol_vendedor2 SET RestauradoEn = SYSUTCDATETIME() WHERE Lote = @Lote AND RestauradoEn IS NULL;

INSERT dbo.SeguridadEventosAuditoria (FechaEvento, UsuarioId, UsuarioNombre, Modulo, Accion, Entidad, Detalle, DireccionIp, CreatedAt, CreatedBy, IsDeleted)
VALUES (SYSUTCDATETIME(), NULL, LEFT(CONCAT(N'script-vendedor2-rollback (', SUSER_SNAME(), N')'), 256), N'Seguridad', N'Restaurar rol', N'Rol "vendedor2"',
        LEFT(CONCAT(N'Rol restaurado desde el respaldo (lote ', CONVERT(nvarchar(36), @Lote), N'; ', @nPerm, N' permisos, ', @nClaim, N' claims, ', @nMeta,
                    N' metadata). Huella identica a la previa. Modo=', @Modo, N'.'), 1000),
        NULL, SYSUTCDATETIME(), N'script-vendedor2-rollback', 0);

SELECT N'Filas reinsertadas' AS Concepto, N'AspNetRoles' AS Tabla, @nRoles AS Filas UNION ALL SELECT N'Filas reinsertadas', N'RolesMetadata', @nMeta
UNION ALL SELECT N'Filas reinsertadas', N'RolPermisos', @nPerm UNION ALL SELECT N'Filas reinsertadas', N'AspNetRoleClaims', @nClaim;
PRINT CONCAT(N'Lote restaurado: ', CONVERT(nvarchar(36), @Lote), N'. Huella (5 tablas del rol) identica a la previa a la eliminacion.');

IF @Modo = N'DryRun'
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'DryRun: se REVIRTIO todo. La base no cambio.';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT N'APLICADO: rol vendedor2 restaurado (respaldo conservado, marcado como restaurado).';
END;
GO

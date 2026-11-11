/*
  vendedor2-eliminar.sql  --  PREPARADO, NO APLICADO.  Elimina el rol "vendedor2" con respaldo previo y reversion exacta.

  QUE HACE (todo en UNA transaccion, XACT_ABORT; cualquier guarda que falle revierte TODO y deja la base intacta):
    1. Valida parametros y guardas (rol unico, no reservado, 0 usuarios, sin FK/referencias desconocidas).
    2. Respalda, ANTES de borrar, las filas exactas del rol en dbo.__Respaldo_Rol_vendedor2 (una fila JSON por fila origen:
       AspNetRoles, RolesMetadata, RolPermisos incl. bajas logicas, AspNetRoleClaims) y la huella previa de la base.
    3. Escribe el archivo de reinsercion (.sql autonomo) a partir de ESE respaldo (sqlcmd :OUT) y exige que exista.
    4. Borra en orden: AspNetRoleClaims, RolPermisos, RolesMetadata, AspNetRoles (el mismo conjunto que borra el cascade de la
       aplicacion al hacer DeleteRol, pero sin perder las filas). Verifica que NADA mas cambio (huella de "otros" identica).
    5. Registra el evento de auditoria "Eliminar rol" (la aplicacion no registra el exito de DeleteRol; este script si).
    6. DryRun: ROLLBACK de todo (incluida la tabla de respaldo). Aplicar: COMMIT.

  PARAMETROS (sqlcmd -v; TODOS obligatorios: si falta uno sqlcmd aborta sin ejecutar nada):
    BaseEsperada      base a la que debe estar conectado el script; si DB_NAME() es otro, aborta.
    Modo              DryRun | Aplicar.  Cualquier otro valor aborta.
    Token             solo se evalua en Aplicar: debe ser  ELIMINAR-VENDEDOR2-AAAAMMDD  con la fecha de HOY (hora del servidor).
                      En DryRun poner cualquier texto (p. ej. NO).
    ConfirmarBaseReal SI | NO.  Sobre la base real "TheBuryProjectDb" (DryRun o Aplicar) se exige SI ademas del token.
    RutaReinsercion   ruta ABSOLUTA del .sql de reinsercion que genera el script (en el equipo del cliente sqlcmd).
                      Se pasa como VARIABLE DE ENTORNO, no con -v: el sqlcmd 15 (ODBC) rechaza el ':' de "D:\..." dentro de -v.
                      sqlcmd toma del entorno toda variable $(...) no definida con -v; si falta, aborta sin ejecutar nada.
                      Se pasa como VARIABLE DE ENTORNO (no con -v): el sqlcmd 15 (ODBC) rechaza el ':' de "D:\..." dentro de -v.
                      sqlcmd toma del entorno toda variable $(...) no definida con -v; si falta, aborta sin ejecutar nada.

  USO (sqlcmd -I obligatorio por los indices filtrados):
    $env:RutaReinsercion = 'D:\ruta\vendedor2-reinsertar.sql'        (PowerShell)
    sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -I -b -W -f 65001 -d <base> -i vendedor2-eliminar.sql -v BaseEsperada=<base> Modo=DryRun Token=NO ConfirmarBaseReal=NO
    (Aplicar: Modo=Aplicar Token=ELIMINAR-VENDEDOR2-<AAAAMMDD de hoy>; sobre la real ConfirmarBaseReal=SI.)

  REVERSION: vendedor2-rollback.sql (desde el respaldo en la base) o el archivo de reinsercion generado (autonomo).

  EFECTO EN LA APLICACION: ninguna cache de roles/permisos (se leen de la base en cada solicitud: GetUserActiveRolesAsync /
  GetUserEffectivePermissionsAsync); el rol desaparece de /Seguridad en la solicitud siguiente, sin reiniciar.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ===== Lote 1: parametros, guardas, transaccion y tabla de respaldo =====
DECLARE @Base sysname = N'$(BaseEsperada)';
DECLARE @Modo nvarchar(20) = N'$(Modo)';
DECLARE @Token nvarchar(100) = N'$(Token)';
DECLARE @ConfReal nvarchar(10) = N'$(ConfirmarBaseReal)';
DECLARE @Ruta nvarchar(400) = N'$(RutaReinsercion)';
DECLARE @Rol sysname = N'vendedor2';

IF DB_NAME() <> @Base
    THROW 52001, 'Base de destino incorrecta (BaseEsperada no coincide con DB_NAME()). No se modifica nada.', 1;
IF @Modo NOT IN (N'DryRun', N'Aplicar')
    THROW 52002, 'Modo invalido: usar DryRun o Aplicar. No se modifica nada.', 1;
IF LOWER(DB_NAME()) = N'theburyprojectdb'
BEGIN
    IF @ConfReal <> N'SI'
        THROW 52003, 'Base real TheBuryProjectDb: se requiere ConfirmarBaseReal=SI ademas del token. No se modifica nada.', 1;
END;
IF @Modo = N'Aplicar' AND @Token <> N'ELIMINAR-VENDEDOR2-' + FORMAT(SYSDATETIME(), 'yyyyMMdd')
    THROW 52004, 'Token invalido para Aplicar (debe ser ELIMINAR-VENDEDOR2-AAAAMMDD con la fecha de hoy). No se modifica nada.', 1;
IF @Ruta NOT LIKE N'_:\%.sql' OR @Ruta LIKE N'% %' OR @Ruta LIKE N'%"%'
    THROW 52005, 'RutaReinsercion debe ser una ruta absoluta de unidad local terminada en .sql, sin espacios ni comillas. No se modifica nada.', 1;

DECLARE @RoleId nvarchar(450);
DECLARE @n int, @txt nvarchar(1000);

-- Guardas (las mismas que vendedor2-preflight.sql; aqui bloquean con THROW).
SET @n = (SELECT COUNT(*) FROM dbo.AspNetRoles WHERE NormalizedName = N'VENDEDOR2' OR Name = @Rol);
SET @RoleId = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE Name COLLATE Latin1_General_BIN = @Rol);
IF @n <> 1 OR @RoleId IS NULL
    THROW 52010, 'El rol vendedor2 no existe exactamente una vez (Name exacto). No se modifica nada.', 1;
IF (SELECT COUNT(*) FROM dbo.AspNetUserRoles WHERE RoleId = @RoleId) > 0
    THROW 52011, 'El rol tiene usuarios asignados. No se modifica nada.', 1;
SET @txt = (SELECT STRING_AGG(OBJECT_NAME(fk.parent_object_id), N', ') FROM sys.foreign_keys fk
            WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.AspNetRoles')
              AND OBJECT_NAME(fk.parent_object_id) NOT IN (N'AspNetUserRoles', N'AspNetRoleClaims', N'RolPermisos', N'RolesMetadata'));
IF @txt IS NOT NULL
    THROW 52012, 'Hay tablas con FK hacia AspNetRoles que este script no conoce. No se modifica nada.', 1;
IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) < 130
    THROW 52013, 'Nivel de compatibilidad < 130: sin soporte JSON para el respaldo. No se modifica nada.', 1;
IF (SELECT COUNT(*) FROM dbo.RolesMetadata WHERE RoleId = @RoleId) > 1
    THROW 52014, 'RolesMetadata tiene mas de una fila para el rol. No se modifica nada.', 1;

-- Referencias por nombre/Id en otras tablas (barrido por esquema + contenido, igual que el preflight G6/G7).
DECLARE @refs nvarchar(max) = N'', @sql nvarchar(max) = N'';
SELECT @sql += N'SELECT @r += CASE WHEN EXISTS (SELECT 1 FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' WHERE '
    + CASE WHEN ty.name IN (N'ntext', N'text') THEN N'CAST(' + QUOTENAME(c.name) + N' AS nvarchar(max))' ELSE QUOTENAME(c.name) END
    + N' LIKE @p1 OR ' + CASE WHEN ty.name IN (N'ntext', N'text') THEN N'CAST(' + QUOTENAME(c.name) + N' AS nvarchar(max))' ELSE QUOTENAME(c.name) END
    + N' LIKE @p2) THEN N''' + t.name + N'.' + c.name + N' '' ELSE N'''' END;' + CHAR(10)
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE ty.name IN (N'nvarchar', N'varchar', N'nchar', N'char', N'ntext', N'text') AND (c.max_length = -1 OR c.max_length >= 16)
  AND t.name NOT IN (N'AspNetRoles', N'RolPermisos', N'AspNetRoleClaims', N'RolesMetadata', N'SeguridadEventosAuditoria', N'__Respaldo_Rol_vendedor2');
DECLARE @p1 nvarchar(300) = N'%' + @Rol + N'%', @p2 nvarchar(500) = N'%' + @RoleId + N'%';
EXEC sp_executesql @sql, N'@r nvarchar(max) OUTPUT, @p1 nvarchar(300), @p2 nvarchar(500)', @r = @refs OUTPUT, @p1 = @p1, @p2 = @p2;
IF LEN(@refs) > 0
BEGIN
    DECLARE @m1 nvarchar(400) = CONCAT(N'Otras tablas mencionan el rol (', LEFT(@refs, 250), N'). No se modifica nada.');
    THROW 52015, @m1, 1;
END;

-- Contexto de sesion para los lotes siguientes (las variables no sobreviven al GO).
CREATE TABLE #Ctx (Lote uniqueidentifier NOT NULL, RoleId nvarchar(450) NOT NULL, Modo nvarchar(20) NOT NULL, Ruta nvarchar(400) NOT NULL,
                   Ahora datetime2(7) NOT NULL, Base sysname NOT NULL);
INSERT #Ctx VALUES (NEWID(), @RoleId, @Modo, @Ruta, SYSUTCDATETIME(), DB_NAME());

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.__Respaldo_Rol_vendedor2', N'U') IS NULL
    CREATE TABLE dbo.__Respaldo_Rol_vendedor2 (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK___Respaldo_Rol_vendedor2 PRIMARY KEY,
        Lote uniqueidentifier NOT NULL,
        FechaRespaldo datetime2(7) NOT NULL,
        BaseOrigen sysname NOT NULL,
        Tabla sysname NOT NULL,
        PkOrigen nvarchar(450) NOT NULL,
        Fila nvarchar(max) NOT NULL,
        RestauradoEn datetime2(7) NULL);
GO

-- ===== Lote 2: huella previa y respaldo de las filas exactas (ANTES de borrar nada) =====
CREATE TABLE #H (Alcance varchar(6) NOT NULL, Tabla sysname NOT NULL, Filas int NOT NULL, Suma int NULL);
DECLARE @Lote uniqueidentifier, @RoleId nvarchar(450), @Ahora datetime2(7);
SELECT @Lote = Lote, @RoleId = RoleId, @Ahora = Ahora FROM #Ctx;

-- Huella TOTAL y OTROS (todo salvo las filas del rol) por tabla.
DECLARE @def TABLE (Tabla sysname, Cols nvarchar(500), ColRol sysname NULL);
INSERT @def VALUES (N'AspNetRoles', N'Id,Name,NormalizedName,ConcurrencyStamp', N'Id'), (N'AspNetUserRoles', N'UserId,RoleId', N'RoleId'),
    (N'AspNetUsers', N'Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,ConcurrencyStamp', NULL),
    (N'RolPermisos', N'Id,RoleId,ModuloId,AccionId,ClaimValue,Observaciones,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted', N'RoleId'),
    (N'AspNetRoleClaims', N'Id,RoleId,ClaimType,ClaimValue', N'RoleId'),
    (N'RolesMetadata', N'Id,RoleId,Descripcion,Activo,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted', N'RoleId');
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'INSERT #H SELECT ''TOTAL'', N''' + Tabla + N''', COUNT(*), CHECKSUM_AGG(CHECKSUM(' + Cols + N')) FROM dbo.' + QUOTENAME(Tabla) + N';' + CHAR(10)
            + N'INSERT #H SELECT ''OTROS'', N''' + Tabla + N''', COUNT(*), CHECKSUM_AGG(CHECKSUM(' + Cols + N')) FROM dbo.' + QUOTENAME(Tabla)
            + ISNULL(N' WHERE ' + QUOTENAME(ColRol) + N' <> @RoleId', N'') + N';' + CHAR(10) FROM @def;
EXEC sp_executesql @sql, N'@RoleId nvarchar(450)', @RoleId = @RoleId;
INSERT #H SELECT 'TOTAL', N'SeguridadEventosAuditoria', COUNT(*), NULL FROM dbo.SeguridadEventosAuditoria;

INSERT dbo.__Respaldo_Rol_vendedor2 (Lote, FechaRespaldo, BaseOrigen, Tabla, PkOrigen, Fila)
SELECT @Lote, @Ahora, DB_NAME(), N'AspNetRoles', r.Id,
       (SELECT r.Id AS Id, r.Name AS Name, r.NormalizedName AS NormalizedName, r.ConcurrencyStamp AS ConcurrencyStamp FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
FROM dbo.AspNetRoles r WHERE r.Id = @RoleId;

INSERT dbo.__Respaldo_Rol_vendedor2 (Lote, FechaRespaldo, BaseOrigen, Tabla, PkOrigen, Fila)
SELECT @Lote, @Ahora, DB_NAME(), N'RolesMetadata', CAST(m.Id AS nvarchar(450)),
       (SELECT m.Id AS Id, m.RoleId AS RoleId, m.Descripcion AS Descripcion, m.Activo AS Activo, m.CreatedAt AS CreatedAt, m.UpdatedAt AS UpdatedAt,
               m.CreatedBy AS CreatedBy, m.UpdatedBy AS UpdatedBy, m.IsDeleted AS IsDeleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
FROM dbo.RolesMetadata m WHERE m.RoleId = @RoleId;

INSERT dbo.__Respaldo_Rol_vendedor2 (Lote, FechaRespaldo, BaseOrigen, Tabla, PkOrigen, Fila)
SELECT @Lote, @Ahora, DB_NAME(), N'RolPermisos', CAST(p.Id AS nvarchar(450)),
       (SELECT p.Id AS Id, p.RoleId AS RoleId, p.ModuloId AS ModuloId, p.AccionId AS AccionId, p.ClaimValue AS ClaimValue, p.Observaciones AS Observaciones,
               p.CreatedAt AS CreatedAt, p.UpdatedAt AS UpdatedAt, p.CreatedBy AS CreatedBy, p.UpdatedBy AS UpdatedBy, p.IsDeleted AS IsDeleted
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
FROM dbo.RolPermisos p WHERE p.RoleId = @RoleId;

INSERT dbo.__Respaldo_Rol_vendedor2 (Lote, FechaRespaldo, BaseOrigen, Tabla, PkOrigen, Fila)
SELECT @Lote, @Ahora, DB_NAME(), N'AspNetRoleClaims', CAST(c.Id AS nvarchar(450)),
       (SELECT c.Id AS Id, c.RoleId AS RoleId, c.ClaimType AS ClaimType, c.ClaimValue AS ClaimValue FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
FROM dbo.AspNetRoleClaims c WHERE c.RoleId = @RoleId;

-- Huella previa (TOTAL) guardada con el lote para que el rollback la compare.
INSERT dbo.__Respaldo_Rol_vendedor2 (Lote, FechaRespaldo, BaseOrigen, Tabla, PkOrigen, Fila)
SELECT @Lote, @Ahora, DB_NAME(), N'__HUELLA', N'antes',
       (SELECT h.Tabla AS Tabla, h.Filas AS Filas, h.Suma AS Suma FROM #H h WHERE h.Alcance = 'TOTAL' AND h.Tabla <> N'SeguridadEventosAuditoria' FOR JSON PATH);

IF (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoles') <> 1
    THROW 52020, 'El respaldo de AspNetRoles no tiene exactamente 1 fila. Se revierte.', 1;
IF (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolPermisos') <> (SELECT COUNT(*) FROM dbo.RolPermisos WHERE RoleId = @RoleId)
   OR (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoleClaims') <> (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId)
   OR (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolesMetadata') <> (SELECT COUNT(*) FROM dbo.RolesMetadata WHERE RoleId = @RoleId)
    THROW 52021, 'El respaldo no coincide con las filas del rol. Se revierte.', 1;

SELECT Tabla, COUNT(*) AS FilasRespaldadas FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla NOT LIKE N'\_\_%' ESCAPE N'\' GROUP BY Tabla ORDER BY Tabla;
PRINT CONCAT(N'Lote de respaldo: ', CONVERT(nvarchar(36), @Lote));
GO

-- ===== Lote 3: archivo de reinsercion autonomo, generado desde el respaldo (sqlcmd :OUT) =====
:OUT $(RutaReinsercion)
SET NOCOUNT ON;
DECLARE @Lote uniqueidentifier, @Ahora datetime2(7), @Base sysname, @RoleId nvarchar(450);
SELECT @Lote = Lote, @Ahora = Ahora, @Base = Base, @RoleId = RoleId FROM #Ctx;
DECLARE @nPerm int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolPermisos');
DECLARE @nClaim int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'AspNetRoleClaims');
DECLARE @nMeta int = (SELECT COUNT(*) FROM dbo.__Respaldo_Rol_vendedor2 WHERE Lote = @Lote AND Tabla = N'RolesMetadata');
DECLARE @l nvarchar(max);

PRINT N'/* REINSERCION AUTONOMA del rol "vendedor2" (generada por vendedor2-eliminar.sql).';
PRINT CONCAT(N'   Base: ', @Base, N' | Lote: ', CONVERT(nvarchar(36), @Lote), N' | Generado (UTC): ', CONVERT(varchar(30), @Ahora, 126));
PRINT N'   Uso: sqlcmd -S "<servidor>" -E -C -I -b -f 65001 -d <base> -i <este archivo>';
PRINT N'   Reinserta con los MISMOS Id y ConcurrencyStamp las filas que borro el script. Aborta si el rol ya existe. */';
PRINT N'SET NOCOUNT ON;';
PRINT N'SET XACT_ABORT ON;';
PRINT CONCAT(N'IF DB_NAME() <> N''', REPLACE(@Base, N'''', N''''''), N''' THROW 53001, ''Base incorrecta: este archivo se genero para otra base.'', 1;');
PRINT CONCAT(N'IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = N''', REPLACE(@RoleId, N'''', N''''''), N''' OR NormalizedName = N''VENDEDOR2'' OR Name = N''vendedor2'') THROW 53002, ''El rol ya existe. No se reinserta nada.'', 1;');
PRINT N'BEGIN TRANSACTION;';

SELECT @l = CONCAT(N'INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES (',
    ISNULL(N'N''' + REPLACE(j.Id, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.Name, N'''', N'''''') + N'''', N'NULL'), N', ',
    ISNULL(N'N''' + REPLACE(j.NormalizedName, N'''', N'''''') + N'''', N'NULL'), N', ',
    ISNULL(N'N''' + REPLACE(j.ConcurrencyStamp, N'''', N'''''') + N'''', N'NULL'), N');')
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id nvarchar(450), Name nvarchar(256), NormalizedName nvarchar(256), ConcurrencyStamp nvarchar(max)) j
WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoles';
PRINT @l;

PRINT N'SET IDENTITY_INSERT dbo.RolesMetadata ON;';
DECLARE c1 CURSOR LOCAL FAST_FORWARD FOR
SELECT CONCAT(N'INSERT dbo.RolesMetadata (Id, RoleId, Descripcion, Activo, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted) VALUES (',
    j.Id, N', ', ISNULL(N'N''' + REPLACE(j.RoleId, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.Descripcion, N'''', N'''''') + N'''', N'NULL'), N', ',
    CAST(j.Activo AS varchar(1)), N', ', ISNULL(N'CAST(''' + CONVERT(varchar(30), j.CreatedAt, 126) + N''' AS datetime2(7))', N'NULL'), N', ',
    ISNULL(N'CAST(''' + CONVERT(varchar(30), j.UpdatedAt, 126) + N''' AS datetime2(7))', N'NULL'), N', ',
    ISNULL(N'N''' + REPLACE(j.CreatedBy, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.UpdatedBy, N'''', N'''''') + N'''', N'NULL'), N', ',
    CAST(j.IsDeleted AS varchar(1)), N');')
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), Descripcion nvarchar(500), Activo bit, CreatedAt datetime2(7),
     UpdatedAt datetime2(7), CreatedBy nvarchar(max), UpdatedBy nvarchar(max), IsDeleted bit) j
WHERE b.Lote = @Lote AND b.Tabla = N'RolesMetadata' ORDER BY j.Id;
OPEN c1; FETCH NEXT FROM c1 INTO @l;
WHILE @@FETCH_STATUS = 0 BEGIN PRINT @l; FETCH NEXT FROM c1 INTO @l; END;
CLOSE c1; DEALLOCATE c1;
PRINT N'SET IDENTITY_INSERT dbo.RolesMetadata OFF;';

PRINT N'SET IDENTITY_INSERT dbo.RolPermisos ON;';
DECLARE c2 CURSOR LOCAL FAST_FORWARD FOR
SELECT CONCAT(N'INSERT dbo.RolPermisos (Id, RoleId, ModuloId, AccionId, ClaimValue, Observaciones, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted) VALUES (',
    j.Id, N', ', ISNULL(N'N''' + REPLACE(j.RoleId, N'''', N'''''') + N'''', N'NULL'), N', ', j.ModuloId, N', ', j.AccionId, N', ',
    ISNULL(N'N''' + REPLACE(j.ClaimValue, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.Observaciones, N'''', N'''''') + N'''', N'NULL'), N', ',
    ISNULL(N'CAST(''' + CONVERT(varchar(30), j.CreatedAt, 126) + N''' AS datetime2(7))', N'NULL'), N', ',
    ISNULL(N'CAST(''' + CONVERT(varchar(30), j.UpdatedAt, 126) + N''' AS datetime2(7))', N'NULL'), N', ',
    ISNULL(N'N''' + REPLACE(j.CreatedBy, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.UpdatedBy, N'''', N'''''') + N'''', N'NULL'), N', ',
    CAST(j.IsDeleted AS varchar(1)), N');')
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), ModuloId int, AccionId int, ClaimValue nvarchar(200),
     Observaciones nvarchar(500), CreatedAt datetime2(7), UpdatedAt datetime2(7), CreatedBy nvarchar(max), UpdatedBy nvarchar(max), IsDeleted bit) j
WHERE b.Lote = @Lote AND b.Tabla = N'RolPermisos' ORDER BY j.Id;
OPEN c2; FETCH NEXT FROM c2 INTO @l;
WHILE @@FETCH_STATUS = 0 BEGIN PRINT @l; FETCH NEXT FROM c2 INTO @l; END;
CLOSE c2; DEALLOCATE c2;
PRINT N'SET IDENTITY_INSERT dbo.RolPermisos OFF;';

PRINT N'SET IDENTITY_INSERT dbo.AspNetRoleClaims ON;';
DECLARE c3 CURSOR LOCAL FAST_FORWARD FOR
SELECT CONCAT(N'INSERT dbo.AspNetRoleClaims (Id, RoleId, ClaimType, ClaimValue) VALUES (', j.Id, N', ', ISNULL(N'N''' + REPLACE(j.RoleId, N'''', N'''''') + N'''', N'NULL'), N', ',
    ISNULL(N'N''' + REPLACE(j.ClaimType, N'''', N'''''') + N'''', N'NULL'), N', ', ISNULL(N'N''' + REPLACE(j.ClaimValue, N'''', N'''''') + N'''', N'NULL'), N');')
FROM dbo.__Respaldo_Rol_vendedor2 b CROSS APPLY OPENJSON(b.Fila) WITH (Id int, RoleId nvarchar(450), ClaimType nvarchar(max), ClaimValue nvarchar(max)) j
WHERE b.Lote = @Lote AND b.Tabla = N'AspNetRoleClaims' ORDER BY j.Id;
OPEN c3; FETCH NEXT FROM c3 INTO @l;
WHILE @@FETCH_STATUS = 0 BEGIN PRINT @l; FETCH NEXT FROM c3 INTO @l; END;
CLOSE c3; DEALLOCATE c3;
PRINT N'SET IDENTITY_INSERT dbo.AspNetRoleClaims OFF;';

PRINT CONCAT(N'IF (SELECT COUNT(*) FROM dbo.RolPermisos WHERE RoleId = N''', REPLACE(@RoleId, N'''', N''''''), N''') <> ', @nPerm,
    N' OR (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE RoleId = N''', REPLACE(@RoleId, N'''', N''''''), N''') <> ', @nClaim,
    N' OR (SELECT COUNT(*) FROM dbo.RolesMetadata WHERE RoleId = N''', REPLACE(@RoleId, N'''', N''''''), N''') <> ', @nMeta,
    N' THROW 53003, ''La reinsercion no dejo los conteos esperados. Se revierte.'', 1;');
PRINT N'COMMIT TRANSACTION;';
PRINT N'PRINT N''Rol vendedor2 reinsertado.'';';
GO
:OUT STDOUT

-- ===== Lote 4: el archivo debe existir; borrado; verificaciones; auditoria; DryRun/Aplicar =====
DECLARE @Lote uniqueidentifier, @RoleId nvarchar(450), @Modo nvarchar(20), @Ruta nvarchar(400), @Ahora datetime2(7);
SELECT @Lote = Lote, @RoleId = RoleId, @Modo = Modo, @Ruta = Ruta, @Ahora = Ahora FROM #Ctx;

DECLARE @f TABLE (Existe int, EsDir int, PadreExiste int);
INSERT @f EXEC master.dbo.xp_fileexist @Ruta;
IF NOT EXISTS (SELECT 1 FROM @f WHERE Existe = 1)
    THROW 52030, 'El archivo de reinsercion no se genero (ruta invalida o sin permisos). Se revierte sin borrar nada.', 1;

DECLARE @nPerm int = (SELECT COUNT(*) FROM dbo.RolPermisos WHERE RoleId = @RoleId);
DECLARE @nClaim int = (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId);
DECLARE @nMeta int = (SELECT COUNT(*) FROM dbo.RolesMetadata WHERE RoleId = @RoleId);
DECLARE @d int;

DELETE FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId; SET @d = @@ROWCOUNT;
IF @d <> @nClaim THROW 52031, 'Filas borradas de AspNetRoleClaims distintas de las respaldadas. Se revierte.', 1;
DELETE FROM dbo.RolPermisos WHERE RoleId = @RoleId; SET @d = @@ROWCOUNT;
IF @d <> @nPerm THROW 52032, 'Filas borradas de RolPermisos distintas de las respaldadas. Se revierte.', 1;
DELETE FROM dbo.RolesMetadata WHERE RoleId = @RoleId; SET @d = @@ROWCOUNT;
IF @d <> @nMeta THROW 52033, 'Filas borradas de RolesMetadata distintas de las respaldadas. Se revierte.', 1;
DELETE FROM dbo.AspNetRoles WHERE Id = @RoleId; SET @d = @@ROWCOUNT;
IF @d <> 1 THROW 52034, 'Se esperaba borrar exactamente 1 fila de AspNetRoles. Se revierte.', 1;

-- Verificacion: el rol ya no existe y NADA mas cambio (huella de "otros" previa = huella total actual, tabla por tabla).
IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @RoleId OR NormalizedName = N'VENDEDOR2')
    THROW 52035, 'El rol sigue existiendo. Se revierte.', 1;
CREATE TABLE #Despues (Tabla sysname NOT NULL, Filas int NOT NULL, Suma int NULL);
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'INSERT #Despues SELECT N''' + t.Tabla + N''', COUNT(*), CHECKSUM_AGG(CHECKSUM(' + t.Cols + N')) FROM dbo.' + QUOTENAME(t.Tabla) + N';' + CHAR(10)
FROM (VALUES (N'AspNetRoles', N'Id,Name,NormalizedName,ConcurrencyStamp'), (N'AspNetUserRoles', N'UserId,RoleId'),
    (N'AspNetUsers', N'Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,ConcurrencyStamp'),
    (N'RolPermisos', N'Id,RoleId,ModuloId,AccionId,ClaimValue,Observaciones,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted'),
    (N'AspNetRoleClaims', N'Id,RoleId,ClaimType,ClaimValue'),
    (N'RolesMetadata', N'Id,RoleId,Descripcion,Activo,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted')) t(Tabla, Cols);
EXEC sp_executesql @sql;
DECLARE @malas nvarchar(400) = (SELECT STRING_AGG(d.Tabla, N', ') FROM #Despues d JOIN #H h ON h.Alcance = 'OTROS' AND h.Tabla = d.Tabla
                                WHERE d.Filas <> h.Filas OR ISNULL(d.Suma, 0) <> ISNULL(h.Suma, 0));
IF @malas IS NOT NULL
BEGIN
    DECLARE @m2 nvarchar(500) = CONCAT(N'Cambio inesperado fuera de las filas del rol en: ', @malas, N'. Se revierte.');
    THROW 52036, @m2, 1;
END;
IF (SELECT COUNT(*) FROM dbo.SeguridadEventosAuditoria) <> (SELECT Filas FROM #H WHERE Alcance = 'TOTAL' AND Tabla = N'SeguridadEventosAuditoria')
    THROW 52037, 'La auditoria cambio durante el borrado. Se revierte.', 1;

-- Auditoria: el exito de DeleteRol no se registra en la aplicacion; este script deja el evento.
INSERT dbo.SeguridadEventosAuditoria (FechaEvento, UsuarioId, UsuarioNombre, Modulo, Accion, Entidad, Detalle, DireccionIp, CreatedAt, CreatedBy, IsDeleted)
VALUES (SYSUTCDATETIME(), NULL, LEFT(CONCAT(N'script-vendedor2-eliminar (', SUSER_SNAME(), N')'), 256), N'Seguridad', N'Eliminar rol', N'Rol "vendedor2"',
        LEFT(CONCAT(N'Rol eliminado por script con respaldo previo (lote ', CONVERT(nvarchar(36), @Lote), N'; ', @nPerm, N' permisos, ', @nClaim, N' claims, ',
                    @nMeta, N' metadata). Modo=', @Modo, N'.'), 1000),
        NULL, SYSUTCDATETIME(), N'script-vendedor2-eliminar', 0);

SELECT N'Filas borradas' AS Concepto, N'AspNetRoles' AS Tabla, 1 AS Filas UNION ALL SELECT N'Filas borradas', N'RolesMetadata', @nMeta
UNION ALL SELECT N'Filas borradas', N'RolPermisos', @nPerm UNION ALL SELECT N'Filas borradas', N'AspNetRoleClaims', @nClaim;
PRINT CONCAT(N'Archivo de reinsercion: ', @Ruta);
PRINT CONCAT(N'Lote de respaldo: ', CONVERT(nvarchar(36), @Lote));

IF @Modo = N'DryRun'
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'DryRun: se REVIRTIO todo (incluida la tabla de respaldo). La base no cambio. (El archivo de reinsercion si se escribio, para revisarlo.)';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT N'APLICADO: rol vendedor2 eliminado. Respaldo en dbo.__Respaldo_Rol_vendedor2 y en el archivo de reinsercion.';
END;
GO

/*
  vendedor2-preflight.sql  --  SOLO LECTURA.  PREPARADO, NO APLICADO.

  Verifica, con GO / NO-GO por guarda, que el rol "vendedor2" puede eliminarse con vendedor2-eliminar.sql.
  No escribe ni en tablas de usuario ni en la base (solo usa tablas temporales de la sesion). Se puede ejecutar contra
  TheBuryProjectDb: es seguro por construccion (solo SELECT).

  Uso (sqlcmd -I obligatorio; BaseEsperada obligatoria, debe coincidir con la base a la que se conecta):
    sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -I -b -W -f 65001 -d <base> -i vendedor2-preflight.sql -v BaseEsperada=<base>

  Salida: una fila por guarda (GO / NO-GO / AVISO), la huella de conteos y un veredicto final.
  Codigo de salida 0 = TODAS las guardas GO (los AVISO no bloquean); distinto de 0 = al menos un NO-GO.
  AVISO = dato informativo que el operador debe leer (p. ej. rol activo, claims desalineados), no bloquea.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Base sysname = N'$(BaseEsperada)';
IF DB_NAME() <> @Base
    THROW 52101, 'Base de destino incorrecta (BaseEsperada no coincide con DB_NAME()). No se evalua nada.', 1;

CREATE TABLE #Guardas (Orden int IDENTITY(1,1), Guarda nvarchar(70) NOT NULL, Resultado varchar(7) NOT NULL, Detalle nvarchar(1000) NULL);
CREATE TABLE #H (Alcance varchar(6) NOT NULL, Tabla sysname NOT NULL, Filas int NOT NULL, Suma int NULL);

DECLARE @Rol sysname = N'vendedor2';
DECLARE @RoleId nvarchar(450);
DECLARE @n int, @txt nvarchar(1000);

-- G1 tablas necesarias y soporte JSON (el procedimiento de respaldo/rollback usa FOR JSON / OPENJSON).
DECLARE @faltan nvarchar(1000) = (
    SELECT STRING_AGG(t.n, N', ') FROM (VALUES (N'AspNetRoles'),(N'AspNetUserRoles'),(N'AspNetUsers'),(N'RolesMetadata'),
        (N'RolPermisos'),(N'AspNetRoleClaims'),(N'SeguridadEventosAuditoria')) t(n)
    WHERE OBJECT_ID(N'dbo.' + t.n, N'U') IS NULL);
DECLARE @compat int = (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME());
INSERT #Guardas VALUES (N'G1 Tablas requeridas y nivel de compatibilidad >= 130',
    CASE WHEN @faltan IS NULL AND @compat >= 130 THEN 'GO' ELSE 'NO-GO' END,
    CONCAT(N'compat=', @compat, N'; faltan: ', ISNULL(@faltan, N'ninguna')));
IF @faltan IS NOT NULL
BEGIN
    SELECT Orden, Guarda, Resultado, Detalle FROM #Guardas ORDER BY Orden;
    THROW 52102, 'NO-GO: faltan tablas requeridas; no se evalua el resto.', 1;
END;

-- G2 el rol existe exactamente una vez (Name con mayusculas/minusculas exactas y NormalizedName).
SET @n = (SELECT COUNT(*) FROM dbo.AspNetRoles WHERE NormalizedName = N'VENDEDOR2' OR Name = @Rol);
DECLARE @nExacto int = (SELECT COUNT(*) FROM dbo.AspNetRoles WHERE Name COLLATE Latin1_General_BIN = @Rol);
SET @RoleId = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE Name COLLATE Latin1_General_BIN = @Rol);
INSERT #Guardas VALUES (N'G2 El rol existe una sola vez (Name exacto "vendedor2")',
    CASE WHEN @n = 1 AND @nExacto = 1 THEN 'GO' ELSE 'NO-GO' END,
    CONCAT(N'coincidencias (Name/NormalizedName)=', @n, N'; Name exacto=', @nExacto, N'; Id=', ISNULL(@RoleId, N'-')));

-- G3 no es un rol de sistema / reservado.
INSERT #Guardas VALUES (N'G3 No es un rol de sistema ni reservado',
    CASE WHEN @Rol NOT IN (N'SuperAdmin', N'Administrador', N'Gerente', N'Vendedor', N'Cajero', N'Repositor', N'Tecnico', N'Contador')
         THEN 'GO' ELSE 'NO-GO' END,
    N'Roles reservados: SuperAdmin, Administrador, Gerente, Vendedor, Cajero, Repositor, Tecnico, Contador');

-- G4 0 usuarios asignados (cualquier usuario: activo, inactivo o bloqueado).
SET @n = (SELECT COUNT(*) FROM dbo.AspNetUserRoles WHERE RoleId = @RoleId);
SET @txt = (SELECT STRING_AGG(u.UserName, N', ') FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetUsers u ON u.Id = ur.UserId WHERE ur.RoleId = @RoleId);
INSERT #Guardas VALUES (N'G4 Cero usuarios asignados (AspNetUserRoles, incluye inactivos)',
    CASE WHEN @RoleId IS NOT NULL AND @n = 0 THEN 'GO' ELSE 'NO-GO' END,
    CONCAT(N'asignaciones=', @n, N'; usuarios: ', ISNULL(@txt, N'ninguno')));

-- G5 las unicas tablas con FK hacia AspNetRoles son las conocidas (si aparece otra, no se sabe que borra el cascade).
SET @txt = (SELECT STRING_AGG(OBJECT_NAME(fk.parent_object_id), N', ') FROM sys.foreign_keys fk
            WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.AspNetRoles')
              AND OBJECT_NAME(fk.parent_object_id) NOT IN (N'AspNetUserRoles', N'AspNetRoleClaims', N'RolPermisos', N'RolesMetadata'));
INSERT #Guardas VALUES (N'G5 Sin FK desconocidas hacia AspNetRoles',
    CASE WHEN @txt IS NULL THEN 'GO' ELSE 'NO-GO' END,
    CONCAT(N'FK conocidas: AspNetUserRoles, AspNetRoleClaims, RolPermisos, RolesMetadata; desconocidas: ', ISNULL(@txt, N'ninguna')));

-- G6 referencias por nombre/Id del rol en tablas de configuracion (barrido por esquema: columnas Rol/RoleId/RolSolicitante...).
DECLARE @refs nvarchar(max) = N'';
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'SELECT @r += CASE WHEN EXISTS (SELECT 1 FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' WHERE ' + QUOTENAME(c.name)
             + N' IN (@Rol, @Id)) THEN N''' + t.name + N'.' + c.name + N' '' ELSE N'''' END;' + CHAR(10)
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE ty.name IN (N'nvarchar', N'varchar') AND (c.name LIKE N'%Rol%' OR c.name LIKE N'%Role%')
  AND t.name NOT IN (N'AspNetUserRoles', N'AspNetRoleClaims', N'RolPermisos', N'RolesMetadata', N'AspNetRoles')
  AND (c.max_length = -1 OR c.max_length >= 16);
EXEC sp_executesql @sql, N'@r nvarchar(max) OUTPUT, @Rol sysname, @Id nvarchar(450)', @r = @refs OUTPUT, @Rol = @Rol, @Id = @RoleId;
INSERT #Guardas VALUES (N'G6 Sin referencias al rol en columnas Rol/RoleId de otras tablas',
    CASE WHEN LEN(@refs) = 0 THEN 'GO' ELSE 'NO-GO' END, CONCAT(N'referencias: ', IIF(LEN(@refs) = 0, N'ninguna', @refs)));

-- G7 barrido por contenido: ninguna otra tabla (fuera de las propias del rol y de la auditoria) contiene el nombre o el Id del rol.
DECLARE @cont nvarchar(max) = N'';
SET @sql = N'';
SELECT @sql += N'SELECT @r += CASE WHEN EXISTS (SELECT 1 FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' WHERE '
    + CASE WHEN ty.name IN (N'ntext', N'text') THEN N'CAST(' + QUOTENAME(c.name) + N' AS nvarchar(max))' ELSE QUOTENAME(c.name) END
    + N' LIKE @p1 OR ' + CASE WHEN ty.name IN (N'ntext', N'text') THEN N'CAST(' + QUOTENAME(c.name) + N' AS nvarchar(max))' ELSE QUOTENAME(c.name) END
    + N' LIKE @p2) THEN N''' + t.name + N'.' + c.name + N' '' ELSE N'''' END;' + CHAR(10)
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE ty.name IN (N'nvarchar', N'varchar', N'nchar', N'char', N'ntext', N'text') AND (c.max_length = -1 OR c.max_length >= 16)
  AND t.name NOT IN (N'AspNetRoles', N'RolPermisos', N'AspNetRoleClaims', N'RolesMetadata', N'SeguridadEventosAuditoria', N'__Respaldo_Rol_vendedor2');
DECLARE @p1 nvarchar(300) = N'%' + @Rol + N'%', @p2 nvarchar(500) = N'%' + ISNULL(@RoleId, N'#sin-id#') + N'%';
EXEC sp_executesql @sql, N'@r nvarchar(max) OUTPUT, @p1 nvarchar(300), @p2 nvarchar(500)', @r = @cont OUTPUT, @p1 = @p1, @p2 = @p2;
INSERT #Guardas VALUES (N'G7 Barrido por contenido: ninguna otra tabla menciona el rol',
    CASE WHEN LEN(@cont) = 0 THEN 'GO' ELSE 'NO-GO' END, CONCAT(N'menciones: ', IIF(LEN(@cont) = 0, N'ninguna', @cont)));

-- G8 RolesMetadata: una sola fila; AVISO si el rol esta activo.
DECLARE @meta int = (SELECT COUNT(*) FROM dbo.RolesMetadata WHERE RoleId = @RoleId);
DECLARE @activo bit = (SELECT TOP 1 Activo FROM dbo.RolesMetadata WHERE RoleId = @RoleId);
INSERT #Guardas VALUES (N'G8 RolesMetadata: una fila; estado del rol',
    CASE WHEN @meta > 1 THEN 'NO-GO' WHEN @activo = 1 THEN 'AVISO' ELSE 'GO' END,
    CONCAT(N'filas=', @meta, N'; Activo=', ISNULL(CAST(@activo AS varchar(1)), N'-'), IIF(@activo = 1, N' (rol ACTIVO: confirmar con el propietario que no se usa)', N' (rol inactivo: no otorga permisos)')));

-- G9 claims espejo coherentes con RolPermisos vigentes (SyncRoleClaimsAsync).
DECLARE @vig int = (SELECT COUNT(*) FROM dbo.RolPermisos WHERE RoleId = @RoleId AND IsDeleted = 0);
DECLARE @cl int = (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId);
DECLARE @dif int = (SELECT COUNT(*) FROM (
    SELECT ClaimValue FROM dbo.RolPermisos WHERE RoleId = @RoleId AND IsDeleted = 0
    EXCEPT SELECT ClaimValue FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId AND ClaimType = N'Permission') d)
  + (SELECT COUNT(*) FROM (
    SELECT ClaimValue FROM dbo.AspNetRoleClaims WHERE RoleId = @RoleId AND ClaimType = N'Permission'
    EXCEPT SELECT ClaimValue FROM dbo.RolPermisos WHERE RoleId = @RoleId AND IsDeleted = 0) d2);
INSERT #Guardas VALUES (N'G9 Claims espejo = RolPermisos vigentes',
    CASE WHEN @dif = 0 THEN 'GO' ELSE 'AVISO' END, CONCAT(N'RolPermisos vigentes=', @vig, N'; AspNetRoleClaims=', @cl, N'; diferencias=', @dif));

-- G10 informativo: datos propios que se perderian (se respaldan integros en el procedimiento).
INSERT #Guardas
SELECT N'G10 Datos propios que se respaldan antes de borrar', 'AVISO',
       CONCAT(N'Descripcion="', LEFT(m.Descripcion, 120), N'"; CreatedAt=', CONVERT(varchar(19), m.CreatedAt, 120), N'; CreatedBy=', ISNULL(m.CreatedBy, N'-'),
              N'; UpdatedAt=', ISNULL(CONVERT(varchar(19), m.UpdatedAt, 120), N'-'), N'; UpdatedBy=', ISNULL(m.UpdatedBy, N'-'))
FROM dbo.RolesMetadata m WHERE m.RoleId = @RoleId;

-- G11 informativo: auditoria historica que menciona el rol (NO se toca; guarda texto, no FK).
SET @n = (SELECT COUNT(*) FROM dbo.SeguridadEventosAuditoria WHERE Entidad LIKE N'%' + @Rol + N'%' OR Detalle LIKE N'%' + @Rol + N'%');
INSERT #Guardas VALUES (N'G11 Eventos de auditoria que mencionan el rol (se conservan)', 'AVISO', CONCAT(N'eventos=', @n));

-- G12 informativo: respaldos previos del procedimiento sin restaurar.
IF OBJECT_ID(N'dbo.__Respaldo_Rol_vendedor2', N'U') IS NOT NULL
BEGIN
    SET @n = (SELECT COUNT(DISTINCT Lote) FROM dbo.__Respaldo_Rol_vendedor2 WHERE RestauradoEn IS NULL AND Tabla = N'AspNetRoles');
    INSERT #Guardas VALUES (N'G12 Respaldos previos sin restaurar', CASE WHEN @n > 0 THEN 'AVISO' ELSE 'GO' END, CONCAT(N'lotes sin restaurar=', @n));
END
ELSE INSERT #Guardas VALUES (N'G12 Respaldos previos sin restaurar', 'GO', N'la tabla de respaldo aun no existe');

-- G13 informativo: roles con nombre parecido que NO se tocan.
INSERT #Guardas VALUES (N'G13 Roles con nombre parecido (no se tocan)', 'AVISO',
    ISNULL((SELECT STRING_AGG(Name, N', ') FROM dbo.AspNetRoles WHERE Name LIKE N'%vendedor%' AND Name COLLATE Latin1_General_BIN <> @Rol), N'ninguno'));

-- Huella de conteos y sumas de verificacion (TOTAL).
DECLARE @def TABLE (Tabla sysname, Cols nvarchar(500));
INSERT @def VALUES (N'AspNetRoles', N'Id,Name,NormalizedName,ConcurrencyStamp'), (N'AspNetUserRoles', N'UserId,RoleId'),
    (N'AspNetUsers', N'Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,ConcurrencyStamp'),
    (N'RolPermisos', N'Id,RoleId,ModuloId,AccionId,ClaimValue,Observaciones,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted'),
    (N'AspNetRoleClaims', N'Id,RoleId,ClaimType,ClaimValue'),
    (N'RolesMetadata', N'Id,RoleId,Descripcion,Activo,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,IsDeleted');
SET @sql = N'';
SELECT @sql += N'INSERT #H SELECT ''TOTAL'', N''' + Tabla + N''', COUNT(*), CHECKSUM_AGG(CHECKSUM(' + Cols + N')) FROM dbo.' + QUOTENAME(Tabla) + N';' + CHAR(10) FROM @def;
EXEC sp_executesql @sql;
INSERT #H SELECT 'TOTAL', N'SeguridadEventosAuditoria', COUNT(*), NULL FROM dbo.SeguridadEventosAuditoria;

-- Veredicto.
SELECT Orden, Guarda, Resultado, Detalle FROM #Guardas ORDER BY Orden;
SELECT Tabla AS [Huella (TOTAL)], Filas, Suma AS SumaVerificacion FROM #H ORDER BY Tabla;
SELECT CONCAT(N'Base=', DB_NAME(), N'; Id del rol=', ISNULL(@RoleId, N'-')) AS Contexto;

DECLARE @nogo int = (SELECT COUNT(*) FROM #Guardas WHERE Resultado = 'NO-GO');
DECLARE @av int = (SELECT COUNT(*) FROM #Guardas WHERE Resultado = 'AVISO');
IF @nogo = 0
    PRINT CONCAT(N'VEREDICTO: GO (', @av, N' aviso(s) informativo(s) para leer). Se puede ejecutar vendedor2-eliminar.sql en modo DryRun.');
ELSE
BEGIN
    DECLARE @msg nvarchar(200) = CONCAT(N'VEREDICTO: NO-GO (', @nogo, N' guarda(s) bloqueante(s)). No eliminar.');
    THROW 52103, @msg, 1;
END;
GO

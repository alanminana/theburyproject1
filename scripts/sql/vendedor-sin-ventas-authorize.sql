/*
  Regla aprobada por el propietario: autorizar ventas (ventas.authorize) se reserva a Gerente y superiores.

  Retira UNICAMENTE el permiso ventas.authorize del rol "Vendedor" (nombre exacto):
    1. dbo.RolPermisos: baja logica (IsDeleted = 1) de la fila vigente RoleId = Vendedor, ClaimValue = 'ventas.authorize'.
    2. dbo.AspNetRoleClaims: elimina el espejo del mismo permiso del mismo rol (ClaimType = 'Permission',
       ClaimValue = 'ventas.authorize'), que la aplicacion mantiene sincronizado con RolPermisos (SyncRoleClaimsAsync).
  No toca ningun otro rol (ni "vendedor2" / "vendedor pobre"), permiso, usuario ni dato comercial.

  Es idempotente: si el permiso ya esta retirado, no cambia nada (0 filas). La aplicacion lee los permisos efectivos de
  RolPermisos en cada solicitud, asi que la revocacion rige para sesiones abiertas desde la solicitud siguiente.

  NO SE APLICA A TheBuryProjectDb SIN AUTORIZACION EXPLICITA DEL PROPIETARIO.

  Uso (sqlcmd; -v obligatorio):
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i vendedor-sin-ventas-authorize.sql -v DryRun=1 BaseEsperada=TheBuryProjectDb   (ensayo)
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i vendedor-sin-ventas-authorize.sql -v DryRun=0 BaseEsperada=TheBuryProjectDb   (aplica)
  Reversion (usar los Id impresos al aplicar):
    UPDATE dbo.RolPermisos SET IsDeleted = 0 WHERE Id = <Id de RolPermisos impreso>;
    INSERT dbo.AspNetRoleClaims (RoleId, ClaimType, ClaimValue) SELECT Id, N'Permission', N'ventas.authorize' FROM dbo.AspNetRoles WHERE Name = N'Vendedor';
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @BaseEsperada sysname = N'$(BaseEsperada)';

IF DB_NAME() <> @BaseEsperada
    THROW 51071, 'Base de destino incorrecta. No se modifica nada.', 1;

BEGIN TRANSACTION;

DECLARE @RoleId nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Vendedor');
IF @RoleId IS NULL
    THROW 51072, 'No existe el rol Vendedor. No se modifica nada.', 1;

-- Huella previa de TODO lo que no debe cambiar (otros roles/permisos y los claims de otros roles).
DECLARE @PermOtrosAntes int = (SELECT COUNT(*) FROM dbo.RolPermisos WHERE NOT (RoleId = @RoleId AND ClaimValue = N'ventas.authorize'));
DECLARE @VigOtrosAntes int = (SELECT COUNT(*) FROM dbo.RolPermisos WHERE IsDeleted = 0 AND NOT (RoleId = @RoleId AND ClaimValue = N'ventas.authorize'));
DECLARE @ClaimsOtrosAntes int = (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE NOT (RoleId = @RoleId AND ClaimType = N'Permission' AND ClaimValue = N'ventas.authorize'));

DECLARE @Afectados TABLE (Id int);
UPDATE dbo.RolPermisos SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = N'script-vendedor-sin-ventas-authorize'
OUTPUT inserted.Id INTO @Afectados
WHERE RoleId = @RoleId AND ClaimValue = N'ventas.authorize' AND IsDeleted = 0;

DECLARE @N int = (SELECT COUNT(*) FROM @Afectados);
IF @N > 1
    THROW 51073, 'Se esperaba como maximo 1 fila en RolPermisos. Se revierte.', 1;

DECLARE @Claims TABLE (Id int);
DELETE FROM dbo.AspNetRoleClaims
OUTPUT deleted.Id INTO @Claims
WHERE RoleId = @RoleId AND ClaimType = N'Permission' AND ClaimValue = N'ventas.authorize';

DECLARE @NC int = (SELECT COUNT(*) FROM @Claims);
IF @NC > 1
    THROW 51074, 'Se esperaba como maximo 1 fila en AspNetRoleClaims. Se revierte.', 1;

-- Verificaciones posteriores: nada mas cambio.
IF (SELECT COUNT(*) FROM dbo.RolPermisos WHERE NOT (RoleId = @RoleId AND ClaimValue = N'ventas.authorize')) <> @PermOtrosAntes
   OR (SELECT COUNT(*) FROM dbo.RolPermisos WHERE IsDeleted = 0 AND NOT (RoleId = @RoleId AND ClaimValue = N'ventas.authorize')) <> @VigOtrosAntes
   OR (SELECT COUNT(*) FROM dbo.AspNetRoleClaims WHERE NOT (RoleId = @RoleId AND ClaimType = N'Permission' AND ClaimValue = N'ventas.authorize')) <> @ClaimsOtrosAntes
    THROW 51075, 'Cambio inesperado en otros permisos o claims. Se revierte.', 1;

IF EXISTS (SELECT 1 FROM dbo.RolPermisos WHERE RoleId = @RoleId AND ClaimValue = N'ventas.authorize' AND IsDeleted = 0)
    THROW 51076, 'El permiso sigue vigente. Se revierte.', 1;

DECLARE @IdTxt varchar(20) = ISNULL((SELECT TOP 1 CAST(Id AS varchar(20)) FROM @Afectados), '-');
PRINT CONCAT('RolPermisos dados de baja: ', @N, ' (Id: ', @IdTxt, ')');
PRINT CONCAT('AspNetRoleClaims eliminados: ', @NC);

IF @DryRun = 1
BEGIN
    PRINT 'DryRun=1: se revierte.';
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT 'Aplicado.';
END;

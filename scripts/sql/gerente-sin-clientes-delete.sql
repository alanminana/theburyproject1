/*
  Regla aprobada: la baja de clientes se reserva a Administrador y SuperAdmin.

  El codigo ya lo impone (ClaimsPrincipal.PuedeEliminarClientes exige el rol) y el sembrador ya no entrega
  clientes.delete a Gerente. Esta es la correccion OPCIONAL de los DATOS en una base existente: baja logica del permiso
  clientes.delete del rol Gerente en RolPermisos. No toca ningun otro rol ni permiso.

  NO SE APLICA A TheBuryProjectDb SIN AUTORIZACION EXPLICITA DEL PROPIETARIO.

  Uso (sqlcmd; -v obligatorio):
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i gerente-sin-clientes-delete.sql -v DryRun=1 BaseEsperada=TheBuryProjectDb   (ensayo)
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i gerente-sin-clientes-delete.sql -v DryRun=0 BaseEsperada=TheBuryProjectDb   (aplica)
  Reversion: UPDATE dbo.RolPermisos SET IsDeleted = 0 WHERE Id = <el Id impreso al aplicar>.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @BaseEsperada sysname = N'$(BaseEsperada)';

IF DB_NAME() <> @BaseEsperada
    THROW 51061, 'Base de destino incorrecta. No se modifica nada.', 1;

BEGIN TRANSACTION;

DECLARE @RoleId nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Gerente');
IF @RoleId IS NULL
    THROW 51062, 'No existe el rol Gerente. No se modifica nada.', 1;

DECLARE @Afectados TABLE (Id int);
UPDATE dbo.RolPermisos SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = N'script-gerente-sin-clientes-delete'
OUTPUT inserted.Id INTO @Afectados
WHERE RoleId = @RoleId AND ClaimValue = N'clientes.delete' AND IsDeleted = 0;

DECLARE @N int = (SELECT COUNT(*) FROM @Afectados);
IF @N > 1
    THROW 51063, 'Se esperaba como maximo 1 fila. Se revierte.', 1;

DECLARE @IdTxt varchar(20) = ISNULL((SELECT TOP 1 CAST(Id AS varchar(20)) FROM @Afectados), '-');
PRINT CONCAT('Filas dadas de baja: ', @N, ' (Id: ', @IdTxt, ')');

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

-- P-07: verificación SOLO LECTURA de las cuentas de una base SQL Server de producción nueva.
-- No modifica nada (solo lee; usa la tabla temporal #c de tempdb). Ejecutar con sqlcmd -I contra la base a verificar.
-- No imprime hashes ni contraseñas. Runbook: docs/puesta-en-marcha-p07-p02.md §3.
--
--   @Cierre = 0  (defecto): durante el alta de cuentas. Solo informa y marca como FALLA lo que NUNCA debe existir (cuentas sintéticas activas).
--   @Cierre = 1: control final antes de operar. Además exige >= 2 SuperAdmin activos y sin bloqueo (propietario + recuperación).
--
-- Cada fila es «control | valor | esperado | estado». Cualquier FALLA detiene el procedimiento.
SET NOCOUNT ON;
DECLARE @Cierre bit = 0;

IF OBJECT_ID(N'tempdb..#c') IS NOT NULL DROP TABLE #c;
CREATE TABLE #c (n int identity(1,1), control nvarchar(160), valor nvarchar(400), esperado nvarchar(160), estado nvarchar(10));

DECLARE @ahora datetimeoffset = SYSDATETIMEOFFSET();

-- «Operativa» = activa y sin bloqueo vigente (el bloqueo manual es un LockoutEnd futuro).
;WITH u AS (
  SELECT x.Id, x.UserName, x.Email, x.Activo,
         CASE WHEN x.Activo = 1 AND (x.LockoutEnd IS NULL OR x.LockoutEnd <= @ahora) THEN 1 ELSE 0 END AS Operativa
  FROM AspNetUsers x
)
SELECT * INTO #u FROM u;

INSERT #c SELECT N'Cuentas totales / activas / operativas',
       CAST(COUNT(*) AS nvarchar(10)) + N' / ' + CAST(SUM(CAST(Activo AS int)) AS nvarchar(10)) + N' / ' + CAST(SUM(Operativa) AS nvarchar(10)),
       N'(informativo)', N'INFO' FROM #u;

DECLARE @sa int = (SELECT COUNT(DISTINCT u.Id) FROM #u u
                   JOIN AspNetUserRoles ur ON ur.UserId = u.Id
                   JOIN AspNetRoles r ON r.Id = ur.RoleId
                   WHERE r.Name = N'SuperAdmin' AND u.Operativa = 1);
INSERT #c VALUES (N'SuperAdmin operativos', CAST(@sa AS nvarchar(10)), N'>= 2 al cierre (propietario + recuperación)',
       CASE WHEN @Cierre = 0 THEN N'INFO' WHEN @sa >= 2 THEN N'OK' ELSE N'FALLA' END);

-- Cuentas que solo existen en ensayos/preproducción: nunca deben estar activas en la base operativa.
DECLARE @sint nvarchar(400) = (
  SELECT STRING_AGG(u.UserName, N', ') FROM #u u
  WHERE u.Activo = 1 AND (
        u.Email LIKE N'%@tbp.local' OR u.Email LIKE N'%@example.%' OR u.Email LIKE N'%.invalid' OR u.Email LIKE N'%@test.%'
     OR u.UserName LIKE N'p0[0-9].%' OR u.UserName LIKE N'p0[0-9]c.%'
     OR u.UserName IN (N'administrador', N'cajero', N'contador', N'gerente', N'repositor', N'tecnico', N'vendedor')));
INSERT #c VALUES (N'Cuentas sintéticas/demo activas', ISNULL(@sint, N'0'), N'0 (nunca en la base operativa)',
       CASE WHEN @sint IS NULL THEN N'OK' ELSE N'FALLA' END);

INSERT #c SELECT N'Cuenta inicial «' + u.UserName + N'» activa', CASE WHEN u.Activo = 1 THEN N'sí' ELSE N'no' END,
       N'al cierre: contraseña propia y guardada offline; desactivar solo si hay >= 2 SuperAdmin', N'INFO'
FROM #u u WHERE u.UserName = N'admin';

-- Detalle por cuenta (sin datos sensibles): usuario, correo, estado y roles.
INSERT #c SELECT N'Cuenta: ' + u.UserName,
       ISNULL(u.Email, N'(sin correo)') + N' | ' + CASE WHEN u.Operativa = 1 THEN N'operativa' WHEN u.Activo = 1 THEN N'activa, bloqueada' ELSE N'inactiva' END
         + N' | ' + ISNULL((SELECT STRING_AGG(r.Name, N',') FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id), N'(sin rol)'),
       N'(informativo)', N'INFO'
FROM #u u ORDER BY u.UserName;

SELECT control, valor, esperado, estado FROM #c ORDER BY n;
DECLARE @fallas int = (SELECT COUNT(*) FROM #c WHERE estado = N'FALLA');
DROP TABLE #u;
DROP TABLE #c;
IF @fallas > 0 RAISERROR(N'RESULTADO: %d FALLA(S). No avanzar con la puesta en marcha.', 16, 1, @fallas); -- con sqlcmd -b corta con código de salida distinto de 0
ELSE PRINT N'RESULTADO: sin FALLA.';

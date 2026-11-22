-- P-07: verificación SOLO LECTURA de que el crédito está CERRADO (cupos en $0) antes de habilitar operaciones.
-- No modifica nada (usa la tabla temporal #c de tempdb). Ejecutar con sqlcmd -I contra la base a verificar.
-- Decisión del propietario (2026-11-22): condición inicial = cupo $0 en TODOS los puntajes, montos financiables $0,
-- punitorios 0 % / período 30 / gracia 12, sin planes de cuotas. Todo se cambia luego POR PANTALLA, nunca por SQL.
-- Procedimiento: docs/puesta-en-marcha-p07-p02.md §7 y docs/cierre-credito-cupos-cero-20261122.md.
--
--   @Operando = 0 (defecto): instalación nueva, antes de abrir el crédito. Cualquier cupo > 0 es FALLA.
--   @Operando = 1: el propietario ya definió cupos a propósito. Los cupos pasan a INFO (no FALLA); el resto se informa igual.
--
-- Cada fila es «control | valor | esperado | estado». Cualquier FALLA detiene el procedimiento.
SET NOCOUNT ON;
DECLARE @Operando bit = 0;

IF OBJECT_ID(N'tempdb..#c') IS NOT NULL DROP TABLE #c;
CREATE TABLE #c (n int identity(1,1), control nvarchar(160), valor nvarchar(400), esperado nvarchar(160), estado nvarchar(10));

-- 1) Cupo por puntaje (autoridad del cupo: PuntajeCreditoLimites). Los puntajes sin fila activa no dan cupo (falla cerrado).
DECLARE @cuposPos nvarchar(400) = (
  SELECT STRING_AGG(CONCAT(N'puntaje ', Puntaje, N' = ', CONVERT(nvarchar(40), LimiteMonto)), N'; ')
  FROM PuntajeCreditoLimites WHERE Activo = 1 AND LimiteMonto > 0);
INSERT #c VALUES (N'Cupos por puntaje con monto > 0 (activos)', ISNULL(@cuposPos, N'ninguno'), N'ninguno',
       CASE WHEN @cuposPos IS NULL THEN N'OK' WHEN @Operando = 1 THEN N'INFO' ELSE N'FALLA' END);

INSERT #c SELECT N'Puntajes con cupo configurado y activo', CAST(COUNT(*) AS nvarchar(10)), N'(informativo: el resto no da cupo)', N'INFO'
FROM PuntajeCreditoLimites WHERE Activo = 1;

-- 2) Tabla «monto máximo financiable» (solo configuración: no la consulta ninguna regla de venta; se deja en 0 igualmente).
DECLARE @montosPos nvarchar(400) = (
  SELECT STRING_AGG(CONCAT(N'puntaje ', Puntaje, N' = ', CONVERT(nvarchar(40), MontoMaximoFinanciable)), N'; ')
  FROM ConfiguracionCreditoMontosPorPuntaje WHERE Activo = 1 AND MontoMaximoFinanciable > 0);
INSERT #c VALUES (N'Montos máximos financiables > 0', ISNULL(@montosPos, N'ninguno'), N'ninguno',
       CASE WHEN @montosPos IS NULL THEN N'OK' WHEN @Operando = 1 THEN N'INFO' ELSE N'FALLA' END);

-- 3) Cupos por cliente (autorizaciones deliberadas con permiso clientes.managecreditlimits): no deben existir al inicio.
DECLARE @clientesLim int = (SELECT COUNT(*) FROM Clientes
                            WHERE IsDeleted = 0 AND (ISNULL(LimiteCredito, 0) > 0 OR ISNULL(MontoMaximoPersonalizado, 0) > 0));
INSERT #c VALUES (N'Clientes con límite manual / monto máximo personalizado > 0', CAST(@clientesLim AS nvarchar(10)), N'0',
       CASE WHEN @clientesLim = 0 THEN N'OK' WHEN @Operando = 1 THEN N'INFO' ELSE N'FALLA' END);

DECLARE @clientesCfg int = (SELECT COUNT(*) FROM ClientesCreditoConfiguraciones
                            WHERE ISNULL(LimiteOverride, 0) > 0 OR ISNULL(ExcepcionDelta, 0) > 0 OR NivelCreditoManual IS NOT NULL);
INSERT #c VALUES (N'Clientes con override de límite, excepción de cupo o puntaje manual', CAST(@clientesCfg AS nvarchar(10)), N'0',
       CASE WHEN @clientesCfg = 0 THEN N'OK' WHEN @Operando = 1 THEN N'INFO' ELSE N'FALLA' END);

-- 4) Quién puede saltear el cupo por excepción (ventas.authorize) o fijar cupos (clientes.managecreditlimits). Informativo:
--    al inicio solo debería figurar SuperAdmin; revisar antes de dar roles al personal.
INSERT #c SELECT N'Rol con permiso «' + rp.ClaimValue + N'»', r.Name, N'(informativo: solo SuperAdmin al inicio)', N'INFO'
FROM RolPermisos rp JOIN AspNetRoles r ON r.Id = rp.RoleId
WHERE rp.IsDeleted = 0 AND rp.ClaimValue IN (N'ventas.authorize', N'clientes.managecreditlimits')
GROUP BY rp.ClaimValue, r.Name;

-- 5) Planes de cuotas de Crédito Personal: informativo (sin planes activos no se puede financiar; los planes solos NO dan cupo).
INSERT #c SELECT N'Planes de Crédito Personal activos', CAST(COUNT(*) AS nvarchar(10)), N'(informativo: 0 al inicio)', N'INFO'
FROM ConfiguracionCreditoPersonalCuotas WHERE Activo = 1;

-- 6) Punitorios: versión vigente esperada 0 % / 30 días / gracia 12 (solo informa la divergencia; se edita por pantalla).
DECLARE @pPorc decimal(18,4), @pPer int, @pGra int, @pDesde date;
SELECT TOP 1 @pPorc = Porcentaje, @pPer = PeriodoDias, @pGra = DiasGracia, @pDesde = CONVERT(date, VigenteDesde)
FROM ConfiguracionesPunitorio WHERE IsDeleted = 0 AND Activa = 1 ORDER BY VigenteDesde DESC, Id DESC;
INSERT #c VALUES (N'Punitorio vigente (más reciente)',
       CASE WHEN @pPer IS NULL THEN N'(sin versión cargada)'
            ELSE CONCAT(CONVERT(nvarchar(20), @pPorc), N' % / ', @pPer, N' días / gracia ', @pGra, N' (desde ', CONVERT(nvarchar(10), @pDesde), N')') END,
       N'0 % / 30 días / gracia 12',
       CASE WHEN @pPer IS NOT NULL AND @pPorc = 0 AND @pPer = 30 AND @pGra = 12 THEN N'OK' ELSE N'INFO' END);

SELECT control, valor, esperado, estado FROM #c ORDER BY n;
IF EXISTS (SELECT 1 FROM #c WHERE estado = N'FALLA') PRINT N'RESULTADO: HAY FALLAS. No habilitar operaciones de crédito.';
ELSE PRINT N'RESULTADO: sin FALLA.';
DROP TABLE #c;

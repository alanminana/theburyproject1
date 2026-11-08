-- Marca la base ACTUAL como clon de QA para la siembra sintetica. Ejecutar SOLO sobre un clon restaurado (nunca TheBuryProjectDb).
-- Rechaza el nombre de la base real y exige que el nombre indique QA (termina en _qa o _s*, p. ej. TheBuryProjectDb_qa).
SET NOCOUNT ON;
IF DB_NAME() = N'TheBuryProjectDb' OR DB_NAME() NOT LIKE N'%[_]qa%' AND DB_NAME() NOT LIKE N'%[_]s%'
BEGIN
    RAISERROR(N'marcar-clon: la base "%s" no parece un clon de QA (debe contener _qa o _s y no ser TheBuryProjectDb). No se marca nada.', 16, 1, N'?');
    SET NOEXEC ON;
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'csp_sintetico_clon')
    EXEC sp_addextendedproperty @name = N'csp_sintetico_clon', @value = N'1';
PRINT 'Base marcada como clon de QA (csp_sintetico_clon=1).';
GO
SET NOEXEC OFF;

/* ============================================================================
   Limpieza del sistema documental (motor de documentos) — procedimiento controlado

   Vacía SOLO la configuración y los documentos del motor documental:
     DocumentosGenerados, PaquetesDocumentalesItems, PaquetesDocumentales, ReglasDocumento,
     PlantillasDocumentoVersion, PlantillasDocumento, TiposDocumento.

   NO toca: ventas, clientes, créditos, cuotas, pagos, caja, productos, facturas, usuarios/roles/permisos,
   la auditoría (SeguridadEventosAuditoria), EmpresasConfiguracion, ni el sistema legado
   (ContratosVentaCredito y PlantillasContratoCredito, con sus PDFs).

   Documentos que NO se borran (se informan):
     * un documento importado del sistema anterior (ContratoLegadoId) cuyo contrato legado ya no existe:
       sería la única copia. Lo que lo referencia (plantilla/versión/tipo) también se conserva.

   Uso (siempre con un backup previo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d TheBuryProjectDb -I -v DryRun=1 -i limpiar-sistema-documental.sql
     sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d TheBuryProjectDb -I -v DryRun=0 -i limpiar-sistema-documental.sql

   DryRun=1 ejecuta todo y hace ROLLBACK (muestra qué pasaría). DryRun=0 confirma.
   Todo ocurre en una transacción: si un control falla (FK rotas, huérfanos o cambio en datos comerciales)
   se revierte completo. Es idempotente: sobre un sistema ya vacío no hace nada.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);

-- ---------------------------------------------------------------- conteos de control
DECLARE @tablasComerciales TABLE (tabla sysname PRIMARY KEY);
INSERT @tablasComerciales VALUES
    ('Clientes'), ('Ventas'), ('VentaDetalles'), ('Creditos'), ('Cuotas'), ('PagosCuota'), ('MovimientosCaja'),
    ('Productos'), ('Facturas'), ('Cotizaciones'), ('ContratosVentaCredito'), ('PlantillasContratoCredito'),
    ('AspNetUsers'), ('AspNetRoles'), ('RolPermisos'), ('Sucursales'), ('SeguridadEventosAuditoria');

DECLARE @comercialAntes TABLE (tabla sysname PRIMARY KEY, n bigint);
DECLARE @comercialDespues TABLE (tabla sysname PRIMARY KEY, n bigint);
DECLARE @docAntes TABLE (tabla sysname PRIMARY KEY, n bigint);
DECLARE @docDespues TABLE (tabla sysname PRIMARY KEY, n bigint);

DECLARE @t sysname, @sql nvarchar(400);

DECLARE c1 CURSOR LOCAL FAST_FORWARD FOR SELECT tabla FROM @tablasComerciales;
OPEN c1; FETCH NEXT FROM c1 INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(@t, 'U') IS NOT NULL
    BEGIN
        SET @sql = N'SELECT ''' + @t + N''', COUNT_BIG(*) FROM ' + QUOTENAME(@t);
        INSERT @comercialAntes EXEC (@sql);
    END
    FETCH NEXT FROM c1 INTO @t;
END
CLOSE c1; DEALLOCATE c1;

IF OBJECT_ID('DocumentosGenerados', 'U') IS NULL
BEGIN
    PRINT 'El esquema del motor documental no existe en esta base: nada para limpiar.';
    RETURN;
END

INSERT @docAntes
SELECT 'TiposDocumento', COUNT_BIG(*) FROM TiposDocumento UNION ALL
SELECT 'PlantillasDocumento', COUNT_BIG(*) FROM PlantillasDocumento UNION ALL
SELECT 'PlantillasDocumentoVersion', COUNT_BIG(*) FROM PlantillasDocumentoVersion UNION ALL
SELECT 'ReglasDocumento', COUNT_BIG(*) FROM ReglasDocumento UNION ALL
SELECT 'PaquetesDocumentales', COUNT_BIG(*) FROM PaquetesDocumentales UNION ALL
SELECT 'PaquetesDocumentalesItems', COUNT_BIG(*) FROM PaquetesDocumentalesItems UNION ALL
SELECT 'DocumentosGenerados', COUNT_BIG(*) FROM DocumentosGenerados;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Documentos generados. Se conservan solo las copias de contratos legados cuyo original ya no existe.
    DELETE d
    FROM DocumentosGenerados d
    WHERE d.ContratoLegadoId IS NULL
       OR EXISTS (SELECT 1 FROM ContratosVentaCredito c WHERE c.Id = d.ContratoLegadoId);

    -- 2. Paquetes: items, luego paquetes (las reglas se borran antes que los paquetes que referencian).
    DELETE FROM ReglasDocumento;
    DELETE FROM PaquetesDocumentalesItems;
    DELETE FROM PaquetesDocumentales;

    -- 3. Versiones y plantillas que no sostienen ningún documento conservado.
    DELETE v
    FROM PlantillasDocumentoVersion v
    WHERE NOT EXISTS (SELECT 1 FROM DocumentosGenerados d WHERE d.PlantillaDocumentoVersionId = v.Id);

    DELETE p
    FROM PlantillasDocumento p
    WHERE NOT EXISTS (SELECT 1 FROM DocumentosGenerados d WHERE d.PlantillaDocumentoId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM PlantillasDocumentoVersion v WHERE v.PlantillaDocumentoId = p.Id);

    -- 4. Tipos documentales sin uso.
    DELETE t
    FROM TiposDocumento t
    WHERE NOT EXISTS (SELECT 1 FROM DocumentosGenerados d WHERE d.TipoDocumentoId = t.Id)
      AND NOT EXISTS (SELECT 1 FROM PlantillasDocumento p WHERE p.TipoDocumentoId = t.Id);

    -- ------------------------------------------------------------ controles previos al commit
    INSERT @docDespues
    SELECT 'TiposDocumento', COUNT_BIG(*) FROM TiposDocumento UNION ALL
    SELECT 'PlantillasDocumento', COUNT_BIG(*) FROM PlantillasDocumento UNION ALL
    SELECT 'PlantillasDocumentoVersion', COUNT_BIG(*) FROM PlantillasDocumentoVersion UNION ALL
    SELECT 'ReglasDocumento', COUNT_BIG(*) FROM ReglasDocumento UNION ALL
    SELECT 'PaquetesDocumentales', COUNT_BIG(*) FROM PaquetesDocumentales UNION ALL
    SELECT 'PaquetesDocumentalesItems', COUNT_BIG(*) FROM PaquetesDocumentalesItems UNION ALL
    SELECT 'DocumentosGenerados', COUNT_BIG(*) FROM DocumentosGenerados;

    DECLARE c2 CURSOR LOCAL FAST_FORWARD FOR SELECT tabla FROM @tablasComerciales;
    OPEN c2; FETCH NEXT FROM c2 INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID(@t, 'U') IS NOT NULL
        BEGIN
            SET @sql = N'SELECT ''' + @t + N''', COUNT_BIG(*) FROM ' + QUOTENAME(@t);
            INSERT @comercialDespues EXEC (@sql);
        END
        FETCH NEXT FROM c2 INTO @t;
    END
    CLOSE c2; DEALLOCATE c2;

    IF EXISTS (SELECT 1 FROM @comercialAntes a JOIN @comercialDespues d ON d.tabla = a.tabla WHERE a.n <> d.n)
        THROW 51001, 'Cambió el conteo de una tabla comercial: se revierte la limpieza.', 1;

    -- Huérfanos: reglas sin destino válido, versiones/plantillas/items sin padre, documentos sin plantilla/tipo.
    IF EXISTS (SELECT 1 FROM ReglasDocumento r WHERE
            (r.PlantillaDocumentoId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM PlantillasDocumento p WHERE p.Id = r.PlantillaDocumentoId))
         OR (r.PaqueteDocumentalId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM PaquetesDocumentales q WHERE q.Id = r.PaqueteDocumentalId)))
        THROW 51002, 'Quedarían reglas huérfanas: se revierte la limpieza.', 1;
    IF EXISTS (SELECT 1 FROM PlantillasDocumentoVersion v WHERE NOT EXISTS (SELECT 1 FROM PlantillasDocumento p WHERE p.Id = v.PlantillaDocumentoId))
        THROW 51003, 'Quedarían versiones huérfanas: se revierte la limpieza.', 1;
    IF EXISTS (SELECT 1 FROM PaquetesDocumentalesItems i WHERE NOT EXISTS (SELECT 1 FROM PaquetesDocumentales q WHERE q.Id = i.PaqueteDocumentalId)
                                                          OR NOT EXISTS (SELECT 1 FROM PlantillasDocumento p WHERE p.Id = i.PlantillaDocumentoId))
        THROW 51004, 'Quedarían items de paquete huérfanos: se revierte la limpieza.', 1;
    IF EXISTS (SELECT 1 FROM DocumentosGenerados d WHERE NOT EXISTS (SELECT 1 FROM PlantillasDocumento p WHERE p.Id = d.PlantillaDocumentoId)
                                                    OR NOT EXISTS (SELECT 1 FROM PlantillasDocumentoVersion v WHERE v.Id = d.PlantillaDocumentoVersionId)
                                                    OR NOT EXISTS (SELECT 1 FROM TiposDocumento t WHERE t.Id = d.TipoDocumentoId))
        THROW 51005, 'Quedarían documentos sin plantilla/versión/tipo: se revierte la limpieza.', 1;

    IF @DryRun = 1
    BEGIN
        PRINT '*** DRY RUN: se muestra el resultado y se hace ROLLBACK ***';
        SELECT a.tabla AS Entidad, a.n AS Antes, a.n - ISNULL(d.n, 0) AS Eliminados, ISNULL(d.n, 0) AS Despues
        FROM @docAntes a LEFT JOIN @docDespues d ON d.tabla = a.tabla ORDER BY a.tabla;
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;
        SELECT a.tabla AS Entidad, a.n AS Antes, a.n - ISNULL(d.n, 0) AS Eliminados, ISNULL(d.n, 0) AS Despues
        FROM @docAntes a LEFT JOIN @docDespues d ON d.tabla = a.tabla ORDER BY a.tabla;
    END

    SELECT a.tabla AS Comercial, a.n AS Antes, d.n AS Despues, CASE WHEN a.n = d.n THEN 'igual' ELSE 'CAMBIO' END AS Estado
    FROM @comercialAntes a JOIN @comercialDespues d ON d.tabla = a.tabla ORDER BY a.tabla;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @msg nvarchar(2048) = ERROR_MESSAGE();
    RAISERROR('Limpieza revertida: %s', 16, 1, @msg);
END CATCH

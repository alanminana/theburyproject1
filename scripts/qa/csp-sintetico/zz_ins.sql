-- Helper de siembra sintetica (solo para clones de QA): inserta una fila rellenando columnas NOT NULL sin default con valores neutros.
-- @ov: 'Col=literalSQL|Col2=literalSQL'. Devuelve el Id generado en @id.
-- GUARDA: solo corre en una base marcada como clon de QA (ver marcar-clon.sql). Si no, no ejecuta nada de lo que sigue.
IF DB_NAME() = N'TheBuryProjectDb'
   OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'csp_sintetico_clon' AND CAST(value AS nvarchar(10)) = N'1')
BEGIN
    RAISERROR(N'csp-sintetico: esta base no esta marcada como clon de QA (marcar-clon.sql) o es la base real. No se ejecuta nada.', 16, 1);
    SET NOEXEC ON;
END;
GO
IF OBJECT_ID('dbo.zz_ins') IS NOT NULL DROP PROCEDURE dbo.zz_ins;
GO
CREATE PROCEDURE dbo.zz_ins @table sysname, @ov nvarchar(max) = N'', @id int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- Guardas en cada llamada: clon marcado + lista cerrada de tablas (esquema dbo) + tabla delimitada con QUOTENAME.
    IF DB_NAME() = N'TheBuryProjectDb' OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'csp_sintetico_clon' AND CAST(value AS nvarchar(10)) = N'1')
        THROW 52001, 'zz_ins: la base no esta marcada como clon de QA.', 1;
    IF @table NOT IN (N'ProductoUnidades', N'Clientes', N'DocumentosCliente', N'Ventas', N'VentaDetalles', N'Devoluciones', N'MercadoLibreAccounts',
                      N'MercadoLibreOrders', N'MercadoLibreOrderItems', N'MercadoLibreQuestions', N'MercadoLibreListings', N'MercadoLibrePriceBatches', N'MercadoLibrePriceBatchItems')
        THROW 52002, 'zz_ins: tabla no permitida.', 1;
    -- @ov: 'Col=valor|Col2=valor'. Se SEPARA primero (igual que el armado posterior) y se valida CADA campo: la columna debe existir en la tabla y
    -- el valor, tras quitar los literales entre comillas y SYSUTCDATETIME()/DATEADD(day,1,SYSUTCDATETIME())/NEWID(), solo puede contener [A-Za-z0-9_ ]
    -- (colación binaria): sin parentesis, ';', operadores, tabulaciones ni saltos de línea. Un literal que contenga '|' queda con comillas sin cerrar y se rechaza.
    DECLARE @campo nvarchar(max), @cn nvarchar(max), @cv nvarchar(max), @chk nvarchar(max), @p int, @q int;
    DECLARE vc CURSOR LOCAL FAST_FORWARD FOR SELECT value FROM STRING_SPLIT(ISNULL(@ov, N''), N'|') WHERE LEN(value) > 0;
    OPEN vc; FETCH NEXT FROM vc INTO @campo;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @p = CHARINDEX(N'=', @campo);
        IF @p < 2 THROW 52005, 'zz_ins: campo de @ov sin formato Col=valor.', 1;
        SET @cn = LEFT(@campo, @p - 1);
        SET @cv = SUBSTRING(@campo, @p + 1, 4000);
        IF @cn LIKE N'%[^A-Za-z0-9_]%' COLLATE Latin1_General_BIN
           OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@table)) AND name = @cn)
            THROW 52006, 'zz_ins: columna no valida en @ov.', 1;
        SET @chk = REPLACE(@cv, N'''''', N'');
        WHILE 1 = 1
        BEGIN
            SET @p = CHARINDEX(N'''', @chk);
            IF @p = 0 BREAK;
            SET @q = CHARINDEX(N'''', @chk, @p + 1);
            IF @q = 0 THROW 52003, 'zz_ins: comillas sin cerrar en @ov.', 1;
            SET @chk = STUFF(@chk, @p, @q - @p + 1, N'');
        END;
        SET @chk = REPLACE(REPLACE(REPLACE(@chk, N'DATEADD(day,1,SYSUTCDATETIME())', N' '), N'SYSUTCDATETIME()', N' '), N'NEWID()', N' ');
        IF @chk LIKE N'%[^A-Za-z0-9_ ]%' COLLATE Latin1_General_BIN
            THROW 52004, 'zz_ins: valor de @ov con construcciones no permitidas.', 1;
        FETCH NEXT FROM vc INTO @campo;
    END;
    CLOSE vc; DEALLOCATE vc;
    DECLARE @cols nvarchar(max) = N'', @vals nvarchar(max) = N'';
    DECLARE @name sysname, @type sysname, @len int, @nullable bit, @hasdef bit, @val nvarchar(max);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT col.name, ty.name, col.max_length, col.is_nullable, CASE WHEN col.default_object_id <> 0 THEN 1 ELSE 0 END
        FROM sys.columns col JOIN sys.types ty ON ty.user_type_id = col.user_type_id
        WHERE col.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@table)) AND col.is_identity = 0 AND col.is_computed = 0 AND ty.name NOT IN ('timestamp', 'rowversion')
        ORDER BY col.column_id;
    OPEN c; FETCH NEXT FROM c INTO @name, @type, @len, @nullable, @hasdef;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @val = NULL;
        -- override explicito
        SELECT @val = SUBSTRING(s.value, CHARINDEX('=', s.value) + 1, 4000)
        FROM STRING_SPLIT(@ov, '|') s WHERE LEFT(s.value, CHARINDEX('=', s.value) - 1) = @name;
        IF @val IS NULL AND @nullable = 0 AND @hasdef = 0
            SET @val = CASE
                WHEN @type IN ('int','bigint','smallint','tinyint','bit','decimal','numeric','float','real','money') THEN N'0'
                WHEN @type IN ('nvarchar','varchar','nchar','char') THEN N'LEFT(CONVERT(nvarchar(36), NEWID()), ' + CAST(CASE WHEN @len = -1 THEN 20 WHEN @type LIKE 'n%' THEN @len / 2 ELSE @len END AS nvarchar(10)) + N')'
                WHEN @type IN ('datetime','datetime2','smalldatetime','datetimeoffset') THEN N'SYSUTCDATETIME()'
                WHEN @type = 'date' THEN N'CAST(GETDATE() AS date)'
                WHEN @type = 'time' THEN N'CAST(GETDATE() AS time)'
                WHEN @type = 'uniqueidentifier' THEN N'NEWID()'
                ELSE N'0x' END;
        IF @val IS NOT NULL
        BEGIN
            SET @cols += CASE WHEN @cols = N'' THEN N'' ELSE N', ' END + QUOTENAME(@name);
            SET @vals += CASE WHEN @vals = N'' THEN N'' ELSE N', ' END + @val;
        END;
        FETCH NEXT FROM c INTO @name, @type, @len, @nullable, @hasdef;
    END;
    CLOSE c; DEALLOCATE c;
    DECLARE @sql nvarchar(max) = N'INSERT INTO dbo.' + QUOTENAME(@table) + N' (' + @cols + N') VALUES (' + @vals + N'); SET @i = SCOPE_IDENTITY();';
    EXEC sp_executesql @sql, N'@i int OUTPUT', @i = @id OUTPUT;
END
GO
GO
SET NOEXEC OFF;

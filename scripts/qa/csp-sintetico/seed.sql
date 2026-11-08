-- Siembra SINTETICA para validar handlers/CSP. SOLO EN UN CLON DE QA (nunca TheBuryProjectDb). Requiere zz_ins.sql ya cargado.
-- Ejecutar: sqlcmd -S "(localdb)\MSSQLLocalDB" -d <clon> -I -b -i seed.sql   (admin = usuario de QA; producto 1 y 2 deben existir)
SET NOCOUNT ON;
GO
-- GUARDA: solo corre en una base marcada como clon de QA (ver marcar-clon.sql). Si no, no ejecuta nada de lo que sigue.
IF DB_NAME() = N'TheBuryProjectDb'
   OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'csp_sintetico_clon' AND CAST(value AS nvarchar(10)) = N'1')
BEGIN
    RAISERROR(N'csp-sintetico: esta base no esta marcada como clon de QA (marcar-clon.sql) o es la base real. No se ejecuta nada.', 16, 1);
    SET NOEXEC ON;
END;
GO
DECLARE @i int, @c int, @v int, @d int, @o int, @q int, @l int, @b int, @ov nvarchar(800), @adm nvarchar(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE UserName = 'admin');
UPDATE Productos SET RequiereNumeroSerie = 1 WHERE Id = 1;
EXEC zz_ins 'ProductoUnidades', 'ProductoId=1|CodigoInternoUnidad=N''U-001''|NumeroSerie=N''SN-O''''BRIEN-1''|Estado=0', @i OUT;
EXEC zz_ins 'ProductoUnidades', 'ProductoId=1|CodigoInternoUnidad=N''U-002''|NumeroSerie=N''SN-2''|Estado=0', @i OUT;
EXEC zz_ins 'ProductoUnidades', 'ProductoId=1|CodigoInternoUnidad=N''U-003''|NumeroSerie=N''SN-3''|Estado=5', @i OUT;
EXEC zz_ins 'ProductoUnidades', 'ProductoId=1|CodigoInternoUnidad=N''U-004''|NumeroSerie=N''SN-4''|Estado=6', @i OUT;
EXEC zz_ins 'Clientes', 'NumeroDocumento=N''30111222''|Nombre=N''Ana''|Apellido=N''Sintetica''|Activo=1|IsDeleted=0', @c OUT;
SET @ov = N'ClienteId=' + CAST(@c AS nvarchar(20)) + N'|TipoDocumento=0|NombreArchivo=N''dni.pdf''|RutaArchivo=N''x/dni.pdf''|Estado=0'; EXEC zz_ins 'DocumentosCliente', @ov, @d OUT;
SET @ov = N'ClienteId=' + CAST(@c AS nvarchar(20)) + N'|TipoDocumento=1|NombreArchivo=N''recibo.pdf''|RutaArchivo=N''x/recibo.pdf''|Estado=1'; EXEC zz_ins 'DocumentosCliente', @ov, @d OUT;
SET @ov = N'ClienteId=' + CAST(@c AS nvarchar(20)) + N'|Numero=N''VTA-SINT-1''|Estado=0'; EXEC zz_ins 'Ventas', @ov, @v OUT;
SET @ov = N'VentaId=' + CAST(@v AS nvarchar(20)) + N'|ClienteId=' + CAST(@c AS nvarchar(20)) + N'|NumeroDevolucion=N''DEV-SINT-1''|Estado=0'; EXEC zz_ins 'Devoluciones', @ov, @d OUT;
SET @ov = N'ClienteId=' + CAST(@c AS nvarchar(20)) + N'|Numero=N''VTA-SINT-C1''|Estado=2|VendedorUserId=N''' + REPLACE(@adm, N'''', N'''''') + N'''|FechaVenta=SYSUTCDATETIME()'; EXEC zz_ins 'Ventas', @ov, @v OUT;
SET @ov = N'VentaId=' + CAST(@v AS nvarchar(20)) + N'|ProductoId=1|Cantidad=2|PrecioUnitario=1000|Subtotal=2000|PorcentajeComision=5|ComisionMonto=100'; EXEC zz_ins 'VentaDetalles', @ov, @d OUT;
SET @ov = N'ClienteId=' + CAST(@c AS nvarchar(20)) + N'|Numero=N''VTA-SINT-C2''|Estado=2|VendedorUserId=N''' + REPLACE(@adm, N'''', N'''''') + N'''|FechaVenta=SYSUTCDATETIME()'; EXEC zz_ins 'Ventas', @ov, @v OUT;
SET @ov = N'VentaId=' + CAST(@v AS nvarchar(20)) + N'|ProductoId=2|Cantidad=1|PrecioUnitario=3000|Subtotal=3000'; EXEC zz_ins 'VentaDetalles', @ov, @d OUT;
-- Mercado Libre (cuenta ficticia con tokens falsos; NO usar contra la API real)
-- El clon puede traer cuentas reales de ML: se desactivan y se les borran los tokens ANTES de crear la cuenta ficticia (nada sale a la API real).
UPDATE MercadoLibreAccounts SET Activa = 0, AccessTokenEncrypted = N'', RefreshTokenEncrypted = N'';
IF EXISTS (SELECT 1 FROM MercadoLibreAccounts WHERE Activa <> 0 OR AccessTokenEncrypted <> N'' OR RefreshTokenEncrypted <> N'')
    THROW 52010, 'seed: no se pudieron desactivar las cuentas de Mercado Libre del clon; se aborta antes de sembrar.', 1;
DECLARE @acc int, @accs nvarchar(20);
EXEC zz_ins 'MercadoLibreAccounts', 'Nickname=N''SINT_USER''|Activa=1|AccessTokenEncrypted=N''x''|RefreshTokenEncrypted=N''x''|AccessTokenExpiresAtUtc=DATEADD(day,1,SYSUTCDATETIME())', @acc OUT;
SET @accs = CAST(@acc AS nvarchar(20));
SET @ov = N'AccountId=' + @accs + N'|MeliOrderId=900001|Status=N''paid''|TotalAmount=1500|EstadoInterno=0|FechaCreacionUtc=SYSUTCDATETIME()'; EXEC zz_ins 'MercadoLibreOrders', @ov, @o OUT; --MeliOrderId=900001|Status=N''paid''|TotalAmount=1500|EstadoInterno=0|FechaCreacionUtc=SYSUTCDATETIME()', @o OUT;
SET @ov = N'OrderId=' + CAST(@o AS nvarchar(20)) + N'|ItemId=N''MLA1''|Titulo=N''Item sintetico''|Cantidad=1|PrecioUnitario=1500'; EXEC zz_ins 'MercadoLibreOrderItems', @ov, @i OUT;
SET @ov = N'AccountId=' + @accs + N'|QuestionId=800001|TextoPregunta=N''Hola, hay stock?''|Estado=0|EsSimulada=1'; EXEC zz_ins 'MercadoLibreQuestions', @ov, @q OUT;
SET @ov = N'AccountId=' + @accs + N'|ItemId=N''MLA777''|Titulo=N''Publicacion sintetica''|Precio=2000|AvailableQuantity=3|Status=N''active''|TieneVariaciones=0|ProductoId=1'; EXEC zz_ins 'MercadoLibreListings', @ov, @l OUT;
EXEC zz_ins 'MercadoLibrePriceBatches', 'Nombre=N''Lote sintetico''|Estado=0|Origen=0|ValorAjustePorcentaje=5|CantidadPublicaciones=1|SolicitadoPor=N''admin''|AplicadoEnSimulacion=1', @b OUT;
SET @ov = N'BatchId=' + CAST(@b AS nvarchar(20)) + N'|ListingId=' + CAST(@l AS nvarchar(20)) + N'|ItemId=N''MLA777''|Titulo=N''Publicacion sintetica''|PrecioAnterior=2000|PrecioNuevo=2100'; EXEC zz_ins 'MercadoLibrePriceBatchItems', @ov, @i OUT;
PRINT 'Siembra sintetica lista.';

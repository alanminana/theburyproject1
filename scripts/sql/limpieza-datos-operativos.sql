/*
  LIMPIEZA DE DATOS OPERATIVOS DE PRUEBA - TheBuryProject
  =======================================================
  Elimina los datos operativos ficticios (ventas, cotizaciones, facturas, creditos, caja, cheques, compras, stock,
  notificaciones, tickets, ...) conservando estructura, migraciones, roles/permisos, usuarios, configuracion,
  plantillas y catalogos. Un solo batch, transaccional, SIN deshabilitar ninguna restriccion.

  Uso (sqlcmd, modo SQLCMD):
    sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -i limpieza-datos-operativos.sql ^
      -v BaseEsperada="TheBuryProjectDb" Ejecutar=0 Clientes=eliminar Proveedores=eliminar Productos=conservar ^
         Auditoria=conservar ReiniciarNumeraciones=0 MaxMinutosRespaldo=60

  Variables:
    BaseEsperada          Nombre EXACTO de la base. Si DB_NAME() es otro, aborta.
    Ejecutar              0 = ensayo (todo se revierte con ROLLBACK, solo informa); 1 = confirma (COMMIT).
    Clientes              eliminar | conservar   (incluye documentos de cliente, historial de puntaje, garantes)
    Proveedores           eliminar | conservar   (incluye sus vinculos con categorias, marcas y productos)
    Productos             eliminar | conservar   (conservar => stock en 0 y unidades fisicas eliminadas)
    Auditoria             eliminar | conservar   (SeguridadEventosAuditoria)
    ReiniciarNumeraciones 0 | 1  (1 = reinicia identities de las tablas vaciadas, UltimoNumero de TiposDocumento
                                   y contadores de ventas; 0 = no toca ninguna numeracion almacenada)
    MaxMinutosRespaldo    Antiguedad maxima del ultimo respaldo completo registrado en msdb.

  Guardas (abortan sin cambiar nada): base distinta; historial de migraciones distinto de 135 o sin la migracion
  H12; otras conexiones de usuario abiertas a la base (cerrar la aplicacion); respaldo mas viejo que
  MaxMinutosRespaldo; tablas conservadas con huella (conteo + checksum) distinta tras la limpieza; filas que
  violan restricciones (DBCC CHECKCONSTRAINTS); tablas vaciadas con filas; caja abierta o stock restante.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Opciones que exigen los indices filtrados / columnas calculadas del esquema (sqlcmd las deja en OFF por defecto).
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;

DECLARE @BaseEsperada sysname = N'$(BaseEsperada)';
IF N'$(Ejecutar)' NOT IN (N'0', N'1') OR N'$(ReiniciarNumeraciones)' NOT IN (N'0', N'1')
    THROW 50000, N'Ejecutar y ReiniciarNumeraciones deben ser exactamente 0 o 1.', 1;
IF TRY_CAST(N'$(MaxMinutosRespaldo)' AS int) IS NULL OR TRY_CAST(N'$(MaxMinutosRespaldo)' AS int) NOT BETWEEN 1 AND 1440
    THROW 50000, N'MaxMinutosRespaldo debe ser un entero entre 1 y 1440.', 1;
DECLARE @Ejecutar bit = $(Ejecutar);
DECLARE @DelClientes bit = CASE LOWER(N'$(Clientes)') WHEN N'eliminar' THEN 1 WHEN N'conservar' THEN 0 ELSE NULL END;
DECLARE @DelProveedores bit = CASE LOWER(N'$(Proveedores)') WHEN N'eliminar' THEN 1 WHEN N'conservar' THEN 0 ELSE NULL END;
DECLARE @DelProductos bit = CASE LOWER(N'$(Productos)') WHEN N'eliminar' THEN 1 WHEN N'conservar' THEN 0 ELSE NULL END;
DECLARE @DelAuditoria bit = CASE LOWER(N'$(Auditoria)') WHEN N'eliminar' THEN 1 WHEN N'conservar' THEN 0 ELSE NULL END;
DECLARE @Reiniciar bit = $(ReiniciarNumeraciones);
DECLARE @MaxMin int = $(MaxMinutosRespaldo);
DECLARE @n bigint, @msg nvarchar(2000), @sql nvarchar(max), @t sysname;

IF @DelClientes IS NULL OR @DelProveedores IS NULL OR @DelProductos IS NULL OR @DelAuditoria IS NULL
    THROW 50001, N'Variables Clientes/Proveedores/Productos/Auditoria deben ser eliminar o conservar.', 1;
IF DB_NAME() <> @BaseEsperada
BEGIN SET @msg = N'Base inesperada: ' + DB_NAME() + N' (se esperaba ' + @BaseEsperada + N'). No se hizo ningun cambio.'; THROW 50002, @msg, 1; END
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 135
 OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261105142232_H12CostoServiciosVenta')
    THROW 50003, N'El historial de migraciones no es el esperado (135, con H12CostoServiciosVenta). No se hizo ningun cambio.', 1;
IF EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID AND is_user_process = 1)
    THROW 50004, N'Hay otras conexiones abiertas a la base. Cerrar la aplicacion y las herramientas antes de limpiar.', 1;
-- Respaldo: el mas reciente completo de ESTA incarnation de la base (mismo database_guid), dentro de la ventana y con un
-- unico archivo que existe y pasa RESTORE VERIFYONLY ... WITH CHECKSUM ahora mismo.
DECLARE @bkFile nvarchar(520), @bkId int, @bkDevs int;
SELECT TOP (1) @bkId = bs.backup_set_id FROM msdb.dbo.backupset bs
 WHERE bs.database_name = DB_NAME() AND bs.type = 'D' AND bs.backup_finish_date >= DATEADD(MINUTE, -@MaxMin, GETDATE())
   AND bs.database_guid = (SELECT database_guid FROM sys.database_recovery_status WHERE database_id = DB_ID())
 ORDER BY bs.backup_finish_date DESC;
IF @bkId IS NULL
    THROW 50005, N'No hay un respaldo completo (de esta misma base) en los ultimos MaxMinutosRespaldo minutos. Hacer el respaldo COPY_ONLY y verificarlo.', 1;
SELECT @bkDevs = COUNT(*), @bkFile = MAX(mf.physical_device_name) FROM msdb.dbo.backupset bs JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = bs.media_set_id WHERE bs.backup_set_id = @bkId;
IF @bkDevs <> 1 THROW 50005, N'El respaldo mas reciente usa mas de un archivo; hacer uno de un solo archivo.', 1;
SET @sql = N'RESTORE VERIFYONLY FROM DISK = N''' + REPLACE(@bkFile, N'''', N'''''') + N''' WITH CHECKSUM;';
EXEC (@sql);   -- falla (y aborta) si el archivo no existe o esta danado
-- Si hay publicaciones de Mercado Libre, la limpieza de unidades fisicas las dejaria sin vinculo: revisarlas a mano.
IF EXISTS (SELECT 1 FROM dbo.MercadoLibreListings) OR EXISTS (SELECT 1 FROM dbo.MercadoLibreListingVariations)
    THROW 50006, N'Existen publicaciones de Mercado Libre; revisar y desvincular manualmente antes de limpiar.', 1;

-- ---- Huella de las tablas que deben quedar intactas -------------------------------------------------------------
CREATE TABLE #fp (Tabla sysname PRIMARY KEY, Expr nvarchar(max) NOT NULL, NAntes bigint NULL, CkAntes int NULL, NDespues bigint NULL, CkDespues int NULL);
INSERT #fp (Tabla, Expr) SELECT name, N'CHECKSUM(*)' FROM sys.tables WHERE name IN (
 N'__EFMigrationsHistory',N'AccionesModulo',N'AlicuotasIVA',N'AspNetRoleClaims',N'AspNetRoles',N'AspNetUserClaims',N'AspNetUserLogins',
 N'AspNetUserRoles',N'AspNetUsers',N'AspNetUserTokens',N'CajaVendedores',N'Categorias',N'Marcas',
 N'ConfiguracionCreditoMontosPorPuntaje',N'ConfiguracionCreditoPersonalCuotas',N'ConfiguracionCreditoPersonalCuotasSinRecargo',
 N'ConfiguracionesActualizacionDatosCliente',N'ConfiguracionesCredito',N'ConfiguracionesMora',N'ConfiguracionesPago',
 N'ConfiguracionesPunitorio',N'ConfiguracionesRentabilidad',N'ConfiguracionesScoringCliente',N'ConfiguracionesTarjeta',
 N'ConfiguracionPagoPlanes',N'EmpresasConfiguracion',N'ListasPrecios',N'ModulosSistema',N'PaquetesDocumentales',
 N'PaquetesDocumentalesItems',N'PerfilesCredito',N'PlantillasContratoCredito',N'PlantillasDocumento',N'PlantillasDocumentoVersion',
 N'PrefijosNumeracion',N'PuntajeCreditoLimites',N'ReglasDocumento',N'RolesMetadata',N'RolPermisos',N'ServiciosVentaPrecios',
 N'Sucursales',N'TerminosCondicionesAceptaciones',N'UmbralesAutorizacion');
-- Tablas con columnas que la limpieza puede tocar a proposito: huella solo de las columnas estables.
-- Todas las columnas EXCEPTO las que esta limpieza modifica a proposito y la rowversion (cambia con cualquier UPDATE).
DECLARE @eCajas nvarchar(max), @eTipos nvarchar(max), @eProd nvarchar(max), @eCli nvarchar(max);
SELECT @eCajas = N'CHECKSUM(' + STRING_AGG(QUOTENAME(name), N',') + N')' FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Cajas') AND system_type_id <> 189 AND name NOT IN (N'Estado');
SELECT @eTipos = N'CHECKSUM(' + STRING_AGG(QUOTENAME(name), N',') + N')' FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.TiposDocumento') AND system_type_id <> 189 AND name NOT IN (N'UltimoNumero');
SELECT @eProd  = N'CHECKSUM(' + STRING_AGG(QUOTENAME(name), N',') + N')' FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Productos') AND system_type_id <> 189 AND name NOT IN (N'StockActual');
SELECT @eCli   = N'CHECKSUM(' + STRING_AGG(QUOTENAME(name), N',') + N')' FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clientes') AND system_type_id <> 189 AND name NOT IN (N'PuntajeCliente', N'UltimaVentaFecha', N'CantidadComprasCliente', N'CreditosEnTermino', N'CreditosConAtraso');
INSERT #fp (Tabla, Expr) VALUES (N'Cajas', @eCajas);   -- Estado vuelve a Cerrada a proposito
INSERT #fp (Tabla, Expr) VALUES (N'TiposDocumento', @eTipos);   -- UltimoNumero solo cambia con ReiniciarNumeraciones=1
IF @DelProductos = 0 INSERT #fp (Tabla, Expr) VALUES (N'Productos', @eProd);   -- StockActual vuelve a 0
IF @DelClientes = 0 INSERT #fp (Tabla, Expr) VALUES (N'Clientes', @eCli);   -- scoring/estadisticas derivadas de ventas se reinician
IF @DelProveedores = 0 INSERT #fp (Tabla, Expr) VALUES (N'Proveedores', N'CHECKSUM(*)');
IF @DelAuditoria = 0 INSERT #fp (Tabla, Expr) VALUES (N'SeguridadEventosAuditoria', N'CHECKSUM(*)');
IF @DelProductos = 0 AND @DelClientes = 0 AND @DelProveedores = 0 INSERT #fp (Tabla, Expr) VALUES (N'ProveedorProductos', N'CHECKSUM(*)');

DECLARE @fpEj nvarchar(max) = N'';
SELECT @fpEj += N'UPDATE #fp SET NAntes = (SELECT COUNT_BIG(*) FROM dbo.' + QUOTENAME(Tabla) + N'), CkAntes = (SELECT CHECKSUM_AGG(' + Expr + N') FROM dbo.' + QUOTENAME(Tabla) + N') WHERE Tabla = N''' + Tabla + N''';' FROM #fp;
EXEC (@fpEj);

CREATE TABLE #res (Orden int IDENTITY(1,1) PRIMARY KEY, Tabla sysname, Grupo nvarchar(20), Eliminadas bigint, Restantes bigint NULL);
CREATE TABLE #antes (Clave nvarchar(60) PRIMARY KEY, Valor decimal(19,2));
INSERT #antes VALUES (N'Cajas abiertas (turnos)', (SELECT COUNT(*) FROM dbo.AperturasCaja WHERE Cerrada = 0)),
                     (N'Cajas marcadas Abierta', (SELECT COUNT(*) FROM dbo.Cajas WHERE Estado <> 0)),
                     (N'Stock total en productos', (SELECT ISNULL(SUM(StockActual),0) FROM dbo.Productos)),
                     (N'Creditos', (SELECT COUNT(*) FROM dbo.Creditos));

-- Toda tabla debe estar clasificada (a vaciar o a conservar); una tabla nueva u omitida aborta la limpieza.
IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND name NOT IN (N'AcuerdoPagoCuotasCubiertas',N'AcuerdoPagoImputaciones',N'AlertasMora',N'AlertasStock',N'CambioPrecioDetalles',N'CambioPrecioEventos',N'Cheques',N'CierresCaja',N'ClientesCreditoConfiguraciones',N'ClientesPuntajeHistorial',N'ContratosVentaCredito',N'CotizacionDetalles',N'CotizacionPagosSimulados',N'CuotasAcuerdo',N'DatosCheque',N'DatosTarjeta',N'DevolucionDetalles',N'DocumentosCliente',N'DocumentosGenerados',N'Facturas',N'Garantias',N'HistorialContactos',N'LogsMora',N'MercadoLibreClaims',N'MercadoLibreListingVariations',N'MercadoLibreMessages',N'MercadoLibreOrderItems',N'MercadoLibreOrders',N'MercadoLibrePriceBatchItems',N'MercadoLibrePublicacionBorradores',N'MercadoLibreQuestions',N'MercadoLibreSyncLogs',N'MercadoLibreWebhookEvents',N'MovimientosStock',N'NotaRapidaItems',N'NotasCreditoUso',N'NotasRapidas',N'Notificaciones',N'NumeracionVentaContadores',N'OrdenCompraDetalle',N'OrdenesCompra',N'PagosCuota',N'PreciosHistoricos',N'PriceChangeItems',N'ProductoCreditoPersonalCuotas',N'ProductoCreditoRestricciones',N'ProductosCaracteristicas',N'ProductosPrecios',N'ProductoUnidadMovimientos',N'ProveedorCategorias',N'ProveedorMarcas',N'ProveedorProductos',N'PunitoriosAplicados',N'RMAs',N'SeguridadEventosAuditoria',N'SolicitudesAutorizacion',N'TicketAdjuntos',N'TicketChecklistItems',N'Tickets',N'VentaDetalles',N'VentaEnvio',N'AcuerdosPago',N'AlertasCobranza',N'Cotizaciones',N'Cuotas',N'MercadoLibreListings',N'MercadoLibrePriceBatches',N'MovimientosCaja',N'NotasCredito',N'PriceChangeBatches',N'ProductoCondicionPagoPlanes',N'ProductoUnidades',N'Proveedores',N'Devoluciones',N'ProductoCondicionesPagoTarjeta',N'Ventas',N'AperturasCaja',N'Creditos',N'Garantes',N'ProductoCondicionesPago',N'Productos',N'Clientes')
           AND name NOT IN (N'__EFMigrationsHistory',N'AccionesModulo',N'AlicuotasIVA',N'AspNetRoleClaims',N'AspNetRoles',N'AspNetUserClaims',N'AspNetUserLogins',
 N'AspNetUserRoles',N'AspNetUsers',N'AspNetUserTokens',N'CajaVendedores',N'Cajas',N'Categorias',N'Marcas',
 N'ConfiguracionCreditoMontosPorPuntaje',N'ConfiguracionCreditoPersonalCuotas',N'ConfiguracionCreditoPersonalCuotasSinRecargo',
 N'ConfiguracionesActualizacionDatosCliente',N'ConfiguracionesCredito',N'ConfiguracionesMora',N'ConfiguracionesPago',
 N'ConfiguracionesPunitorio',N'ConfiguracionesRentabilidad',N'ConfiguracionesScoringCliente',N'ConfiguracionesTarjeta',
 N'ConfiguracionPagoPlanes',N'EmpresasConfiguracion',N'ListasPrecios',N'ModulosSistema',N'PaquetesDocumentales',
 N'PaquetesDocumentalesItems',N'PerfilesCredito',N'PlantillasContratoCredito',N'PlantillasDocumento',N'PlantillasDocumentoVersion',
 N'PrefijosNumeracion',N'PuntajeCreditoLimites',N'ReglasDocumento',N'RolesMetadata',N'RolPermisos',N'ServiciosVentaPrecios',
 N'Sucursales',N'TerminosCondicionesAceptaciones',N'TiposDocumento',N'UmbralesAutorizacion',
 N'MercadoLibreAccounts',N'MercadoLibreCategories',N'MercadoLibreCategoryAttributes',N'MercadoLibreCategorySyncStates',N'MercadoLibreConfiguraciones',N'PlantillasNotificacionMora'))
BEGIN
    SELECT name AS TablaSinClasificar FROM sys.tables WHERE is_ms_shipped = 0 AND name NOT IN (N'AcuerdoPagoCuotasCubiertas',N'AcuerdoPagoImputaciones',N'AlertasMora',N'AlertasStock',N'CambioPrecioDetalles',N'CambioPrecioEventos',N'Cheques',N'CierresCaja',N'ClientesCreditoConfiguraciones',N'ClientesPuntajeHistorial',N'ContratosVentaCredito',N'CotizacionDetalles',N'CotizacionPagosSimulados',N'CuotasAcuerdo',N'DatosCheque',N'DatosTarjeta',N'DevolucionDetalles',N'DocumentosCliente',N'DocumentosGenerados',N'Facturas',N'Garantias',N'HistorialContactos',N'LogsMora',N'MercadoLibreClaims',N'MercadoLibreListingVariations',N'MercadoLibreMessages',N'MercadoLibreOrderItems',N'MercadoLibreOrders',N'MercadoLibrePriceBatchItems',N'MercadoLibrePublicacionBorradores',N'MercadoLibreQuestions',N'MercadoLibreSyncLogs',N'MercadoLibreWebhookEvents',N'MovimientosStock',N'NotaRapidaItems',N'NotasCreditoUso',N'NotasRapidas',N'Notificaciones',N'NumeracionVentaContadores',N'OrdenCompraDetalle',N'OrdenesCompra',N'PagosCuota',N'PreciosHistoricos',N'PriceChangeItems',N'ProductoCreditoPersonalCuotas',N'ProductoCreditoRestricciones',N'ProductosCaracteristicas',N'ProductosPrecios',N'ProductoUnidadMovimientos',N'ProveedorCategorias',N'ProveedorMarcas',N'ProveedorProductos',N'PunitoriosAplicados',N'RMAs',N'SeguridadEventosAuditoria',N'SolicitudesAutorizacion',N'TicketAdjuntos',N'TicketChecklistItems',N'Tickets',N'VentaDetalles',N'VentaEnvio',N'AcuerdosPago',N'AlertasCobranza',N'Cotizaciones',N'Cuotas',N'MercadoLibreListings',N'MercadoLibrePriceBatches',N'MovimientosCaja',N'NotasCredito',N'PriceChangeBatches',N'ProductoCondicionPagoPlanes',N'ProductoUnidades',N'Proveedores',N'Devoluciones',N'ProductoCondicionesPagoTarjeta',N'Ventas',N'AperturasCaja',N'Creditos',N'Garantes',N'ProductoCondicionesPago',N'Productos',N'Clientes') AND name NOT IN (SELECT Tabla FROM #fp)
       AND name NOT IN (N'MercadoLibreAccounts',N'MercadoLibreCategories',N'MercadoLibreCategoryAttributes',N'MercadoLibreCategorySyncStates',N'MercadoLibreConfiguraciones',N'PlantillasNotificacionMora');
    THROW 50007, N'Hay tablas sin clasificar en el script (tabla nueva u omitida). Revisar antes de limpiar.', 1;
END

BEGIN TRANSACTION;

-- ---- Aristas que forman ciclos: se cortan poniendo en NULL columnas nullables de filas que se eliminan ----------------
UPDATE dbo.Ventas SET NotaCreditoId = NULL WHERE NotaCreditoId IS NOT NULL;                 -- Ventas -> NotasCredito -> Devoluciones -> Ventas
UPDATE dbo.ProductoUnidades SET VentaDetalleId = NULL WHERE VentaDetalleId IS NOT NULL;      -- ProductoUnidades <-> VentaDetalles
IF @DelClientes = 1 UPDATE dbo.Clientes SET GaranteId = NULL WHERE GaranteId IS NOT NULL;     -- Clientes <-> Garantes

-- ---- Borrado en orden de dependencias (hijos antes que padres) ---------------------------------------------------------
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AcuerdoPagoCuotasCubiertas]; DELETE FROM [dbo].[AcuerdoPagoCuotasCubiertas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AcuerdoPagoCuotasCubiertas', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AcuerdoPagoImputaciones]; DELETE FROM [dbo].[AcuerdoPagoImputaciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AcuerdoPagoImputaciones', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AlertasMora]; DELETE FROM [dbo].[AlertasMora]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AlertasMora', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AlertasStock]; DELETE FROM [dbo].[AlertasStock]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AlertasStock', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CambioPrecioDetalles]; DELETE FROM [dbo].[CambioPrecioDetalles]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CambioPrecioDetalles', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CambioPrecioEventos]; DELETE FROM [dbo].[CambioPrecioEventos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CambioPrecioEventos', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Cheques]; DELETE FROM [dbo].[Cheques]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Cheques', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CierresCaja]; DELETE FROM [dbo].[CierresCaja]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CierresCaja', N'Operativo', @n); END
IF @DelClientes = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ClientesCreditoConfiguraciones]; DELETE FROM [dbo].[ClientesCreditoConfiguraciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ClientesCreditoConfiguraciones', N'Clientes', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ClientesPuntajeHistorial]; DELETE FROM [dbo].[ClientesPuntajeHistorial]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ClientesPuntajeHistorial', N'Clientes', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ContratosVentaCredito]; DELETE FROM [dbo].[ContratosVentaCredito]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ContratosVentaCredito', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CotizacionDetalles]; DELETE FROM [dbo].[CotizacionDetalles]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CotizacionDetalles', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CotizacionPagosSimulados]; DELETE FROM [dbo].[CotizacionPagosSimulados]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CotizacionPagosSimulados', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[CuotasAcuerdo]; DELETE FROM [dbo].[CuotasAcuerdo]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'CuotasAcuerdo', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[DatosCheque]; DELETE FROM [dbo].[DatosCheque]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'DatosCheque', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[DatosTarjeta]; DELETE FROM [dbo].[DatosTarjeta]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'DatosTarjeta', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[DevolucionDetalles]; DELETE FROM [dbo].[DevolucionDetalles]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'DevolucionDetalles', N'Operativo', @n); END
IF @DelClientes = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[DocumentosCliente]; DELETE FROM [dbo].[DocumentosCliente]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'DocumentosCliente', N'Clientes', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[DocumentosGenerados]; DELETE FROM [dbo].[DocumentosGenerados]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'DocumentosGenerados', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Facturas]; DELETE FROM [dbo].[Facturas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Facturas', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Garantias]; DELETE FROM [dbo].[Garantias]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Garantias', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[HistorialContactos]; DELETE FROM [dbo].[HistorialContactos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'HistorialContactos', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[LogsMora]; DELETE FROM [dbo].[LogsMora]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'LogsMora', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreClaims]; DELETE FROM [dbo].[MercadoLibreClaims]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreClaims', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreListingVariations]; DELETE FROM [dbo].[MercadoLibreListingVariations]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreListingVariations', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreMessages]; DELETE FROM [dbo].[MercadoLibreMessages]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreMessages', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreOrderItems]; DELETE FROM [dbo].[MercadoLibreOrderItems]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreOrderItems', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreOrders]; DELETE FROM [dbo].[MercadoLibreOrders]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreOrders', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibrePriceBatchItems]; DELETE FROM [dbo].[MercadoLibrePriceBatchItems]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibrePriceBatchItems', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibrePublicacionBorradores]; DELETE FROM [dbo].[MercadoLibrePublicacionBorradores]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibrePublicacionBorradores', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreQuestions]; DELETE FROM [dbo].[MercadoLibreQuestions]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreQuestions', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreSyncLogs]; DELETE FROM [dbo].[MercadoLibreSyncLogs]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreSyncLogs', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreWebhookEvents]; DELETE FROM [dbo].[MercadoLibreWebhookEvents]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreWebhookEvents', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MovimientosStock]; DELETE FROM [dbo].[MovimientosStock]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MovimientosStock', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[NotaRapidaItems]; DELETE FROM [dbo].[NotaRapidaItems]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'NotaRapidaItems', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[NotasCreditoUso]; DELETE FROM [dbo].[NotasCreditoUso]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'NotasCreditoUso', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[NotasRapidas]; DELETE FROM [dbo].[NotasRapidas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'NotasRapidas', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Notificaciones]; DELETE FROM [dbo].[Notificaciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Notificaciones', N'Operativo', @n); END
IF @Reiniciar = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[NumeracionVentaContadores]; DELETE FROM [dbo].[NumeracionVentaContadores]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'NumeracionVentaContadores', N'Numeracion', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[OrdenCompraDetalle]; DELETE FROM [dbo].[OrdenCompraDetalle]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'OrdenCompraDetalle', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[OrdenesCompra]; DELETE FROM [dbo].[OrdenesCompra]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'OrdenesCompra', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[PagosCuota]; DELETE FROM [dbo].[PagosCuota]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'PagosCuota', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[PreciosHistoricos]; DELETE FROM [dbo].[PreciosHistoricos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'PreciosHistoricos', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[PriceChangeItems]; DELETE FROM [dbo].[PriceChangeItems]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'PriceChangeItems', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoCreditoPersonalCuotas]; DELETE FROM [dbo].[ProductoCreditoPersonalCuotas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoCreditoPersonalCuotas', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoCreditoRestricciones]; DELETE FROM [dbo].[ProductoCreditoRestricciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoCreditoRestricciones', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductosCaracteristicas]; DELETE FROM [dbo].[ProductosCaracteristicas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductosCaracteristicas', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductosPrecios]; DELETE FROM [dbo].[ProductosPrecios]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductosPrecios', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoUnidadMovimientos]; DELETE FROM [dbo].[ProductoUnidadMovimientos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoUnidadMovimientos', N'Operativo', @n); END
IF @DelProveedores = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProveedorCategorias]; DELETE FROM [dbo].[ProveedorCategorias]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProveedorCategorias', N'Proveedores', @n); END
IF @DelProveedores = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProveedorMarcas]; DELETE FROM [dbo].[ProveedorMarcas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProveedorMarcas', N'Proveedores', @n); END
IF (@DelProveedores = 1 OR @DelProductos = 1) BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProveedorProductos]; DELETE FROM [dbo].[ProveedorProductos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProveedorProductos', N'Proveedores', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[PunitoriosAplicados]; DELETE FROM [dbo].[PunitoriosAplicados]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'PunitoriosAplicados', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[RMAs]; DELETE FROM [dbo].[RMAs]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'RMAs', N'Operativo', @n); END
IF @DelAuditoria = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[SeguridadEventosAuditoria]; DELETE FROM [dbo].[SeguridadEventosAuditoria]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'SeguridadEventosAuditoria', N'Auditoria', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[SolicitudesAutorizacion]; DELETE FROM [dbo].[SolicitudesAutorizacion]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'SolicitudesAutorizacion', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[TicketAdjuntos]; DELETE FROM [dbo].[TicketAdjuntos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'TicketAdjuntos', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[TicketChecklistItems]; DELETE FROM [dbo].[TicketChecklistItems]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'TicketChecklistItems', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Tickets]; DELETE FROM [dbo].[Tickets]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Tickets', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[VentaDetalles]; DELETE FROM [dbo].[VentaDetalles]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'VentaDetalles', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[VentaEnvio]; DELETE FROM [dbo].[VentaEnvio]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'VentaEnvio', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AcuerdosPago]; DELETE FROM [dbo].[AcuerdosPago]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AcuerdosPago', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AlertasCobranza]; DELETE FROM [dbo].[AlertasCobranza]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AlertasCobranza', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Cotizaciones]; DELETE FROM [dbo].[Cotizaciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Cotizaciones', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Cuotas]; DELETE FROM [dbo].[Cuotas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Cuotas', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibreListings]; DELETE FROM [dbo].[MercadoLibreListings]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibreListings', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MercadoLibrePriceBatches]; DELETE FROM [dbo].[MercadoLibrePriceBatches]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MercadoLibrePriceBatches', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[MovimientosCaja]; DELETE FROM [dbo].[MovimientosCaja]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'MovimientosCaja', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[NotasCredito]; DELETE FROM [dbo].[NotasCredito]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'NotasCredito', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[PriceChangeBatches]; DELETE FROM [dbo].[PriceChangeBatches]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'PriceChangeBatches', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoCondicionPagoPlanes]; DELETE FROM [dbo].[ProductoCondicionPagoPlanes]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoCondicionPagoPlanes', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoUnidades]; DELETE FROM [dbo].[ProductoUnidades]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoUnidades', N'Operativo', @n); END
IF @DelProveedores = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Proveedores]; DELETE FROM [dbo].[Proveedores]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Proveedores', N'Proveedores', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Devoluciones]; DELETE FROM [dbo].[Devoluciones]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Devoluciones', N'Operativo', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoCondicionesPagoTarjeta]; DELETE FROM [dbo].[ProductoCondicionesPagoTarjeta]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoCondicionesPagoTarjeta', N'Productos', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Ventas]; DELETE FROM [dbo].[Ventas]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Ventas', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[AperturasCaja]; DELETE FROM [dbo].[AperturasCaja]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'AperturasCaja', N'Operativo', @n); END
IF 1=1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Creditos]; DELETE FROM [dbo].[Creditos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Creditos', N'Operativo', @n); END
IF @DelClientes = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Garantes]; DELETE FROM [dbo].[Garantes]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Garantes', N'Clientes', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[ProductoCondicionesPago]; DELETE FROM [dbo].[ProductoCondicionesPago]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'ProductoCondicionesPago', N'Productos', @n); END
IF @DelProductos = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Productos]; DELETE FROM [dbo].[Productos]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Productos', N'Productos', @n); END
IF @DelClientes = 1 BEGIN SELECT @n = COUNT_BIG(*) FROM [dbo].[Clientes]; DELETE FROM [dbo].[Clientes]; INSERT #res (Tabla, Grupo, Eliminadas) VALUES (N'Clientes', N'Clientes', @n); END

-- ---- Clientes conservados: su scoring y estadisticas se derivaban de ventas/creditos que ya no existen ----------------------
IF @DelClientes = 0
    UPDATE dbo.Clientes SET PuntajeCliente = 0, UltimaVentaFecha = NULL, CantidadComprasCliente = 0, CreditosEnTermino = 0, CreditosConAtraso = 0
     WHERE PuntajeCliente <> 0 OR UltimaVentaFecha IS NOT NULL OR CantidadComprasCliente <> 0 OR CreditosEnTermino <> 0 OR CreditosConAtraso <> 0;

-- ---- Cajas: la columna Cajas.Estado (Abierta/Cerrada) es independiente de AperturasCaja; sin turnos, todas quedan Cerradas ----
UPDATE dbo.Cajas SET Estado = 0 WHERE Estado <> 0;

-- ---- Stock: si los productos se conservan, su stock ficticio vuelve a 0 ---------------------------------------------------
IF @DelProductos = 0 UPDATE dbo.Productos SET StockActual = 0 WHERE StockActual <> 0;

-- ---- Numeraciones almacenadas (solo si se pidio) --------------------------------------------------------------------------
IF @Reiniciar = 1
BEGIN
    UPDATE dbo.TiposDocumento SET UltimoNumero = 0 WHERE UltimoNumero <> 0;
    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.Tabla FROM #res r JOIN sys.tables t ON t.name = r.Tabla JOIN sys.identity_columns ic ON ic.object_id = t.object_id
         WHERE ic.last_value IS NOT NULL;   -- identities nunca usados ya empiezan en 1; RESEED 0 los dejaria en 0
    OPEN cur; FETCH NEXT FROM cur INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'DBCC CHECKIDENT (N''dbo.' + @t + N''', RESEED, 0) WITH NO_INFOMSGS;';
        EXEC (@sql);
        FETCH NEXT FROM cur INTO @t;
    END
    CLOSE cur; DEALLOCATE cur;
END

-- ---- Verificaciones (cualquier fallo revierte todo) -------------------------------------------------------------------------
DECLARE @chk nvarchar(max) = N'';
SELECT @chk += N'UPDATE #res SET Restantes = (SELECT COUNT_BIG(*) FROM dbo.' + QUOTENAME(Tabla) + N') WHERE Tabla = N''' + Tabla + N''';' FROM #res;
EXEC (@chk);
IF EXISTS (SELECT 1 FROM #res WHERE Restantes <> 0)
BEGIN SELECT * FROM #res WHERE Restantes <> 0; THROW 50010, N'Quedaron filas en tablas que debian vaciarse.', 1; END

DECLARE @fpDe nvarchar(max) = N'';
SELECT @fpDe += N'UPDATE #fp SET NDespues = (SELECT COUNT_BIG(*) FROM dbo.' + QUOTENAME(Tabla) + N'), CkDespues = (SELECT CHECKSUM_AGG(' + Expr + N') FROM dbo.' + QUOTENAME(Tabla) + N') WHERE Tabla = N''' + Tabla + N''';' FROM #fp;
EXEC (@fpDe);
IF EXISTS (SELECT 1 FROM #fp WHERE NAntes <> NDespues OR ISNULL(CkAntes,0) <> ISNULL(CkDespues,0))
BEGIN SELECT * FROM #fp WHERE NAntes <> NDespues OR ISNULL(CkAntes,0) <> ISNULL(CkDespues,0); THROW 50011, N'Una tabla que debia quedar intacta cambio.', 1; END

CREATE TABLE #viol (T nvarchar(300), C nvarchar(300), W nvarchar(1000));
INSERT #viol EXEC (N'DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, NO_INFOMSGS');
IF EXISTS (SELECT 1 FROM #viol) BEGIN SELECT * FROM #viol; THROW 50012, N'Hay filas que violan restricciones (huerfanos).', 1; END

IF EXISTS (SELECT 1 FROM dbo.AperturasCaja WHERE Cerrada = 0) OR EXISTS (SELECT 1 FROM dbo.Cajas WHERE Estado <> 0) THROW 50013, N'Quedo una caja abierta.', 1;
IF @DelProductos = 0 AND EXISTS (SELECT 1 FROM dbo.Productos WHERE StockActual <> 0) THROW 50014, N'Quedo stock en productos.', 1;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 135 THROW 50015, N'El historial de migraciones cambio.', 1;

-- ---- Informe ---------------------------------------------------------------------------------------------------------------
SELECT Grupo, Tabla, Eliminadas FROM #res WHERE Eliminadas > 0 ORDER BY Orden;
SELECT Tabla, NAntes AS FilasAntes, NDespues AS FilasDespues, CASE WHEN NAntes = NDespues AND ISNULL(CkAntes,0) = ISNULL(CkDespues,0) THEN N'IGUAL' ELSE N'CAMBIO' END AS Huella FROM #fp ORDER BY Tabla;
SELECT a.Clave, a.Valor AS Antes,
       CASE a.Clave WHEN N'Cajas abiertas (turnos)' THEN (SELECT COUNT(*) FROM dbo.AperturasCaja WHERE Cerrada = 0)
                    WHEN N'Cajas marcadas Abierta' THEN (SELECT COUNT(*) FROM dbo.Cajas WHERE Estado <> 0)
                    WHEN N'Stock total en productos' THEN (SELECT ISNULL(SUM(StockActual),0) FROM dbo.Productos)
                    WHEN N'Creditos' THEN (SELECT COUNT(*) FROM dbo.Creditos) ELSE 0 END AS Despues
  FROM #antes a;

-- Reverificar exclusividad justo antes de confirmar (una app que se conecto durante la limpieza invalida el resultado).
IF EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID AND is_user_process = 1)
    THROW 50004, N'Aparecio otra conexion durante la limpieza. Se revierte todo.', 1;

IF @Ejecutar = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT N'LIMPIEZA CONFIRMADA (COMMIT) en ' + DB_NAME();
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'ENSAYO: todo revertido (ROLLBACK). No se cambio ningun dato en ' + DB_NAME();
END




/*
  H-01 - Migracion 20261106072023_H01NumeroOrigenVenta: Ventas.NumeroOrigen (numero «COT-...» original de la venta renumerada a «VTA-...»).

  Agrega EXACTAMENTE una columna y registra la migracion en __EFMigrationsHistory:
    Ventas.NumeroOrigen   nvarchar(20) NULL
  No modifica, borra ni recalcula ningun valor: las ventas existentes quedan con NumeroOrigen NULL (no se renumeran).

  Guardas (el script ABORTA sin cambiar nada si algo no coincide):
    - la base de destino debe llamarse TheBuryProjectDb;
    - __EFMigrationsHistory debe contener EXACTAMENTE las 135 migraciones anteriores (hasta 20261105142232_H12CostoServiciosVenta):
      ni faltantes, ni extras, ni posteriores desconocidas;
    - la columna no puede existir todavia. Si la migracion ya esta registrada solo se acepta el estado «la columna existe con
      tipo, longitud y nulabilidad exactos» (no hace nada); cualquier otro estado intermedio aborta;
    - la tabla Ventas debe existir.
  Es transaccional (XACT_ABORT), serializado (bloqueo de aplicacion) y con ensayo (DryRun=1 revierte). Tras aplicar vuelve a
  validar la definicion dentro de la transaccion y revierte si no coincide.

  NO SE APLICA A TheBuryProjectDb SIN AUTORIZACION EXPLICITA DEL PROPIETARIO. Procedimiento: docs/h01-aplicacion-segura.md.

  Uso (sqlcmd; -v DryRun y -v BaseEsperada son obligatorios):
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i h01-migracion-numero-origen-venta.sql -v DryRun=1 -v BaseEsperada=TheBuryProjectDb   (ensayo)
    sqlcmd -S <servidor> -d TheBuryProjectDb -I -b -f 65001 -i h01-migracion-numero-origen-venta.sql -v DryRun=0 -v BaseEsperada=TheBuryProjectDb   (aplica)
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = $(DryRun);
DECLARE @Migracion nvarchar(150) = N'20261106072023_H01NumeroOrigenVenta';
DECLARE @BaseEsperada sysname = N'$(BaseEsperada)';

BEGIN TRANSACTION;

DECLARE @Lock int;
EXEC @Lock = sp_getapplock @Resource = N'h01-migracion-numero-origen-venta', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @Lock < 0
    THROW 51040, 'No se pudo obtener el bloqueo de aplicacion: hay otra ejecucion en curso.', 1;

-- 1) Base de destino.
IF DB_NAME() <> @BaseEsperada
    THROW 51041, 'Base de destino incorrecta. No se modifica nada.', 1;

-- 2) Historial de migraciones.
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51042, 'No existe __EFMigrationsHistory: no se modifica nada.', 1;

DECLARE @Esperadas TABLE (MigrationId nvarchar(150) NOT NULL PRIMARY KEY);
INSERT INTO @Esperadas (MigrationId) VALUES
    (N'20251227061658_InitialCreate'),
    (N'20251228083000_AddDefaultsMarcaProducto'),
    (N'20251229042000_AddUniqueVigenteIndexProductosPrecios'),
    (N'20251230062944_AddConyugeToCliente'),
    (N'20251230145224_AddMoraModuleEntities'),
    (N'20251230200000_AddClienteAptitudCredito'),
    (N'20251230210000_AddVentaValidacionUnificada'),
    (N'20251231014751_AddVentaDatosCreditoPersonallJson'),
    (N'20260103055614_AddFechaConfiguracionCreditoToVenta'),
    (N'20260104053100_AddSoftDeleteFilterToFacturaNumeroIndex'),
    (N'20260104065515_AddNivelRiesgoToCliente'),
    (N'20260104071646_AddSubcategoriaSubmarcaToProducto'),
    (N'20260105074823_AddBatchPadreIdToReversion'),
    (N'20260106043601_FixDecimalPrecision'),
    (N'20260106160520_AddCambioPrecioEventos'),
    (N'20260106215438_AddVentaCajaVendedor'),
    (N'20260109211408_AddTasaInteresMensualCreditoPersonalToConfiguracionPago'),
    (N'20260208225726_AddAlertaMora'),
    (N'20260208232407_AddConfiguracionCreditoPersonalizadaCliente'),
    (N'20260208233717_AddPerfilesCreditoYDefaultsGlobales'),
    (N'20260208234350_AddPerfilCreditoPreferidoToCliente'),
    (N'20260209001536_AddAuditabilidadMetodoCalculoCredito'),
    (N'20260209004007_AddTasaInteresAplicadaAudit'),
    (N'20260209193239_AddSoftDeleteToUsers'),
    (N'20260209235308_AddPorcentajeIVAToProducto'),
    (N'20260213061047_AddProductoCaracteristicasEtapa2'),
    (N'20260214055512_AddPuntajeCreditoLimites'),
    (N'20260217200247_AddClienteCreditoConfiguracionEtapa2LimiteEfectivo'),
    (N'20260313073317_AddCuilCuitAndBcraFields'),
    (N'20260315005618_AddUsuarioNombreSucursalAcceso'),
    (N'20260315010817_SplitNombreApellidoTelefono'),
    (N'20260315164453_AddRoleMetadataForSecurityModule'),
    (N'20260315170342_AddSeguridadAuditoriaEvents'),
    (N'20260316171645_AddRowVersionToApplicationUser'),
    (N'20260316174143_AddUserSucursalRelationAndPermissionAliases'),
    (N'20260317034424_AddQuickDevolucionResolutionFlow'),
    (N'20260328072035_MakeEvaluacionCreditoCreditoIdNullable'),
    (N'20260414182706_AddTicketModule'),
    (N'20260426214719_AddContratosVentaCredito'),
    (N'20260427015225_AddSellerCommissionFields'),
    (N'20260429000000_AddScoringThresholdsToConfiguracionCredito'),
    (N'20260429000001_AddScoringBandsToConfiguracionCredito'),
    (N'20260429000002_AddSemaforoFinancieroThresholdsToConfiguracionCredito'),
    (N'20260429000003_AddConfiguracionRentabilidad'),
    (N'20260429212459_AddAlicuotaIVA'),
    (N'20260429224713_AddVentaDetalleIvaSnapshots'),
    (N'20260430020647_AddVentaDetalleDescuentoGeneralProrrateado'),
    (N'20260430030000_AddVentaDetalleCostoSnapshots'),
    (N'20260430033000_AddMovimientoStockCostoSnapshots'),
    (N'20260502073010_AddProductoMaxCuotasSinInteres'),
    (N'20260502082719_AddDatosTarjetaMaxCuotasSnapshot'),
    (N'20260506072108_AddMovimientoCajaPagoEstructurado'),
    (N'20260506202529_AddProductoCondicionesPago'),
    (N'20260507000000_AddCreditoRestriccionCuotasSnap'),
    (N'20260507232606_AddProductoCondicionPagoPlan'),
    (N'20260508012116_AddDatosTarjetaProductoCondicionPagoPlanId'),
    (N'20260508045350_AddDatosTarjetaAjustePlanAplicado'),
    (N'20260508072305_AddVentaDetallePagoPorItem'),
    (N'20260512211213_AddConfiguracionPagoPlan'),
    (N'20260513011030_AddDatosTarjetaPagoGlobalSnapshot'),
    (N'20260513154151_AddProductoCreditoRestriccion'),
    (N'20260513170000_BackfillProductoCreditoRestriccionesFromCondicionesPago'),
    (N'20260514034159_AddProductoUnidades'),
    (N'20260514051004_AddVentaDetalleProductoUnidad'),
    (N'20260515152426_AddEstadoAcreditacionMovimientoCaja'),
    (N'20260515165759_AddProductoUnidadToDevolucionDetalle'),
    (N'20260515194116_AddCotizaciones'),
    (N'20260515234236_AddCotizacionOrigenToVenta'),
    (N'20260516174350_AddCotizacionMotivoCancelacion'),
    (N'20260603182000_AddCreditoMontoPorPuntajeConfig'),
    (N'20260615012502_AddMercadoLibreModule'),
    (N'20260615174953_AddProductoEsDestacado'),
    (N'20260616164527_AddBorradorCategoriaSnapshot'),
    (N'20260617020659_AddBorradorImagenes'),
    (N'20260617060432_AddMercadoLibreCategoryCatalog'),
    (N'20260617063221_AddBorradorAtributosCompletados'),
    (N'20260619140000_AddAuditColumnsToConfiguracionesCredito'),
    (N'20260622211826_AddPuntajeClienteScoring'),
    (N'20260622213600_AddConfiguracionScoringCliente'),
    (N'20260623153000_AddNivelCreditoManualCliente'),
    (N'20260625042903_AddCajaVendedor'),
    (N'20260630120000_AddCantidadComprasClienteScoring'),
    (N'20260701010417_AlterPuntajeClienteDefaultCero'),
    (N'20260701043907_AlterCupoPorPuntajeInterno'),
    (N'20260701155943_AddGaranteFechaBajaMotivo'),
    (N'20260703161937_AgregarUltimoExitoBcraCliente'),
    (N'20260703201835_EliminarEvaluacionesCreditoLegacy'),
    (N'20260711050449_AgregarIvaCompraPorLineaYCostosCompraProducto'),
    (N'20260713221407_AddConfiguracionCreditoPersonalCuota'),
    (N'20260719075831_AddTerminosCondicionesAceptacion'),
    (N'20260719120000_AddDesafioALosDiosesAFlags'),
    (N'20260720100000_AddProductoCreditoPersonalCuota'),
    (N'20260720110000_AddRecargoMedioPagoCobros'),
    (N'20260722120000_MakeCreditoPersonalCuotaTasaNullable'),
    (N'20260723213000_VentaDetalleSnapshotProducto'),
    (N'20260723220000_CreditoCobroPrimeraCuotaDecision'),
    (N'20260730233422_ML8_CotizacionAnticipoYCreditoAnticipoPreseleccionado'),
    (N'20260731211649_AddPagoCuotaLedger'),
    (N'20260801020228_AddConfiguracionPunitorio'),
    (N'20260801061625_AddPunitorioAplicado'),
    (N'20260801175306_AddPunitorioAplicadoIdToPagoCuota'),
    (N'20260807212501_NormalizarPlanCreditoPersonalCuotaNullHistorico'),
    (N'20260808155800_AddConfiguracionCreditoPersonalCuotaSinRecargo'),
    (N'20260913155532_AddVentaEnvio'),
    (N'20260916162948_EliminarCaeFactura'),
    (N'20260920175040_AddCotizacionCostoEnvio'),
    (N'20260926194022_AddServiciosVentaEnvioArmado'),
    (N'20260926211626_AddServiciosCotizacion'),
    (N'20260926225923_AddVentaContactoLibre'),
    (N'20260930053635_AddCotizacionDetalleProductoUnidad'),
    (N'20260930151318_AddConfiguracionPagoAplicaAjusteCobroCuotas'),
    (N'20261001160221_AddProductoPorcentajeIVACompra'),
    (N'20261001201632_VentaDetalleEnviarADomicilio'),
    (N'20261002185713_AddNotasRapidas'),
    (N'20261002191443_NotasRapidasTipoEItems'),
    (N'20261004053859_AddMotorDocumental'),
    (N'20261004065604_AddEmpresaConfiguracion'),
    (N'20261004074614_EmpresaPresentacionDocumentos'),
    (N'20261006054105_AddActualizacionDatosCliente'),
    (N'20261006155700_DropVentaCreditoCuotasLegacy'),
    (N'20261010181838_DropColumnasCreditoSinUso'),
    (N'20261014175359_QaPrdP1MovimientoStockRequestId'),
    (N'20261017043435_FinIdemP0MovimientoCajaIdempotencyKey'),
    (N'20261025025053_FinS018VentaIdempotencyKey'),
    (N'20261026061459_NumeracionVentaContador'),
    (N'20261026225333_PrefijosNumeracion'),
    (N'20261027004351_AjusteMedioPagoVentaYAcuerdoVigenteUnico'),
    (N'20261027014633_AcuerdoPagoMontoEntregaPagada'),
    (N'20261027032606_AcuerdoPagoImputaciones'),
    (N'20261028013804_AcuerdoPagoDeudaCongelada'),
    (N'20261028021207_AcuerdoPagoIncumplimientoAutomatico'),
    (N'20261028172934_BorradorCreditoVencimientoConfig'),
    (N'20261029012048_H4E2NotaCreditoCanje'),
    (N'20261030030301_H10ChequesRecibidos'),
    (N'20261105142232_H12CostoServiciosVenta');

IF OBJECT_ID(N'dbo.Ventas', N'U') IS NULL
    THROW 51043, 'Falta la tabla Ventas: no se modifica nada.', 1;

DECLARE @Registrada bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @Migracion) THEN 1 ELSE 0 END;

DECLARE @Faltantes int = (SELECT COUNT(*) FROM @Esperadas e WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory h WHERE h.MigrationId = e.MigrationId));
DECLARE @Extras int = (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory h WHERE h.MigrationId <> @Migracion AND NOT EXISTS (SELECT 1 FROM @Esperadas e WHERE e.MigrationId = h.MigrationId));
IF @Faltantes <> 0 OR @Extras <> 0
    THROW 51044, 'El historial de migraciones no coincide con el esperado. No se modifica nada.', 1;

DECLARE @Existe bit = CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Ventas') AND name = N'NumeroOrigen') THEN 1 ELSE 0 END;

DECLARE @Exacta bit = CASE WHEN EXISTS (
    SELECT 1 FROM sys.columns col JOIN sys.types ty ON ty.user_type_id = col.user_type_id
    WHERE col.object_id = OBJECT_ID(N'dbo.Ventas') AND col.name = N'NumeroOrigen'
      AND ty.name = N'nvarchar' AND col.max_length = 40 AND col.is_nullable = 1 AND col.is_computed = 0) THEN 1 ELSE 0 END;

IF @Registrada = 1
BEGIN
    IF @Existe = 1 AND @Exacta = 1
    BEGIN
        PRINT 'La migracion ya esta registrada y la columna existe con la definicion exacta: no se hace nada.';
        ROLLBACK TRANSACTION;
        RETURN;
    END;
    THROW 51045, 'Discrepancia: la migracion figura aplicada pero la columna falta o no coincide. No se modifica nada.', 1;
END;

IF @Existe = 1
    THROW 51046, 'Discrepancia: la columna existe sin que la migracion este registrada. No se modifica nada.', 1;

-- 3) Aplicacion (solo la columna y el registro).
ALTER TABLE dbo.Ventas ADD NumeroOrigen nvarchar(20) NULL;

INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (@Migracion, N'10.0.13');

-- 4) Verificacion final dentro de la transaccion.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns col JOIN sys.types ty ON ty.user_type_id = col.user_type_id
    WHERE col.object_id = OBJECT_ID(N'dbo.Ventas') AND col.name = N'NumeroOrigen'
      AND ty.name = N'nvarchar' AND col.max_length = 40 AND col.is_nullable = 1 AND col.is_computed = 0)
    THROW 51047, 'Verificacion fallida: la columna no quedo con la definicion esperada. Se revierte.', 1;

PRINT 'H-01: columna Ventas.NumeroOrigen agregada y migracion registrada.';

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

/* Rollback (solo si hiciera falta volver atras; NO ejecutar por defecto). Valido mientras ningun codigo de H-01 haya escrito
   NumeroOrigen que se quiera conservar; despues de revertir hay que volver a una version de la aplicacion SIN H-01.
  BEGIN TRANSACTION;
  ALTER TABLE dbo.Ventas DROP COLUMN NumeroOrigen;
  DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261106072023_H01NumeroOrigenVenta';
  COMMIT;
*/

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddMercadoLibreModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MercadoLibreAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeliUserId = table.Column<long>(type: "bigint", nullable: false),
                    Nickname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    UltimaPruebaConexionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimaPruebaConexionOk = table.Column<bool>(type: "bit", nullable: true),
                    UltimaImportacionListingsUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibrePriceBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Origen = table.Column<int>(type: "int", nullable: false),
                    ValorAjustePorcentaje = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    FiltrosJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CantidadPublicaciones = table.Column<int>(type: "int", nullable: false),
                    SimulacionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AplicadoEnSimulacion = table.Column<bool>(type: "bit", nullable: false),
                    SolicitadoPor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FechaSolicitud = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AplicadoPor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FechaAplicacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevertidoPor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FechaReversion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoReversion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibrePriceBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibrePublicacionBorradores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false),
                    CategoryIdMl = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Condicion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ListingTypeId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Garantia = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErroresValidacion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaValidacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicadoItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FechaPublicadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicadoEnSimulacion = table.Column<bool>(type: "bit", nullable: false),
                    FechaSimulacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayloadSimuladoJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibrePublicacionBorradores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibrePublicacionBorradores_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    ListingId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Operacion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Exito = table.Column<bool>(type: "bit", nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DuracionMs = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MeliUserId = table.Column<long>(type: "bigint", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: true),
                    RawBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecibidoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Procesado = table.Column<bool>(type: "bit", nullable: false),
                    ProcesadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorProcesamiento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IntentosProcesamiento = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreConfiguraciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    ListaPrecioId = table.Column<int>(type: "int", nullable: true),
                    SucursalId = table.Column<int>(type: "int", nullable: true),
                    ClienteMercadoLibreId = table.Column<int>(type: "int", nullable: true),
                    AjusteCanalPorcentaje = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    ComisionEstimadaPorcentaje = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CostoEnvioEstimado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MargenMinimoPorcentaje = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    ReglaRedondeo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrigenStock = table.Column<int>(type: "int", nullable: false),
                    SyncAutomaticaStock = table.Column<bool>(type: "bit", nullable: false),
                    SyncAutomaticaPrecio = table.Column<bool>(type: "bit", nullable: false),
                    ImportacionAutomaticaOrdenes = table.Column<bool>(type: "bit", nullable: false),
                    CrearVentaAutomatica = table.Column<bool>(type: "bit", nullable: false),
                    PermitirPublicacionDesdeErp = table.Column<bool>(type: "bit", nullable: false),
                    ModoSimulacion = table.Column<bool>(type: "bit", nullable: false),
                    PoliticaDevolucion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreConfiguraciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreConfiguraciones_Clientes_ClienteMercadoLibreId",
                        column: x => x.ClienteMercadoLibreId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreConfiguraciones_ListasPrecios_ListaPrecioId",
                        column: x => x.ListaPrecioId,
                        principalTable: "ListasPrecios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreConfiguraciones_MercadoLibreAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MercadoLibreAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreConfiguraciones_Sucursales_SucursalId",
                        column: x => x.SucursalId,
                        principalTable: "Sucursales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreListings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: false),
                    SoldQuantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubStatus = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Permalink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CategoryId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ListingTypeId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SellerSku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TieneVariaciones = table.Column<bool>(type: "bit", nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    OrigenStockOverride = table.Column<int>(type: "int", nullable: true),
                    ProductoUnidadId = table.Column<int>(type: "int", nullable: true),
                    LastSyncUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListings_MercadoLibreAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MercadoLibreAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListings_ProductoUnidades_ProductoUnidadId",
                        column: x => x.ProductoUnidadId,
                        principalTable: "ProductoUnidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListings_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    MeliOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BuyerId = table.Column<long>(type: "bigint", nullable: true),
                    BuyerNickname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShipmentId = table.Column<long>(type: "bigint", nullable: true),
                    VentaId = table.Column<int>(type: "int", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstadoInterno = table.Column<int>(type: "int", nullable: false),
                    FechaProcesadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorProcesamiento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MontoComision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MontoEnvio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NetoEstimado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NetoReal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FechaLiquidacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MovimientoCajaId = table.Column<int>(type: "int", nullable: true),
                    ShipmentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ShipmentSubStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TrackingMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShippingMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShippingType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FechaDespachoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaEntregadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaUltimaActualizacionEnvioUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawShipmentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstadoEnvioInterno = table.Column<int>(type: "int", nullable: false),
                    DevolucionEstado = table.Column<int>(type: "int", nullable: false),
                    DevolucionNota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreOrders_MercadoLibreAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MercadoLibreAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MercadoLibreOrders_MovimientosCaja_MovimientoCajaId",
                        column: x => x.MovimientoCajaId,
                        principalTable: "MovimientosCaja",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreOrders_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreListingVariations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    VariationId = table.Column<long>(type: "bigint", nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: false),
                    SoldQuantity = table.Column<int>(type: "int", nullable: false),
                    SellerSku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    OrigenStockOverride = table.Column<int>(type: "int", nullable: true),
                    ProductoUnidadId = table.Column<int>(type: "int", nullable: true),
                    AttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreListingVariations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListingVariations_MercadoLibreListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MercadoLibreListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListingVariations_ProductoUnidades_ProductoUnidadId",
                        column: x => x.ProductoUnidadId,
                        principalTable: "ProductoUnidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreListingVariations_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibrePriceBatchItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariationId = table.Column<long>(type: "bigint", nullable: true),
                    Titulo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PrecioAnterior = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrecioNuevo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiferenciaPorcentaje = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    PayloadAplicacionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TieneAdvertencia = table.Column<bool>(type: "bit", nullable: false),
                    MensajeAdvertencia = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Aplicado = table.Column<bool>(type: "bit", nullable: false),
                    Revertido = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibrePriceBatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibrePriceBatchItems_MercadoLibreListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MercadoLibreListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MercadoLibrePriceBatchItems_MercadoLibrePriceBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "MercadoLibrePriceBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<long>(type: "bigint", nullable: false),
                    ListingId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    MeliUserId = table.Column<long>(type: "bigint", nullable: true),
                    TextoPregunta = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    RespuestaTexto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaPreguntaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaRespuestaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EsSimulada = table.Column<bool>(type: "bit", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorProcesamiento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UsuarioRespuesta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreQuestions_MercadoLibreAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MercadoLibreAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MercadoLibreQuestions_MercadoLibreListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MercadoLibreListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreQuestions_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MercadoLibreClaimId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    MercadoLibreOrderId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolucionManual = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccionStock = table.Column<int>(type: "int", nullable: false),
                    AccionEconomica = table.Column<int>(type: "int", nullable: false),
                    MovimientoStockId = table.Column<int>(type: "int", nullable: true),
                    MovimientoCajaId = table.Column<int>(type: "int", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EsSimuladoLocal = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaResolucionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioResolucion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreClaims_MercadoLibreOrders_MercadoLibreOrderId",
                        column: x => x.MercadoLibreOrderId,
                        principalTable: "MercadoLibreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MercadoLibreClaims_MovimientosCaja_MovimientoCajaId",
                        column: x => x.MovimientoCajaId,
                        principalTable: "MovimientosCaja",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MercadoLibreClaims_MovimientosStock_MovimientoStockId",
                        column: x => x.MovimientoStockId,
                        principalTable: "MovimientosStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    MeliOrderId = table.Column<long>(type: "bigint", nullable: true),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Direccion = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    MeliUserId = table.Column<long>(type: "bigint", nullable: true),
                    FechaMensajeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaRespuestaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EsSimulado = table.Column<bool>(type: "bit", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorProcesamiento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UsuarioEnvio = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreMessages_MercadoLibreAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MercadoLibreAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MercadoLibreMessages_MercadoLibreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MercadoLibreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreOrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariationId = table.Column<long>(type: "bigint", nullable: true),
                    Titulo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Cantidad = table.Column<int>(type: "int", nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SellerSku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SaleFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    UnidadesAsignadas = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreOrderItems_MercadoLibreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MercadoLibreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreAccounts_MeliUserId",
                table: "MercadoLibreAccounts",
                column: "MeliUserId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_Estado",
                table: "MercadoLibreClaims",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_FechaCreacionUtc",
                table: "MercadoLibreClaims",
                column: "FechaCreacionUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_MercadoLibreClaimId",
                table: "MercadoLibreClaims",
                column: "MercadoLibreClaimId",
                unique: true,
                filter: "MercadoLibreClaimId IS NOT NULL AND IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_MercadoLibreOrderId",
                table: "MercadoLibreClaims",
                column: "MercadoLibreOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_MovimientoCajaId",
                table: "MercadoLibreClaims",
                column: "MovimientoCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_MovimientoStockId",
                table: "MercadoLibreClaims",
                column: "MovimientoStockId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreClaims_Tipo",
                table: "MercadoLibreClaims",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreConfiguraciones_AccountId",
                table: "MercadoLibreConfiguraciones",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreConfiguraciones_ClienteMercadoLibreId",
                table: "MercadoLibreConfiguraciones",
                column: "ClienteMercadoLibreId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreConfiguraciones_ListaPrecioId",
                table: "MercadoLibreConfiguraciones",
                column: "ListaPrecioId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreConfiguraciones_SucursalId",
                table: "MercadoLibreConfiguraciones",
                column: "SucursalId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_AccountId",
                table: "MercadoLibreListings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_ItemId",
                table: "MercadoLibreListings",
                column: "ItemId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_ProductoId",
                table: "MercadoLibreListings",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_ProductoUnidadId",
                table: "MercadoLibreListings",
                column: "ProductoUnidadId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_SellerSku",
                table: "MercadoLibreListings",
                column: "SellerSku");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListings_Status",
                table: "MercadoLibreListings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListingVariations_ListingId_VariationId",
                table: "MercadoLibreListingVariations",
                columns: new[] { "ListingId", "VariationId" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListingVariations_ProductoId",
                table: "MercadoLibreListingVariations",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListingVariations_ProductoUnidadId",
                table: "MercadoLibreListingVariations",
                column: "ProductoUnidadId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreListingVariations_SellerSku",
                table: "MercadoLibreListingVariations",
                column: "SellerSku");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_AccountId",
                table: "MercadoLibreMessages",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_Estado",
                table: "MercadoLibreMessages",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_FechaMensajeUtc",
                table: "MercadoLibreMessages",
                column: "FechaMensajeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_MeliOrderId",
                table: "MercadoLibreMessages",
                column: "MeliOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_MessageId",
                table: "MercadoLibreMessages",
                column: "MessageId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreMessages_OrderId",
                table: "MercadoLibreMessages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrderItems_ItemId",
                table: "MercadoLibreOrderItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrderItems_OrderId",
                table: "MercadoLibreOrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_AccountId",
                table: "MercadoLibreOrders",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_EstadoEnvioInterno",
                table: "MercadoLibreOrders",
                column: "EstadoEnvioInterno");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_EstadoInterno",
                table: "MercadoLibreOrders",
                column: "EstadoInterno");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_MeliOrderId",
                table: "MercadoLibreOrders",
                column: "MeliOrderId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_MovimientoCajaId",
                table: "MercadoLibreOrders",
                column: "MovimientoCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_ShipmentId",
                table: "MercadoLibreOrders",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_Status",
                table: "MercadoLibreOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreOrders_VentaId",
                table: "MercadoLibreOrders",
                column: "VentaId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePriceBatches_CreatedAt",
                table: "MercadoLibrePriceBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePriceBatches_Estado",
                table: "MercadoLibrePriceBatches",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePriceBatchItems_BatchId",
                table: "MercadoLibrePriceBatchItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePriceBatchItems_ListingId",
                table: "MercadoLibrePriceBatchItems",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePublicacionBorradores_Estado",
                table: "MercadoLibrePublicacionBorradores",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibrePublicacionBorradores_ProductoId",
                table: "MercadoLibrePublicacionBorradores",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_AccountId",
                table: "MercadoLibreQuestions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_Estado",
                table: "MercadoLibreQuestions",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_FechaPreguntaUtc",
                table: "MercadoLibreQuestions",
                column: "FechaPreguntaUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_ListingId",
                table: "MercadoLibreQuestions",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_ProductoId",
                table: "MercadoLibreQuestions",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreQuestions_QuestionId",
                table: "MercadoLibreQuestions",
                column: "QuestionId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreSyncLogs_AccountId",
                table: "MercadoLibreSyncLogs",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreSyncLogs_CreatedAt",
                table: "MercadoLibreSyncLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreSyncLogs_Operacion",
                table: "MercadoLibreSyncLogs",
                column: "Operacion");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreWebhookEvents_Procesado",
                table: "MercadoLibreWebhookEvents",
                column: "Procesado");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreWebhookEvents_RecibidoUtc",
                table: "MercadoLibreWebhookEvents",
                column: "RecibidoUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreWebhookEvents_Topic",
                table: "MercadoLibreWebhookEvents",
                column: "Topic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MercadoLibreClaims");

            migrationBuilder.DropTable(
                name: "MercadoLibreConfiguraciones");

            migrationBuilder.DropTable(
                name: "MercadoLibreListingVariations");

            migrationBuilder.DropTable(
                name: "MercadoLibreMessages");

            migrationBuilder.DropTable(
                name: "MercadoLibreOrderItems");

            migrationBuilder.DropTable(
                name: "MercadoLibrePriceBatchItems");

            migrationBuilder.DropTable(
                name: "MercadoLibrePublicacionBorradores");

            migrationBuilder.DropTable(
                name: "MercadoLibreQuestions");

            migrationBuilder.DropTable(
                name: "MercadoLibreSyncLogs");

            migrationBuilder.DropTable(
                name: "MercadoLibreWebhookEvents");

            migrationBuilder.DropTable(
                name: "MercadoLibreOrders");

            migrationBuilder.DropTable(
                name: "MercadoLibrePriceBatches");

            migrationBuilder.DropTable(
                name: "MercadoLibreListings");

            migrationBuilder.DropTable(
                name: "MercadoLibreAccounts");
        }
    }
}

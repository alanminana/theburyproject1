using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCotizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cotizaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    NombreClienteLibre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TelefonoClienteLibre = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DescuentoTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MedioPagoSeleccionado = table.Column<int>(type: "int", nullable: true),
                    PlanSeleccionado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CantidadCuotasSeleccionada = table.Column<int>(type: "int", nullable: true),
                    TotalSeleccionado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ValorCuotaSeleccionada = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FechaVencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cotizaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cotizaciones_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CotizacionDetalles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CotizacionId = table.Column<int>(type: "int", nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    CodigoProductoSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NombreProductoSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrecioUnitarioSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DescuentoPorcentajeSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DescuentoImporteSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotizacionDetalles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CotizacionDetalles_Cotizaciones_CotizacionId",
                        column: x => x.CotizacionId,
                        principalTable: "Cotizaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CotizacionDetalles_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CotizacionPagosSimulados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CotizacionId = table.Column<int>(type: "int", nullable: false),
                    MedioPago = table.Column<int>(type: "int", nullable: false),
                    NombreMedioPago = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: true),
                    RecargoPorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    DescuentoPorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    InteresPorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    TasaMensual = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    CostoFinancieroTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorCuota = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Recomendado = table.Column<bool>(type: "bit", nullable: false),
                    Seleccionado = table.Column<bool>(type: "bit", nullable: false),
                    AdvertenciasJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotizacionPagosSimulados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CotizacionPagosSimulados_Cotizaciones_CotizacionId",
                        column: x => x.CotizacionId,
                        principalTable: "Cotizaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionDetalles_CotizacionId",
                table: "CotizacionDetalles",
                column: "CotizacionId");

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionDetalles_ProductoId",
                table: "CotizacionDetalles",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_ClienteId",
                table: "Cotizaciones",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_Estado",
                table: "Cotizaciones",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_Fecha",
                table: "Cotizaciones",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_Numero",
                table: "Cotizaciones",
                column: "Numero",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionPagosSimulados_CotizacionId",
                table: "CotizacionPagosSimulados",
                column: "CotizacionId");

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionPagosSimulados_MedioPago",
                table: "CotizacionPagosSimulados",
                column: "MedioPago");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotizacionDetalles");

            migrationBuilder.DropTable(
                name: "CotizacionPagosSimulados");

            migrationBuilder.DropTable(
                name: "Cotizaciones");
        }
    }
}

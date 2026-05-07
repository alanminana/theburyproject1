using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoCondicionesPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductoCondicionesPago",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    TipoPago = table.Column<int>(type: "int", nullable: false),
                    Permitido = table.Column<bool>(type: "bit", nullable: true),
                    MaxCuotasSinInteres = table.Column<int>(type: "int", nullable: true),
                    MaxCuotasConInteres = table.Column<int>(type: "int", nullable: true),
                    MaxCuotasCredito = table.Column<int>(type: "int", nullable: true),
                    PorcentajeRecargo = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PorcentajeDescuentoMaximo = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoCondicionesPago", x => x.Id);
                    table.CheckConstraint("CK_ProductoCondicionesPago_Cuotas", "([MaxCuotasSinInteres] IS NULL OR [MaxCuotasSinInteres] >= 1) AND ([MaxCuotasConInteres] IS NULL OR [MaxCuotasConInteres] >= 1) AND ([MaxCuotasCredito] IS NULL OR [MaxCuotasCredito] >= 1)");
                    table.CheckConstraint("CK_ProductoCondicionesPago_Porcentajes", "([PorcentajeRecargo] IS NULL OR ([PorcentajeRecargo] >= 0 AND [PorcentajeRecargo] <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR ([PorcentajeDescuentoMaximo] >= 0 AND [PorcentajeDescuentoMaximo] <= 100))");
                    table.ForeignKey(
                        name: "FK_ProductoCondicionesPago_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductoCondicionesPagoTarjeta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoCondicionPagoId = table.Column<int>(type: "int", nullable: false),
                    ConfiguracionTarjetaId = table.Column<int>(type: "int", nullable: true),
                    Permitido = table.Column<bool>(type: "bit", nullable: true),
                    MaxCuotasSinInteres = table.Column<int>(type: "int", nullable: true),
                    MaxCuotasConInteres = table.Column<int>(type: "int", nullable: true),
                    PorcentajeRecargo = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PorcentajeDescuentoMaximo = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoCondicionesPagoTarjeta", x => x.Id);
                    table.CheckConstraint("CK_ProductoCondicionesPagoTarjeta_Cuotas", "([MaxCuotasSinInteres] IS NULL OR [MaxCuotasSinInteres] >= 1) AND ([MaxCuotasConInteres] IS NULL OR [MaxCuotasConInteres] >= 1)");
                    table.CheckConstraint("CK_ProductoCondicionesPagoTarjeta_Porcentajes", "([PorcentajeRecargo] IS NULL OR ([PorcentajeRecargo] >= 0 AND [PorcentajeRecargo] <= 100)) AND ([PorcentajeDescuentoMaximo] IS NULL OR ([PorcentajeDescuentoMaximo] >= 0 AND [PorcentajeDescuentoMaximo] <= 100))");
                    table.ForeignKey(
                        name: "FK_ProductoCondicionesPagoTarjeta_ConfiguracionesTarjeta_ConfiguracionTarjetaId",
                        column: x => x.ConfiguracionTarjetaId,
                        principalTable: "ConfiguracionesTarjeta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductoCondicionesPagoTarjeta_ProductoCondicionesPago_ProductoCondicionPagoId",
                        column: x => x.ProductoCondicionPagoId,
                        principalTable: "ProductoCondicionesPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionesPago_ProductoId",
                table: "ProductoCondicionesPago",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionesPago_ProductoId_TipoPago",
                table: "ProductoCondicionesPago",
                columns: new[] { "ProductoId", "TipoPago" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Activo] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionesPago_TipoPago",
                table: "ProductoCondicionesPago",
                column: "TipoPago");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionesPagoTarjeta_ConfiguracionTarjetaId",
                table: "ProductoCondicionesPagoTarjeta",
                column: "ConfiguracionTarjetaId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoCondicionesPagoTarjeta_Especifica",
                table: "ProductoCondicionesPagoTarjeta",
                columns: new[] { "ProductoCondicionPagoId", "ConfiguracionTarjetaId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoCondicionesPagoTarjeta_General",
                table: "ProductoCondicionesPagoTarjeta",
                column: "ProductoCondicionPagoId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductoCondicionesPagoTarjeta");

            migrationBuilder.DropTable(
                name: "ProductoCondicionesPago");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoCondicionPagoPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductoCondicionPagoPlanes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoCondicionPagoId = table.Column<int>(type: "int", nullable: false),
                    ProductoCondicionPagoTarjetaId = table.Column<int>(type: "int", nullable: true),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AjustePorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    TipoAjuste = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ProductoCondicionPagoPlanes", x => x.Id);
                    table.CheckConstraint("CK_ProductoCondicionPagoPlanes_Ajuste", "[AjustePorcentaje] >= -100.0000 AND [AjustePorcentaje] <= 999.9999");
                    table.CheckConstraint("CK_ProductoCondicionPagoPlanes_Cuotas", "[CantidadCuotas] >= 1");
                    table.ForeignKey(
                        name: "FK_ProductoCondicionPagoPlanes_ProductoCondicionesPagoTarjeta_ProductoCondicionPagoTarjetaId",
                        column: x => x.ProductoCondicionPagoTarjetaId,
                        principalTable: "ProductoCondicionesPagoTarjeta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductoCondicionPagoPlanes_ProductoCondicionesPago_ProductoCondicionPagoId",
                        column: x => x.ProductoCondicionPagoId,
                        principalTable: "ProductoCondicionesPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionPagoPlanes_ProductoCondicionPagoId",
                table: "ProductoCondicionPagoPlanes",
                column: "ProductoCondicionPagoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoCondicionPagoPlanes_ProductoCondicionPagoTarjetaId",
                table: "ProductoCondicionPagoPlanes",
                column: "ProductoCondicionPagoTarjetaId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoCondicionPagoPlanes_General",
                table: "ProductoCondicionPagoPlanes",
                columns: new[] { "ProductoCondicionPagoId", "CantidadCuotas" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProductoCondicionPagoTarjetaId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoCondicionPagoPlanes_Tarjeta",
                table: "ProductoCondicionPagoPlanes",
                columns: new[] { "ProductoCondicionPagoId", "ProductoCondicionPagoTarjetaId", "CantidadCuotas" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProductoCondicionPagoTarjetaId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductoCondicionPagoPlanes");
        }
    }
}

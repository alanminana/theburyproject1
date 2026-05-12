using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionPagoPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionPagoPlanes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfiguracionPagoId = table.Column<int>(type: "int", nullable: false),
                    ConfiguracionTarjetaId = table.Column<int>(type: "int", nullable: true),
                    TipoPago = table.Column<int>(type: "int", nullable: false),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TipoAjuste = table.Column<int>(type: "int", nullable: false),
                    AjustePorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    Etiqueta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ConfiguracionPagoPlanes", x => x.Id);
                    table.CheckConstraint("CK_ConfiguracionPagoPlanes_Ajuste", "[AjustePorcentaje] >= -100.0000 AND [AjustePorcentaje] <= 999.9999");
                    table.CheckConstraint("CK_ConfiguracionPagoPlanes_Cuotas", "[CantidadCuotas] >= 1");
                    table.ForeignKey(
                        name: "FK_ConfiguracionPagoPlanes_ConfiguracionesPago_ConfiguracionPagoId",
                        column: x => x.ConfiguracionPagoId,
                        principalTable: "ConfiguracionesPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConfiguracionPagoPlanes_ConfiguracionesTarjeta_ConfiguracionTarjetaId",
                        column: x => x.ConfiguracionTarjetaId,
                        principalTable: "ConfiguracionesTarjeta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionPagoPlanes_ConfiguracionPagoId",
                table: "ConfiguracionPagoPlanes",
                column: "ConfiguracionPagoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionPagoPlanes_ConfiguracionTarjetaId",
                table: "ConfiguracionPagoPlanes",
                column: "ConfiguracionTarjetaId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionPagoPlanes_TipoPago_Activo_Orden",
                table: "ConfiguracionPagoPlanes",
                columns: new[] { "TipoPago", "Activo", "Orden" });

            migrationBuilder.CreateIndex(
                name: "UX_ConfiguracionPagoPlanes_General",
                table: "ConfiguracionPagoPlanes",
                columns: new[] { "ConfiguracionPagoId", "TipoPago", "CantidadCuotas" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ConfiguracionPagoPlanes_Tarjeta",
                table: "ConfiguracionPagoPlanes",
                columns: new[] { "ConfiguracionPagoId", "TipoPago", "ConfiguracionTarjetaId", "CantidadCuotas" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Activo] = 1 AND [ConfiguracionTarjetaId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionPagoPlanes");
        }
    }
}

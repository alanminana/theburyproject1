using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AjusteMedioPagoVentaYAcuerdoVigenteUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcuerdosPago_CreditoId",
                table: "AcuerdosPago");

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteMedioPagoMonto",
                table: "Ventas",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteMedioPagoPorcentaje",
                table: "Ventas",
                type: "decimal(9,4)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_AcuerdosPago_CreditoVigente",
                table: "AcuerdosPago",
                column: "CreditoId",
                unique: true,
                filter: "IsDeleted = 0 AND Estado IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AcuerdosPago_CreditoVigente",
                table: "AcuerdosPago");

            migrationBuilder.DropColumn(
                name: "AjusteMedioPagoMonto",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "AjusteMedioPagoPorcentaje",
                table: "Ventas");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_CreditoId",
                table: "AcuerdosPago",
                column: "CreditoId");
        }
    }
}

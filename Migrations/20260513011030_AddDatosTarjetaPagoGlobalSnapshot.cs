using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddDatosTarjetaPagoGlobalSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfiguracionPagoPlanId",
                table: "DatosTarjeta",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoAjustePagoAplicado",
                table: "DatosTarjeta",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombrePlanPagoSnapshot",
                table: "DatosTarjeta",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeAjustePagoAplicado",
                table: "DatosTarjeta",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatosTarjeta_ConfiguracionPagoPlanId",
                table: "DatosTarjeta",
                column: "ConfiguracionPagoPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_DatosTarjeta_ConfiguracionPagoPlanes_ConfiguracionPagoPlanId",
                table: "DatosTarjeta",
                column: "ConfiguracionPagoPlanId",
                principalTable: "ConfiguracionPagoPlanes",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DatosTarjeta_ConfiguracionPagoPlanes_ConfiguracionPagoPlanId",
                table: "DatosTarjeta");

            migrationBuilder.DropIndex(
                name: "IX_DatosTarjeta_ConfiguracionPagoPlanId",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "ConfiguracionPagoPlanId",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "MontoAjustePagoAplicado",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "NombrePlanPagoSnapshot",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "PorcentajeAjustePagoAplicado",
                table: "DatosTarjeta");
        }
    }
}

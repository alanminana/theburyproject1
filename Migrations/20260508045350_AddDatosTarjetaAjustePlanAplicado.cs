using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddDatosTarjetaAjustePlanAplicado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontoAjustePlanAplicado",
                table: "DatosTarjeta",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeAjustePlanAplicado",
                table: "DatosTarjeta",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MontoAjustePlanAplicado",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "PorcentajeAjustePlanAplicado",
                table: "DatosTarjeta");
        }
    }
}

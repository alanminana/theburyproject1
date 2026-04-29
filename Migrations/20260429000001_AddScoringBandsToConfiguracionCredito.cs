using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringBandsToConfiguracionCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeRiesgoMedio",
                table: "ConfiguracionesCredito",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 5.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeRiesgoExcelente",
                table: "ConfiguracionesCredito",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 7.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UmbralCuotaIngresoBajo",
                table: "ConfiguracionesCredito",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<decimal>(
                name: "UmbralCuotaIngresoAlto",
                table: "ConfiguracionesCredito",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.45m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PuntajeRiesgoMedio",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "PuntajeRiesgoExcelente",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "UmbralCuotaIngresoBajo",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "UmbralCuotaIngresoAlto",
                table: "ConfiguracionesCredito");
        }
    }
}

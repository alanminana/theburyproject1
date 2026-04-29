using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringThresholdsToConfiguracionCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontoRequiereGarante",
                table: "ConfiguracionesCredito",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 500000m);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeMinimoParaAnalisis",
                table: "ConfiguracionesCredito",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 50m);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeMinimoParaAprobacion",
                table: "ConfiguracionesCredito",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 70m);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeRiesgoMinimo",
                table: "ConfiguracionesCredito",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 3.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RelacionCuotaIngresoMax",
                table: "ConfiguracionesCredito",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.35m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MontoRequiereGarante",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "PuntajeMinimoParaAnalisis",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "PuntajeMinimoParaAprobacion",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "PuntajeRiesgoMinimo",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "RelacionCuotaIngresoMax",
                table: "ConfiguracionesCredito");
        }
    }
}

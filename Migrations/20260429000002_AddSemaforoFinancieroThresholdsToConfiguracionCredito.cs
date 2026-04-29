using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddSemaforoFinancieroThresholdsToConfiguracionCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SemaforoFinancieroRatioVerdeMax",
                table: "ConfiguracionesCredito",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.08m);

            migrationBuilder.AddColumn<decimal>(
                name: "SemaforoFinancieroRatioAmarilloMax",
                table: "ConfiguracionesCredito",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.15m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SemaforoFinancieroRatioVerdeMax",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "SemaforoFinancieroRatioAmarilloMax",
                table: "ConfiguracionesCredito");
        }
    }
}

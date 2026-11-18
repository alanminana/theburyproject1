using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class D02_ReferenciasVerificadasMinimasCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReferenciasVerificadasMinimas",
                table: "ConfiguracionesCredito",
                type: "int",
                nullable: false,
                // Decisión D-02: el mínimo vigente por defecto es 1 (también para la fila de configuración ya existente).
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenciasVerificadasMinimas",
                table: "ConfiguracionesCredito");
        }
    }
}

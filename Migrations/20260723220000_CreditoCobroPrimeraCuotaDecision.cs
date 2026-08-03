using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class CreditoCobroPrimeraCuotaDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Micro-lote 6 (F2): decisión persistida de cobrar la primera cuota al confirmar la venta.
            // Se toma en la configuración del crédito y el servidor la revalida contra la base al
            // confirmar (no se confía en el payload). Columnas nullable/con default para créditos previos.
            migrationBuilder.AddColumn<bool>(
                name: "CobrarPrimeraCuotaSolicitada",
                table: "Creditos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MedioPagoPrimeraCuota",
                table: "Creditos",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CobrarPrimeraCuotaSolicitada",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "MedioPagoPrimeraCuota",
                table: "Creditos");
        }
    }
}

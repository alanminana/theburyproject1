using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AlterCupoPorPuntajeInterno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntajeCreditoLimites_Puntaje",
                table: "PuntajeCreditoLimites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.InsertData(
                table: "PuntajeCreditoLimites",
                columns: new[] { "Id", "Activo", "FechaActualizacion", "LimiteMonto", "Puntaje", "UsuarioActualizacion" },
                values: new object[] { 6, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 200000m, 0, "System" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntajeCreditoLimites_Puntaje",
                table: "PuntajeCreditoLimites",
                sql: "[Puntaje] >= 0 AND [Puntaje] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones",
                sql: "[NivelCreditoManual] IS NULL OR ([NivelCreditoManual] >= 0 AND [NivelCreditoManual] <= 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PuntajeCreditoLimites_Puntaje",
                table: "PuntajeCreditoLimites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DeleteData(
                table: "PuntajeCreditoLimites",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PuntajeCreditoLimites_Puntaje",
                table: "PuntajeCreditoLimites",
                sql: "[Puntaje] >= 1 AND [Puntaje] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones",
                sql: "[NivelCreditoManual] IS NULL OR ([NivelCreditoManual] >= 1 AND [NivelCreditoManual] <= 5)");
        }
    }
}

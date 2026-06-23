using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddNivelCreditoManualCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoNivelCreditoManual",
                table: "ClientesCreditoConfiguraciones",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NivelCreditoManualAsignadoPor",
                table: "ClientesCreditoConfiguraciones",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NivelCreditoManualAsignadoEnUtc",
                table: "ClientesCreditoConfiguraciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones",
                sql: "[NivelCreditoManual] IS NULL OR ([NivelCreditoManual] >= 1 AND [NivelCreditoManual] <= 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientesCreditoConfiguraciones_NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropColumn(
                name: "NivelCreditoManual",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropColumn(
                name: "MotivoNivelCreditoManual",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropColumn(
                name: "NivelCreditoManualAsignadoPor",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropColumn(
                name: "NivelCreditoManualAsignadoEnUtc",
                table: "ClientesCreditoConfiguraciones");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class DropColumnasCreditoSinUso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarda anti pérdida de datos: estas columnas no tienen ningún consumidor en el código (nunca se
            // escriben ni se leen) y se verificaron sin datos en la base de desarrollo. Si alguna otra base
            // tiene valores, la migración aborta en vez de borrarlos en silencio.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [ContratosVentaCredito] WHERE [FechaImpresionUtc] IS NOT NULL) " +
                "THROW 51000, 'DropColumnasCreditoSinUso: ContratosVentaCredito.FechaImpresionUtc tiene valores; no se elimina para evitar perder datos.', 1;");

            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [ClientesCreditoConfiguraciones] WHERE [AprobadoEnUtc] IS NOT NULL OR [OverrideAprobadoEnUtc] IS NOT NULL) " +
                "THROW 51000, 'DropColumnasCreditoSinUso: ClientesCreditoConfiguraciones.AprobadoEnUtc/OverrideAprobadoEnUtc tienen valores; no se eliminan para evitar perder datos.', 1;");

            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [ConfiguracionesCredito] WHERE [ModificadoPor] IS NOT NULL OR [NotificacionesCambioEstado] = 1 " +
                "OR ([MensajeConfiguracionDeshabilitada] IS NOT NULL AND [MensajeConfiguracionDeshabilitada] <> N'La validación de aptitud crediticia no está configurada. Configure los parámetros en Administración.')) " +
                "THROW 51000, 'DropColumnasCreditoSinUso: ConfiguracionesCredito tiene valores propios en ModificadoPor/NotificacionesCambioEstado/MensajeConfiguracionDeshabilitada; no se eliminan para evitar perder datos.', 1;");

            migrationBuilder.DropColumn(
                name: "FechaImpresionUtc",
                table: "ContratosVentaCredito");

            migrationBuilder.DropColumn(
                name: "MensajeConfiguracionDeshabilitada",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "ModificadoPor",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "NotificacionesCambioEstado",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "AprobadoEnUtc",
                table: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropColumn(
                name: "OverrideAprobadoEnUtc",
                table: "ClientesCreditoConfiguraciones");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaImpresionUtc",
                table: "ContratosVentaCredito",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MensajeConfiguracionDeshabilitada",
                table: "ConfiguracionesCredito",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificadoPor",
                table: "ConfiguracionesCredito",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificacionesCambioEstado",
                table: "ConfiguracionesCredito",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AprobadoEnUtc",
                table: "ClientesCreditoConfiguraciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverrideAprobadoEnUtc",
                table: "ClientesCreditoConfiguraciones",
                type: "datetime2",
                nullable: true);
        }
    }
}

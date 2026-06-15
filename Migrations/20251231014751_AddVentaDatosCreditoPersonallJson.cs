using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDatosCreditoPersonallJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Única operación genuina de esta migración: la columna Ventas.DatosCreditoPersonallJson.
            migrationBuilder.AddColumn<string>(
                name: "DatosCreditoPersonallJson",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: true);

            // NOTA: el resto de las operaciones originales de esta migración eran duplicados
            // de migraciones anteriores y rompían la creación de una DB desde cero
            // ("Column name ... specified more than once" / "There is already an object named ...").
            //   - Ventas.RazonesAutorizacionJson y Ventas.RequisitosPendientesJson
            //     ya las crea 20251230210000_AddVentaValidacionUnificada.
            //   - Clientes.EstadoCrediticio, FechaUltimaEvaluacion, LimiteCredito, MotivoNoApto
            //     y la tabla ConfiguracionesCredito ya las crea
            //     20251230200000_AddClienteAptitudCredito.
            // Se eliminaron de aquí para no agregarlas dos veces.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo se revierte la operación genuina (ver Up).
            migrationBuilder.DropColumn(
                name: "DatosCreditoPersonallJson",
                table: "Ventas");
        }
    }
}

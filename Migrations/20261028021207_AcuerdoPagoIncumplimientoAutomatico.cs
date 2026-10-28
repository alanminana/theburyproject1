using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AcuerdoPagoIncumplimientoAutomatico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasGraciaIncumplimientoAcuerdo",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "DiasGraciaIncumplimiento",
                table: "AcuerdosPago",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaActivacionComercial",
                table: "AcuerdosPago",
                type: "date",
                nullable: true);

            // Acuerdos ya Activos (activados con la versión anterior): la entrega inicial vence el día comercial (Argentina) de su
            // activación, que se obtiene de FechaActivacion (UTC). Sin esto su entrega nunca se evaluaría.
            migrationBuilder.Sql("UPDATE [AcuerdosPago] SET [FechaActivacionComercial] = CAST((CAST([FechaActivacion] AS datetimeoffset) AT TIME ZONE 'Argentina Standard Time') AS date) WHERE [Estado] = 2 AND [IsDeleted] = 0 AND [FechaActivacion] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiasGraciaIncumplimientoAcuerdo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasGraciaIncumplimiento",
                table: "AcuerdosPago");

            migrationBuilder.DropColumn(
                name: "FechaActivacionComercial",
                table: "AcuerdosPago");
        }
    }
}

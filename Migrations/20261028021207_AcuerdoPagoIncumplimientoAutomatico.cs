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

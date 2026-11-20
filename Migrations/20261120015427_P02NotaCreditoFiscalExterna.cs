using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class P02NotaCreditoFiscalExterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalExternaCae",
                table: "NotasCredito",
                type: "nvarchar(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalExternaFecha",
                table: "NotasCredito",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiscalExternaImporte",
                table: "NotasCredito",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalExternaNumero",
                table: "NotasCredito",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalExternaPuntoVenta",
                table: "NotasCredito",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalExternaRegistradaEn",
                table: "NotasCredito",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalExternaRegistradaPor",
                table: "NotasCredito",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalExternaTipo",
                table: "NotasCredito",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotasCredito_FiscalExterna",
                table: "NotasCredito",
                columns: new[] { "FiscalExternaPuntoVenta", "FiscalExternaTipo", "FiscalExternaNumero" },
                unique: true,
                filter: "[FiscalExternaNumero] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotasCredito_FiscalExterna",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaCae",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaFecha",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaImporte",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaNumero",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaPuntoVenta",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaRegistradaEn",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaRegistradaPor",
                table: "NotasCredito");

            migrationBuilder.DropColumn(
                name: "FiscalExternaTipo",
                table: "NotasCredito");
        }
    }
}

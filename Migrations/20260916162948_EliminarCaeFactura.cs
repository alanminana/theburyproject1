using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class EliminarCaeFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Facturas_CAE",
                table: "Facturas");

            migrationBuilder.DropColumn(
                name: "CAE",
                table: "Facturas");

            migrationBuilder.DropColumn(
                name: "FechaVencimientoCAE",
                table: "Facturas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CAE",
                table: "Facturas",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaVencimientoCAE",
                table: "Facturas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_CAE",
                table: "Facturas",
                column: "CAE");
        }
    }
}

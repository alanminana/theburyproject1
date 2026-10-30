using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class H10ChequesRecibidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MovimientoOrigenId",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcreditadoAt",
                table: "DatosCheque",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcreditadoPor",
                table: "DatosCheque",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoRechazo",
                table: "DatosCheque",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RechazadoAt",
                table: "DatosCheque",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RechazadoPor",
                table: "DatosCheque",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_MovimientoOrigenId_Concepto",
                table: "MovimientosCaja",
                columns: new[] { "MovimientoOrigenId", "Concepto" },
                unique: true,
                filter: "[MovimientoOrigenId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId",
                table: "MovimientosCaja",
                column: "MovimientoOrigenId",
                principalTable: "MovimientosCaja",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId",
                table: "MovimientosCaja");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosCaja_MovimientoOrigenId_Concepto",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "MovimientoOrigenId",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "AcreditadoAt",
                table: "DatosCheque");

            migrationBuilder.DropColumn(
                name: "AcreditadoPor",
                table: "DatosCheque");

            migrationBuilder.DropColumn(
                name: "MotivoRechazo",
                table: "DatosCheque");

            migrationBuilder.DropColumn(
                name: "RechazadoAt",
                table: "DatosCheque");

            migrationBuilder.DropColumn(
                name: "RechazadoPor",
                table: "DatosCheque");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUltimoExitoBcraCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SituacionCrediticiaBcraUltimoExito",
                table: "Clientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituacionCrediticiaDescripcionUltimoExito",
                table: "Clientes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituacionCrediticiaPeriodoUltimoExito",
                table: "Clientes",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SituacionCrediticiaUltimoExitoUtc",
                table: "Clientes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaBcraUltimoExito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaDescripcionUltimoExito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaPeriodoUltimoExito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaUltimoExitoUtc",
                table: "Clientes");
        }
    }
}

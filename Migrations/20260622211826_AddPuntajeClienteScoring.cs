using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPuntajeClienteScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AntiguedadDias",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CreditosConAtraso",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CreditosEnTermino",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PuntajeCliente",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaVentaFecha",
                table: "Clientes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AntiguedadDias",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "CreditosConAtraso",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "CreditosEnTermino",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "PuntajeCliente",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "UltimaVentaFecha",
                table: "Clientes");
        }
    }
}

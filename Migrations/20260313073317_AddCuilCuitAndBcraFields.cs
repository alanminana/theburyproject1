using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCuilCuitAndBcraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CuilCuit",
                table: "Clientes",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SituacionCrediticiaBcra",
                table: "Clientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SituacionCrediticiaConsultaOk",
                table: "Clientes",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituacionCrediticiaDescripcion",
                table: "Clientes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SituacionCrediticiaPeriodo",
                table: "Clientes",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SituacionCrediticiaUltimaConsultaUtc",
                table: "Clientes",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuilCuit",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaBcra",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaConsultaOk",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaDescripcion",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaPeriodo",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "SituacionCrediticiaUltimaConsultaUtc",
                table: "Clientes");
        }
    }
}

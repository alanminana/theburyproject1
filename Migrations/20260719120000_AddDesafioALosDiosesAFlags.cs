using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddDesafioALosDiosesAFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DesafioALosDiosesActivado",
                table: "TerminosCondicionesAceptaciones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DesafioALosDiosesActivadoEnUtc",
                table: "TerminosCondicionesAceptaciones",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesafioALosDiosesActivado",
                table: "TerminosCondicionesAceptaciones");

            migrationBuilder.DropColumn(
                name: "DesafioALosDiosesActivadoEnUtc",
                table: "TerminosCondicionesAceptaciones");
        }
    }
}

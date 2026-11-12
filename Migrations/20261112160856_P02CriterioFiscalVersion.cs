using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class P02CriterioFiscalVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CriterioFiscalVersion",
                table: "Ventas",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriterioFiscalVersion",
                table: "Ventas");
        }
    }
}

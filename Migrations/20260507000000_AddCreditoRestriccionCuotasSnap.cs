using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditoRestriccionCuotasSnap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FuenteRestriccionCuotasSnap",
                table: "Creditos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxCuotasBaseSnap",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductoIdRestrictivoSnap",
                table: "Creditos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FuenteRestriccionCuotasSnap",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "MaxCuotasBaseSnap",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "ProductoIdRestrictivoSnap",
                table: "Creditos");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddBorradorCategoriaSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CategoryEsHoja",
                table: "MercadoLibrePublicacionBorradores",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryNombre",
                table: "MercadoLibrePublicacionBorradores",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryPathFromRoot",
                table: "MercadoLibrePublicacionBorradores",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryEsHoja",
                table: "MercadoLibrePublicacionBorradores");

            migrationBuilder.DropColumn(
                name: "CategoryNombre",
                table: "MercadoLibrePublicacionBorradores");

            migrationBuilder.DropColumn(
                name: "CategoryPathFromRoot",
                table: "MercadoLibrePublicacionBorradores");
        }
    }
}

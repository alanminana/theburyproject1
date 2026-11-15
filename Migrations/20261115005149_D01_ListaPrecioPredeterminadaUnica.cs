using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class D01_ListaPrecioPredeterminadaUnica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListasPrecios_EsPredeterminada",
                table: "ListasPrecios");

            migrationBuilder.CreateIndex(
                name: "IX_ListasPrecios_EsPredeterminada",
                table: "ListasPrecios",
                column: "EsPredeterminada",
                unique: true,
                filter: "[IsDeleted] = 0 AND [EsPredeterminada] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListasPrecios_EsPredeterminada",
                table: "ListasPrecios");

            migrationBuilder.CreateIndex(
                name: "IX_ListasPrecios_EsPredeterminada",
                table: "ListasPrecios",
                column: "EsPredeterminada");
        }
    }
}

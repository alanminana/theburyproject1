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

            // Normaliza datos previos: si hubiera mas de una predeterminada no eliminada, queda solo la de
            // menor Orden/Id (idempotente: sin duplicados no toca nada). Evita que el indice unico falle.
            migrationBuilder.Sql(@"
UPDATE lp
SET lp.EsPredeterminada = 0
FROM ListasPrecios lp
WHERE lp.IsDeleted = 0 AND lp.EsPredeterminada = 1
  AND lp.Id <> (
      SELECT TOP 1 x.Id FROM ListasPrecios x
      WHERE x.IsDeleted = 0 AND x.EsPredeterminada = 1
      ORDER BY x.Orden, x.Id);");

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

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class D04AperturaCajaUnicaAbierta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Si hubiera datos con dos aperturas vigentes de la misma caja, el índice único fallaría con un error críptico:
            // se corta antes con un mensaje que indica qué corregir (no se modifica ningún dato).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM AperturasCaja WHERE Cerrada = 0 AND IsDeleted = 0 GROUP BY CajaId HAVING COUNT(*) > 1)
    THROW 51004, 'D-04: hay cajas con más de una apertura vigente (Cerrada=0, IsDeleted=0). Cerrar o depurar las duplicadas antes de aplicar esta migración.', 1;");

            migrationBuilder.DropIndex(
                name: "IX_AperturasCaja_CajaId",
                table: "AperturasCaja");

            migrationBuilder.CreateIndex(
                name: "IX_AperturasCaja_CajaId_Cerrada",
                table: "AperturasCaja",
                columns: new[] { "CajaId", "Cerrada" });

            migrationBuilder.CreateIndex(
                name: "UX_AperturasCaja_CajaId_Abierta",
                table: "AperturasCaja",
                column: "CajaId",
                unique: true,
                filter: "[Cerrada] = 0 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AperturasCaja_CajaId_Cerrada",
                table: "AperturasCaja");

            migrationBuilder.DropIndex(
                name: "UX_AperturasCaja_CajaId_Abierta",
                table: "AperturasCaja");

            migrationBuilder.CreateIndex(
                name: "IX_AperturasCaja_CajaId",
                table: "AperturasCaja",
                column: "CajaId");
        }
    }
}

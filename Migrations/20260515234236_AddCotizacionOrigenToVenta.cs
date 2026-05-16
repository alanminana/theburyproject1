using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCotizacionOrigenToVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CotizacionOrigenId",
                table: "Ventas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_CotizacionOrigenId",
                table: "Ventas",
                column: "CotizacionOrigenId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ventas_Cotizaciones_CotizacionOrigenId",
                table: "Ventas",
                column: "CotizacionOrigenId",
                principalTable: "Cotizaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ventas_Cotizaciones_CotizacionOrigenId",
                table: "Ventas");

            migrationBuilder.DropIndex(
                name: "IX_Ventas_CotizacionOrigenId",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "CotizacionOrigenId",
                table: "Ventas");
        }
    }
}

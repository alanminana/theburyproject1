using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddCotizacionDetalleProductoUnidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductoUnidadId",
                table: "CotizacionDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionDetalles_ProductoUnidadId",
                table: "CotizacionDetalles",
                column: "ProductoUnidadId");

            migrationBuilder.AddForeignKey(
                name: "FK_CotizacionDetalles_ProductoUnidades_ProductoUnidadId",
                table: "CotizacionDetalles",
                column: "ProductoUnidadId",
                principalTable: "ProductoUnidades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CotizacionDetalles_ProductoUnidades_ProductoUnidadId",
                table: "CotizacionDetalles");

            migrationBuilder.DropIndex(
                name: "IX_CotizacionDetalles_ProductoUnidadId",
                table: "CotizacionDetalles");

            migrationBuilder.DropColumn(
                name: "ProductoUnidadId",
                table: "CotizacionDetalles");
        }
    }
}

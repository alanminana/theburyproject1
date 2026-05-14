using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDetalleProductoUnidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductoUnidadId",
                table: "VentaDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_VentaDetalles_ProductoUnidadId",
                table: "VentaDetalles",
                column: "ProductoUnidadId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProductoUnidadId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_VentaDetalles_ProductoUnidades_ProductoUnidadId",
                table: "VentaDetalles",
                column: "ProductoUnidadId",
                principalTable: "ProductoUnidades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VentaDetalles_ProductoUnidades_ProductoUnidadId",
                table: "VentaDetalles");

            migrationBuilder.DropIndex(
                name: "UX_VentaDetalles_ProductoUnidadId",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "ProductoUnidadId",
                table: "VentaDetalles");
        }
    }
}

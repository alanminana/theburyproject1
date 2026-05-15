using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoUnidadToDevolucionDetalle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductoUnidadId",
                table: "DevolucionDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DevolucionDetalles_ProductoUnidadId",
                table: "DevolucionDetalles",
                column: "ProductoUnidadId");

            migrationBuilder.AddForeignKey(
                name: "FK_DevolucionDetalles_ProductoUnidades_ProductoUnidadId",
                table: "DevolucionDetalles",
                column: "ProductoUnidadId",
                principalTable: "ProductoUnidades",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DevolucionDetalles_ProductoUnidades_ProductoUnidadId",
                table: "DevolucionDetalles");

            migrationBuilder.DropIndex(
                name: "IX_DevolucionDetalles_ProductoUnidadId",
                table: "DevolucionDetalles");

            migrationBuilder.DropColumn(
                name: "ProductoUnidadId",
                table: "DevolucionDetalles");
        }
    }
}

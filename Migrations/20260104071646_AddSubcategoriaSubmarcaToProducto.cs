using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcategoriaSubmarcaToProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubcategoriaId",
                table: "Productos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmarcaId",
                table: "Productos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Productos_SubcategoriaId",
                table: "Productos",
                column: "SubcategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Productos_SubmarcaId",
                table: "Productos",
                column: "SubmarcaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Productos_Categorias_SubcategoriaId",
                table: "Productos",
                column: "SubcategoriaId",
                principalTable: "Categorias",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Productos_Marcas_SubmarcaId",
                table: "Productos",
                column: "SubmarcaId",
                principalTable: "Marcas",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Productos_Categorias_SubcategoriaId",
                table: "Productos");

            migrationBuilder.DropForeignKey(
                name: "FK_Productos_Marcas_SubmarcaId",
                table: "Productos");

            migrationBuilder.DropIndex(
                name: "IX_Productos_SubcategoriaId",
                table: "Productos");

            migrationBuilder.DropIndex(
                name: "IX_Productos_SubmarcaId",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "SubcategoriaId",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "SubmarcaId",
                table: "Productos");
        }
    }
}

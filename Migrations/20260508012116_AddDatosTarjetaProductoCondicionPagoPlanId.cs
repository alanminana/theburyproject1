using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddDatosTarjetaProductoCondicionPagoPlanId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductoCondicionPagoPlanId",
                table: "DatosTarjeta",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatosTarjeta_ProductoCondicionPagoPlanId",
                table: "DatosTarjeta",
                column: "ProductoCondicionPagoPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_DatosTarjeta_ProductoCondicionPagoPlanes_ProductoCondicionPagoPlanId",
                table: "DatosTarjeta",
                column: "ProductoCondicionPagoPlanId",
                principalTable: "ProductoCondicionPagoPlanes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DatosTarjeta_ProductoCondicionPagoPlanes_ProductoCondicionPagoPlanId",
                table: "DatosTarjeta");

            migrationBuilder.DropIndex(
                name: "IX_DatosTarjeta_ProductoCondicionPagoPlanId",
                table: "DatosTarjeta");

            migrationBuilder.DropColumn(
                name: "ProductoCondicionPagoPlanId",
                table: "DatosTarjeta");
        }
    }
}

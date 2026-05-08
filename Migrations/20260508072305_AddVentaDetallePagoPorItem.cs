using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDetallePagoPorItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontoAjustePlanAplicado",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeAjustePlanAplicado",
                table: "VentaDetalles",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductoCondicionPagoPlanId",
                table: "VentaDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoPago",
                table: "VentaDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VentaDetalles_ProductoCondicionPagoPlanId",
                table: "VentaDetalles",
                column: "ProductoCondicionPagoPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_VentaDetalles_ProductoCondicionPagoPlanes_ProductoCondicionPagoPlanId",
                table: "VentaDetalles",
                column: "ProductoCondicionPagoPlanId",
                principalTable: "ProductoCondicionPagoPlanes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VentaDetalles_ProductoCondicionPagoPlanes_ProductoCondicionPagoPlanId",
                table: "VentaDetalles");

            migrationBuilder.DropIndex(
                name: "IX_VentaDetalles_ProductoCondicionPagoPlanId",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "MontoAjustePlanAplicado",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "PorcentajeAjustePlanAplicado",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "ProductoCondicionPagoPlanId",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "TipoPago",
                table: "VentaDetalles");
        }
    }
}

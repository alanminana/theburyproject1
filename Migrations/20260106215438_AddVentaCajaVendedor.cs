using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaCajaVendedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AperturaCajaId",
                table: "Ventas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendedorUserId",
                table: "Ventas",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_AperturaCajaId",
                table: "Ventas",
                column: "AperturaCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_VendedorUserId",
                table: "Ventas",
                column: "VendedorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ventas_AperturasCaja_AperturaCajaId",
                table: "Ventas",
                column: "AperturaCajaId",
                principalTable: "AperturasCaja",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ventas_AspNetUsers_VendedorUserId",
                table: "Ventas",
                column: "VendedorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ventas_AperturasCaja_AperturaCajaId",
                table: "Ventas");

            migrationBuilder.DropForeignKey(
                name: "FK_Ventas_AspNetUsers_VendedorUserId",
                table: "Ventas");

            migrationBuilder.DropIndex(
                name: "IX_Ventas_AperturaCajaId",
                table: "Ventas");

            migrationBuilder.DropIndex(
                name: "IX_Ventas_VendedorUserId",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "AperturaCajaId",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "VendedorUserId",
                table: "Ventas");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddMovimientoCajaPagoEstructurado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MedioPagoDetalle",
                table: "MovimientosCaja",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecargoDebitoAplicado",
                table: "MovimientosCaja",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoPago",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VentaId",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_AperturaCajaId_TipoPago",
                table: "MovimientosCaja",
                columns: new[] { "AperturaCajaId", "TipoPago" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_VentaId",
                table: "MovimientosCaja",
                column: "VentaId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimientosCaja_Ventas_VentaId",
                table: "MovimientosCaja",
                column: "VentaId",
                principalTable: "Ventas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosCaja_Ventas_VentaId",
                table: "MovimientosCaja");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosCaja_AperturaCajaId_TipoPago",
                table: "MovimientosCaja");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosCaja_VentaId",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "MedioPagoDetalle",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "RecargoDebitoAplicado",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "TipoPago",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "VentaId",
                table: "MovimientosCaja");
        }
    }
}

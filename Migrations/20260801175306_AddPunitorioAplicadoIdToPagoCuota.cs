using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddPunitorioAplicadoIdToPagoCuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PunitorioAplicadoId",
                table: "PagosCuota",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_PunitorioAplicadoId",
                table: "PagosCuota",
                column: "PunitorioAplicadoId");

            migrationBuilder.AddForeignKey(
                name: "FK_PagosCuota_PunitoriosAplicados_PunitorioAplicadoId",
                table: "PagosCuota",
                column: "PunitorioAplicadoId",
                principalTable: "PunitoriosAplicados",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagosCuota_PunitoriosAplicados_PunitorioAplicadoId",
                table: "PagosCuota");

            migrationBuilder.DropIndex(
                name: "IX_PagosCuota_PunitorioAplicadoId",
                table: "PagosCuota");

            migrationBuilder.DropColumn(
                name: "PunitorioAplicadoId",
                table: "PagosCuota");
        }
    }
}

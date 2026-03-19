using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfilCreditoPreferidoToCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PerfilCreditoPreferidoId",
                table: "Clientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_PerfilCreditoPreferidoId",
                table: "Clientes",
                column: "PerfilCreditoPreferidoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clientes_PerfilesCredito_PerfilCreditoPreferidoId",
                table: "Clientes",
                column: "PerfilCreditoPreferidoId",
                principalTable: "PerfilesCredito",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clientes_PerfilesCredito_PerfilCreditoPreferidoId",
                table: "Clientes");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_PerfilCreditoPreferidoId",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "PerfilCreditoPreferidoId",
                table: "Clientes");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditabilidadMetodoCalculoCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CuotasMaximasPermitidas",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CuotasMinimasPermitidas",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FuenteConfiguracionAplicada",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GastosAdministrativos",
                table: "Creditos",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MetodoCalculoAplicado",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PerfilCreditoAplicadoId",
                table: "Creditos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerfilCreditoAplicadoNombre",
                table: "Creditos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Creditos_PerfilCreditoAplicadoId",
                table: "Creditos",
                column: "PerfilCreditoAplicadoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Creditos_PerfilesCredito_PerfilCreditoAplicadoId",
                table: "Creditos",
                column: "PerfilCreditoAplicadoId",
                principalTable: "PerfilesCredito",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Creditos_PerfilesCredito_PerfilCreditoAplicadoId",
                table: "Creditos");

            migrationBuilder.DropIndex(
                name: "IX_Creditos_PerfilCreditoAplicadoId",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "CuotasMaximasPermitidas",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "CuotasMinimasPermitidas",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "FuenteConfiguracionAplicada",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "GastosAdministrativos",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "MetodoCalculoAplicado",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "PerfilCreditoAplicadoId",
                table: "Creditos");

            migrationBuilder.DropColumn(
                name: "PerfilCreditoAplicadoNombre",
                table: "Creditos");
        }
    }
}

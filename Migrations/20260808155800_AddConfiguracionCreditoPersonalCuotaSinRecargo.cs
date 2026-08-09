using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionCreditoPersonalCuotaSinRecargo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionCreditoPersonalCuotasSinRecargo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfiguracionCreditoPersonalCuotaId = table.Column<int>(type: "int", nullable: false),
                    NumeroCuota = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionCreditoPersonalCuotasSinRecargo", x => x.Id);
                    table.CheckConstraint("CK_ConfCreditoPersonalCuotaSinRecargo_NumeroCuota", "[NumeroCuota] >= 1 AND [NumeroCuota] <= 120");
                    table.ForeignKey(
                        name: "FK_ConfiguracionCreditoPersonalCuotasSinRecargo_ConfiguracionCreditoPersonalCuotas_ConfiguracionCreditoPersonalCuotaId",
                        column: x => x.ConfiguracionCreditoPersonalCuotaId,
                        principalTable: "ConfiguracionCreditoPersonalCuotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ConfCreditoPersonalCuotaSinRecargo_PlanNumero",
                table: "ConfiguracionCreditoPersonalCuotasSinRecargo",
                columns: new[] { "ConfiguracionCreditoPersonalCuotaId", "NumeroCuota" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionCreditoPersonalCuotasSinRecargo");
        }
    }
}

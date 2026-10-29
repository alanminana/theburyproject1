using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class BorradorCreditoVencimientoConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasVencimientoBorradorCredito",
                table: "ConfiguracionesCredito",
                type: "int",
                nullable: true);

            // La configuracion vigente queda con el plazo provisorio de 7 dias (null = vencimiento desactivado).
            migrationBuilder.Sql(
                "UPDATE [ConfiguracionesCredito] SET [DiasVencimientoBorradorCredito] = 7 WHERE [DiasVencimientoBorradorCredito] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiasVencimientoBorradorCredito",
                table: "ConfiguracionesCredito");
        }
    }
}

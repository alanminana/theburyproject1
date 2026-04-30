using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDetalleDescuentoGeneralProrrateado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoGeneralProrrateado",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalFinal",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalFinalIVA",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalFinalNeto",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE [VentaDetalles]
                SET
                    [DescuentoGeneralProrrateado] = 0,
                    [SubtotalFinalNeto] = [SubtotalNeto],
                    [SubtotalFinalIVA] = [SubtotalIVA],
                    [SubtotalFinal] = [Subtotal]
                WHERE [IsDeleted] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescuentoGeneralProrrateado",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "SubtotalFinal",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "SubtotalFinalIVA",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "SubtotalFinalNeto",
                table: "VentaDetalles");
        }
    }
}

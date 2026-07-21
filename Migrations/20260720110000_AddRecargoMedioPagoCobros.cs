using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddRecargoMedioPagoCobros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RecargoMedioPago",
                table: "Cuotas",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ImporteBase",
                table: "MovimientosCaja",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecargoMedioPago",
                table: "MovimientosCaja",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoMedioPago",
                table: "MovimientosCaja",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecargoMedioPago",
                table: "Cuotas");

            migrationBuilder.DropColumn(
                name: "ImporteBase",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "RecargoMedioPago",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "DescuentoMedioPago",
                table: "MovimientosCaja");
        }
    }
}

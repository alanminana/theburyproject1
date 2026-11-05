using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class H12CostoServiciosVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostoEstandarServicio",
                table: "VentaEnvio",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostoRealServicio",
                table: "VentaEnvio",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoCostoReal",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoCostoUnitarioEstandar",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Costo",
                table: "ServiciosVentaPrecios",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostoEstandarServicio",
                table: "VentaEnvio");

            migrationBuilder.DropColumn(
                name: "CostoRealServicio",
                table: "VentaEnvio");

            migrationBuilder.DropColumn(
                name: "ArmadoCostoReal",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "ArmadoCostoUnitarioEstandar",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "Costo",
                table: "ServiciosVentaPrecios");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddServiciosCotizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnvioIncluidoEnTotal",
                table: "Cotizaciones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ImporteArmados",
                table: "Cotizaciones",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TipoEnvio",
                table: "Cotizaciones",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoPrecioUnitario",
                table: "CotizacionDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoSubtotal",
                table: "CotizacionDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EntregaCajaCerrada",
                table: "CotizacionDetalles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TipoArmado",
                table: "CotizacionDetalles",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnvioIncluidoEnTotal",
                table: "Cotizaciones");

            migrationBuilder.DropColumn(
                name: "ImporteArmados",
                table: "Cotizaciones");

            migrationBuilder.DropColumn(
                name: "TipoEnvio",
                table: "Cotizaciones");

            migrationBuilder.DropColumn(
                name: "ArmadoPrecioUnitario",
                table: "CotizacionDetalles");

            migrationBuilder.DropColumn(
                name: "ArmadoSubtotal",
                table: "CotizacionDetalles");

            migrationBuilder.DropColumn(
                name: "EntregaCajaCerrada",
                table: "CotizacionDetalles");

            migrationBuilder.DropColumn(
                name: "TipoArmado",
                table: "CotizacionDetalles");
        }
    }
}

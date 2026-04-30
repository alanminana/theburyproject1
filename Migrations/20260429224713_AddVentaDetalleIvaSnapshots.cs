using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDetalleIvaSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlicuotaIVAId",
                table: "VentaDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlicuotaIVANombre",
                table: "VentaDetalles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IVAUnitario",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeIVA",
                table: "VentaDetalles",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioUnitarioNeto",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalIVA",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalNeto",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE [VentaDetalles]
                SET
                    [PorcentajeIVA] = 21,
                    [PrecioUnitarioNeto] = ROUND([PrecioUnitario] / 1.21, 2),
                    [IVAUnitario] = ROUND([PrecioUnitario] - ROUND([PrecioUnitario] / 1.21, 2), 2),
                    [SubtotalNeto] = ROUND([Subtotal] / 1.21, 2),
                    [SubtotalIVA] = ROUND([Subtotal] - ROUND([Subtotal] / 1.21, 2), 2),
                    [AlicuotaIVAId] = NULL,
                    [AlicuotaIVANombre] = N'IVA 21% (legacy)'
                WHERE [IsDeleted] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlicuotaIVAId",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "AlicuotaIVANombre",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "IVAUnitario",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "PorcentajeIVA",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "PrecioUnitarioNeto",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "SubtotalIVA",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "SubtotalNeto",
                table: "VentaDetalles");
        }
    }
}

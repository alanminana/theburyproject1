using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260430030000_AddVentaDetalleCostoSnapshots")]
    public partial class AddVentaDetalleCostoSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostoTotalAlMomento",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostoUnitarioAlMomento",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Backfill legacy estimado: no reconstruye costo histórico real.
            // Usa el PrecioCompra actual del producto para evitar nulos en reportes existentes.
            migrationBuilder.Sql("""
                UPDATE vd
                SET
                    vd.CostoUnitarioAlMomento = p.PrecioCompra,
                    vd.CostoTotalAlMomento = ROUND(p.PrecioCompra * vd.Cantidad, 2)
                FROM VentaDetalles vd
                INNER JOIN Productos p ON p.Id = vd.ProductoId
                WHERE vd.CostoUnitarioAlMomento = 0
                  AND vd.CostoTotalAlMomento = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostoTotalAlMomento",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "CostoUnitarioAlMomento",
                table: "VentaDetalles");
        }
    }
}

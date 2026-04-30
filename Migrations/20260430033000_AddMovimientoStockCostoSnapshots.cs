using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260430033000_AddMovimientoStockCostoSnapshots")]
    public partial class AddMovimientoStockCostoSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostoTotalAlMomento",
                table: "MovimientosStock",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostoUnitarioAlMomento",
                table: "MovimientosStock",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FuenteCosto",
                table: "MovimientosStock",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NoInformado");

            migrationBuilder.Sql("""
                UPDATE m
                SET
                    m.CostoUnitarioAlMomento = p.PrecioCompra,
                    m.CostoTotalAlMomento = ROUND(p.PrecioCompra * ABS(m.Cantidad), 2),
                    m.FuenteCosto = 'Legacy'
                FROM MovimientosStock m
                INNER JOIN Productos p ON p.Id = m.ProductoId;

                UPDATE m
                SET m.FuenteCosto = 'NoInformado'
                FROM MovimientosStock m
                LEFT JOIN Productos p ON p.Id = m.ProductoId
                WHERE p.Id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostoTotalAlMomento",
                table: "MovimientosStock");

            migrationBuilder.DropColumn(
                name: "CostoUnitarioAlMomento",
                table: "MovimientosStock");

            migrationBuilder.DropColumn(
                name: "FuenteCosto",
                table: "MovimientosStock");
        }
    }
}

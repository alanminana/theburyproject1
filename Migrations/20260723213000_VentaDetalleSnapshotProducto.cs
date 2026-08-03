using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class VentaDetalleSnapshotProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Micro-lote 5 — snapshot histórico de la identidad del producto en cada línea de venta.
            // Nullable: las líneas nuevas siempre quedan pobladas server-side; el nullable existe sólo
            // para compatibilidad con filas anteriores a esta migración (backfill + fallback de lectura).
            migrationBuilder.AddColumn<string>(
                name: "ProductoNombreAlMomento",
                table: "VentaDetalles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductoCodigoAlMomento",
                table: "VentaDetalles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Backfill de filas existentes desde el producto relacionado. IMPORTANTE: este valor
            // representa el nombre/código ACTUAL conocido del producto, no necesariamente el original
            // vigente al momento de la venta (para ventas anteriores a esta migración esa información
            // histórica no existía). Si el producto relacionado ya no existe, se usa "Producto #<id>"
            // como último recurso estable para el nombre y NULL para el código (no se inventan datos).
            migrationBuilder.Sql(@"
                UPDATE vd
                SET
                    vd.ProductoNombreAlMomento = COALESCE(
                        NULLIF(LTRIM(RTRIM(p.Nombre)), ''),
                        N'Producto #' + CAST(vd.ProductoId AS nvarchar(12))),
                    vd.ProductoCodigoAlMomento = NULLIF(LTRIM(RTRIM(p.Codigo)), '')
                FROM [VentaDetalles] vd
                LEFT JOIN [Productos] p ON p.[Id] = vd.[ProductoId]
                WHERE vd.[ProductoNombreAlMomento] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductoNombreAlMomento",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "ProductoCodigoAlMomento",
                table: "VentaDetalles");
        }
    }
}

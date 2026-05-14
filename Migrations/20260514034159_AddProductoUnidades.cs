using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoUnidades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductoUnidades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    CodigoInternoUnidad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NumeroSerie = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    UbicacionActual = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaIngreso = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VentaDetalleId = table.Column<int>(type: "int", nullable: true),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    FechaVenta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoUnidades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductoUnidades_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductoUnidades_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductoUnidades_VentaDetalles_VentaDetalleId",
                        column: x => x.VentaDetalleId,
                        principalTable: "VentaDetalles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductoUnidadMovimientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoUnidadId = table.Column<int>(type: "int", nullable: false),
                    EstadoAnterior = table.Column<int>(type: "int", nullable: false),
                    EstadoNuevo = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrigenReferencia = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UsuarioResponsable = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaCambio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoUnidadMovimientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductoUnidadMovimientos_ProductoUnidades_ProductoUnidadId",
                        column: x => x.ProductoUnidadId,
                        principalTable: "ProductoUnidades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidades_ClienteId",
                table: "ProductoUnidades",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidades_Estado",
                table: "ProductoUnidades",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidades_ProductoId",
                table: "ProductoUnidades",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidades_VentaDetalleId",
                table: "ProductoUnidades",
                column: "VentaDetalleId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoUnidades_CodigoInterno",
                table: "ProductoUnidades",
                columns: new[] { "ProductoId", "CodigoInternoUnidad" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ProductoUnidades_NumeroSerie",
                table: "ProductoUnidades",
                columns: new[] { "ProductoId", "NumeroSerie" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [NumeroSerie] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidadMovimientos_EstadoNuevo",
                table: "ProductoUnidadMovimientos",
                column: "EstadoNuevo");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoUnidadMovimientos_UnidadFecha",
                table: "ProductoUnidadMovimientos",
                columns: new[] { "ProductoUnidadId", "FechaCambio" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductoUnidadMovimientos");

            migrationBuilder.DropTable(
                name: "ProductoUnidades");
        }
    }
}

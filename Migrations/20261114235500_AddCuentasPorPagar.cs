using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddCuentasPorPagar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprobantesProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProveedorId = table.Column<int>(type: "int", nullable: false),
                    OrdenCompraId = table.Column<int>(type: "int", nullable: true),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    NumeroComprobante = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaVencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImporteNeto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ImporteIva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ImporteTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SaldoPendiente = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    AnuladoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprobantesProveedor_OrdenesCompra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalTable: "OrdenesCompra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComprobantesProveedor_Proveedores_ProveedorId",
                        column: x => x.ProveedorId,
                        principalTable: "Proveedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PagosProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProveedorId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Medio = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeId = table.Column<int>(type: "int", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Anulado = table.Column<bool>(type: "bit", nullable: false),
                    AnuladoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosProveedor_Cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "Cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosProveedor_Proveedores_ProveedorId",
                        column: x => x.ProveedorId,
                        principalTable: "Proveedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImputacionesNotaCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotaCreditoId = table.Column<int>(type: "int", nullable: false),
                    ComprobanteId = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Anulada = table.Column<bool>(type: "bit", nullable: false),
                    AnuladaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImputacionesNotaCredito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImputacionesNotaCredito_ComprobantesProveedor_ComprobanteId",
                        column: x => x.ComprobanteId,
                        principalTable: "ComprobantesProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImputacionesNotaCredito_ComprobantesProveedor_NotaCreditoId",
                        column: x => x.NotaCreditoId,
                        principalTable: "ComprobantesProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PagoProveedorImputaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PagoProveedorId = table.Column<int>(type: "int", nullable: false),
                    ComprobanteProveedorId = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagoProveedorImputaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagoProveedorImputaciones_ComprobantesProveedor_ComprobanteProveedorId",
                        column: x => x.ComprobanteProveedorId,
                        principalTable: "ComprobantesProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagoProveedorImputaciones_PagosProveedor_PagoProveedorId",
                        column: x => x.PagoProveedorId,
                        principalTable: "PagosProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesProveedor_FechaVencimiento",
                table: "ComprobantesProveedor",
                column: "FechaVencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesProveedor_OrdenCompraId",
                table: "ComprobantesProveedor",
                column: "OrdenCompraId");

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesProveedor_ProveedorId_Estado",
                table: "ComprobantesProveedor",
                columns: new[] { "ProveedorId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesProveedor_ProveedorId_Tipo_NumeroComprobante",
                table: "ComprobantesProveedor",
                columns: new[] { "ProveedorId", "Tipo", "NumeroComprobante" },
                unique: true,
                filter: "Estado <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_ImputacionesNotaCredito_ComprobanteId",
                table: "ImputacionesNotaCredito",
                column: "ComprobanteId");

            migrationBuilder.CreateIndex(
                name: "IX_ImputacionesNotaCredito_NotaCreditoId_ClaveIdempotencia",
                table: "ImputacionesNotaCredito",
                columns: new[] { "NotaCreditoId", "ClaveIdempotencia" });

            migrationBuilder.CreateIndex(
                name: "IX_PagoProveedorImputaciones_ComprobanteProveedorId",
                table: "PagoProveedorImputaciones",
                column: "ComprobanteProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_PagoProveedorImputaciones_PagoProveedorId",
                table: "PagoProveedorImputaciones",
                column: "PagoProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_ChequeId",
                table: "PagosProveedor",
                column: "ChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_ProveedorId_ClaveIdempotencia",
                table: "PagosProveedor",
                columns: new[] { "ProveedorId", "ClaveIdempotencia" },
                unique: true,
                filter: "ClaveIdempotencia IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_ProveedorId_Fecha",
                table: "PagosProveedor",
                columns: new[] { "ProveedorId", "Fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImputacionesNotaCredito");

            migrationBuilder.DropTable(
                name: "PagoProveedorImputaciones");

            migrationBuilder.DropTable(
                name: "ComprobantesProveedor");

            migrationBuilder.DropTable(
                name: "PagosProveedor");
        }
    }
}

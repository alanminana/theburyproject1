using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class DropVentaCreditoCuotasLegacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarda anti pérdida de datos: VentaCreditoCuotas y Ventas.DatosCreditoPersonallJson eran
            // el flujo legacy de crédito personal por JSON (reemplazado por Credito + Credito.Cuotas) y
            // se verificaron vacíos en la base de desarrollo. Si alguna otra base tiene datos, la
            // migración aborta en vez de borrarlos en silencio: migrarlos a Credito antes de reintentar.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [VentaCreditoCuotas]) " +
                "THROW 51000, 'DropVentaCreditoCuotasLegacy: VentaCreditoCuotas contiene filas; no se elimina para evitar perder datos.', 1;");

            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [Ventas] WHERE [DatosCreditoPersonallJson] IS NOT NULL) " +
                "THROW 51000, 'DropVentaCreditoCuotasLegacy: hay Ventas con DatosCreditoPersonallJson; no se elimina para evitar perder datos.', 1;");

            migrationBuilder.DropTable(
                name: "VentaCreditoCuotas");

            migrationBuilder.DropColumn(
                name: "DatosCreditoPersonallJson",
                table: "Ventas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DatosCreditoPersonallJson",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VentaCreditoCuotas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditoId = table.Column<int>(type: "int", nullable: false),
                    VentaId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaVencimiento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoPagado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NumeroCuota = table.Column<int>(type: "int", nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Pagada = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Saldo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VentaCreditoCuotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VentaCreditoCuotas_Creditos_CreditoId",
                        column: x => x.CreditoId,
                        principalTable: "Creditos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VentaCreditoCuotas_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VentaCreditoCuotas_CreditoId",
                table: "VentaCreditoCuotas",
                column: "CreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_VentaCreditoCuotas_FechaVencimiento",
                table: "VentaCreditoCuotas",
                column: "FechaVencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_VentaCreditoCuotas_Pagada",
                table: "VentaCreditoCuotas",
                column: "Pagada");

            migrationBuilder.CreateIndex(
                name: "IX_VentaCreditoCuotas_VentaId_NumeroCuota",
                table: "VentaCreditoCuotas",
                columns: new[] { "VentaId", "NumeroCuota" });
        }
    }
}

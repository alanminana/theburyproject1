using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPagoCuotaLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PagosCuota",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CuotaId = table.Column<int>(type: "int", nullable: false),
                    FechaPagoComercial = table.Column<DateOnly>(type: "date", nullable: false),
                    ImporteTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ImporteAplicadoCuota = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ImporteAplicadoPunitorio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MovimientoCajaId = table.Column<int>(type: "int", nullable: true),
                    MedioPago = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Origen = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    HistorialCompleto = table.Column<bool>(type: "bit", nullable: false),
                    MotivoIncompleto = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PagoCuotaOrigenId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosCuota", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosCuota_Cuotas_CuotaId",
                        column: x => x.CuotaId,
                        principalTable: "Cuotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosCuota_MovimientosCaja_MovimientoCajaId",
                        column: x => x.MovimientoCajaId,
                        principalTable: "MovimientosCaja",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagosCuota_PagosCuota_PagoCuotaOrigenId",
                        column: x => x.PagoCuotaOrigenId,
                        principalTable: "PagosCuota",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_CuotaId",
                table: "PagosCuota",
                column: "CuotaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_FechaPagoComercial",
                table: "PagosCuota",
                column: "FechaPagoComercial");

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_MovimientoCajaId",
                table: "PagosCuota",
                column: "MovimientoCajaId",
                unique: true,
                filter: "[MovimientoCajaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_PagoCuotaOrigenId",
                table: "PagosCuota",
                column: "PagoCuotaOrigenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PagosCuota");
        }
    }
}

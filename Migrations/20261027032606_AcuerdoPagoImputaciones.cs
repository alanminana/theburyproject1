using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AcuerdoPagoImputaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperacionExternaId",
                table: "PagosCuota",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcuerdoPagoImputaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcuerdoPagoId = table.Column<int>(type: "int", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CuotaAcuerdoId = table.Column<int>(type: "int", nullable: true),
                    CuotaCreditoId = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MedioPago = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Comprobante = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    PagoCuotaId = table.Column<int>(type: "int", nullable: true),
                    MontoAplicado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuerdoPagoImputaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuerdoPagoImputaciones_AcuerdosPago_AcuerdoPagoId",
                        column: x => x.AcuerdoPagoId,
                        principalTable: "AcuerdosPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagosCuota_OperacionExternaId",
                table: "PagosCuota",
                column: "OperacionExternaId",
                unique: true,
                filter: "[OperacionExternaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdoPagoImputaciones_AcuerdoPagoId_Estado",
                table: "AcuerdoPagoImputaciones",
                columns: new[] { "AcuerdoPagoId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdoPagoImputaciones_OperacionId",
                table: "AcuerdoPagoImputaciones",
                column: "OperacionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdoPagoImputaciones_PagoCuotaId",
                table: "AcuerdoPagoImputaciones",
                column: "PagoCuotaId",
                unique: true,
                filter: "[PagoCuotaId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcuerdoPagoImputaciones");

            migrationBuilder.DropIndex(
                name: "IX_PagosCuota_OperacionExternaId",
                table: "PagosCuota");

            migrationBuilder.DropColumn(
                name: "OperacionExternaId",
                table: "PagosCuota");
        }
    }
}

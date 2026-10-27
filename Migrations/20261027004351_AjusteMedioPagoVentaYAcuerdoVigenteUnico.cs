using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AjusteMedioPagoVentaYAcuerdoVigenteUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcuerdosPago_CreditoId",
                table: "AcuerdosPago");

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteMedioPagoMonto",
                table: "Ventas",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteMedioPagoPorcentaje",
                table: "Ventas",
                type: "decimal(9,4)",
                nullable: true);

            // El esquema anterior permitía varios acuerdos vigentes por crédito: antes de crear el índice único se
            // concilian (queda el Activo más nuevo, o el Borrador más nuevo; el resto pasa a Cancelado conservando su historial).
            migrationBuilder.Sql(@"
WITH r AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY CreditoId ORDER BY CASE Estado WHEN 2 THEN 0 ELSE 1 END, Id DESC) AS rn
      FROM AcuerdosPago
     WHERE IsDeleted = 0 AND Estado IN (1, 2))
UPDATE a SET Estado = 5, UpdatedAt = SYSUTCDATETIME()
  FROM AcuerdosPago a
  JOIN r ON r.Id = a.Id
 WHERE r.rn > 1;");

            migrationBuilder.CreateIndex(
                name: "UX_AcuerdosPago_CreditoVigente",
                table: "AcuerdosPago",
                column: "CreditoId",
                unique: true,
                filter: "IsDeleted = 0 AND Estado IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AcuerdosPago_CreditoVigente",
                table: "AcuerdosPago");

            migrationBuilder.DropColumn(
                name: "AjusteMedioPagoMonto",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "AjusteMedioPagoPorcentaje",
                table: "Ventas");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_CreditoId",
                table: "AcuerdosPago",
                column: "CreditoId");
        }
    }
}

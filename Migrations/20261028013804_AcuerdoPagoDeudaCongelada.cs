using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AcuerdoPagoDeudaCongelada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Un acuerdo Activo anterior no tiene deuda congelada y no se reconstruye desde el saldo actual (no acredita su composición
            // original): la actualización se frena hasta que se cancelen o completen. Cumplidos, Incumplidos, Cancelados y borradores
            // no necesitan nada: un borrador congela al activarse y los cerrados quedan fuera de la garantía nueva.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [AcuerdosPago] WHERE [Estado] = 2 AND [IsDeleted] = 0) THROW 50001, 'Hay acuerdos de pago Activos sin deuda congelada. Cancelalos o completalos antes de aplicar esta migracion.', 1;");

            migrationBuilder.AlterColumn<int>(
                name: "AlertaCobranzaId",
                table: "AcuerdosPago",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "AcuerdoPagoCuotasCubiertas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcuerdoPagoId = table.Column<int>(type: "int", nullable: false),
                    CuotaId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    CapitalCubierto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PunitorioCubierto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuerdoPagoCuotasCubiertas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuerdoPagoCuotasCubiertas_AcuerdosPago_AcuerdoPagoId",
                        column: x => x.AcuerdoPagoId,
                        principalTable: "AcuerdosPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcuerdoPagoCuotasCubiertas_Cuotas_CuotaId",
                        column: x => x.CuotaId,
                        principalTable: "Cuotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdoPagoCuotasCubiertas_AcuerdoPagoId_CuotaId",
                table: "AcuerdoPagoCuotasCubiertas",
                columns: new[] { "AcuerdoPagoId", "CuotaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdoPagoCuotasCubiertas_CuotaId",
                table: "AcuerdoPagoCuotasCubiertas",
                column: "CuotaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se inventa una alerta para volver a NOT NULL: si hay acuerdos sin alerta (deuda solo punitoria), la reversión se frena.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [AcuerdosPago] WHERE [AlertaCobranzaId] IS NULL) THROW 50000, 'No se puede revertir: hay acuerdos de pago sin alerta de cobranza.', 1;");

            migrationBuilder.DropTable(
                name: "AcuerdoPagoCuotasCubiertas");

            migrationBuilder.AlterColumn<int>(
                name: "AlertaCobranzaId",
                table: "AcuerdosPago",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPunitorioAplicado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PunitoriosAplicados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CuotaId = table.Column<int>(type: "int", nullable: false),
                    FechaCalculo = table.Column<DateOnly>(type: "date", nullable: false),
                    SaldoBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiasComputados = table.Column<int>(type: "int", nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    ConfiguracionPunitorioId = table.Column<int>(type: "int", nullable: true),
                    Porcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    PeriodoDias = table.Column<int>(type: "int", nullable: true),
                    DiasGracia = table.Column<int>(type: "int", nullable: true),
                    DesgloseSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MotivoAplicacion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaAplicacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioAplicacion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioAnulacion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PunitoriosAplicados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PunitoriosAplicados_ConfiguracionesPunitorio_ConfiguracionPunitorioId",
                        column: x => x.ConfiguracionPunitorioId,
                        principalTable: "ConfiguracionesPunitorio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PunitoriosAplicados_Cuotas_CuotaId",
                        column: x => x.CuotaId,
                        principalTable: "Cuotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PunitoriosAplicados_ConfiguracionPunitorioId",
                table: "PunitoriosAplicados",
                column: "ConfiguracionPunitorioId");

            migrationBuilder.CreateIndex(
                name: "IX_PunitoriosAplicados_CuotaId_UnaActivaPorCuota",
                table: "PunitoriosAplicados",
                column: "CuotaId",
                unique: true,
                filter: "[Estado] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PunitoriosAplicados");
        }
    }
}

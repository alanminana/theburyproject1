using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteCreditoConfiguracionEtapa2LimiteEfectivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExcepcionAlMomento",
                table: "Ventas",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LimiteAplicado",
                table: "Ventas",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverrideAlMomento",
                table: "Ventas",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PresetIdAlMomento",
                table: "Ventas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntajeAlMomento",
                table: "Ventas",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientesCreditoConfiguraciones",
                columns: table => new
                {
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    CreditoPresetId = table.Column<int>(type: "int", nullable: true),
                    LimiteOverride = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExcepcionDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExcepcionDesde = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExcepcionHasta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoExcepcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AprobadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AprobadoEnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoOverride = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OverrideAprobadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OverrideAprobadoEnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientesCreditoConfiguraciones", x => x.ClienteId);
                    table.CheckConstraint("CK_ClientesCreditoConfiguraciones_ExcepcionVigencia", "[ExcepcionDesde] IS NULL OR [ExcepcionHasta] IS NULL OR [ExcepcionDesde] <= [ExcepcionHasta]");
                    table.CheckConstraint("CK_ClientesCreditoConfiguraciones_MontosNoNegativos", "([LimiteOverride] IS NULL OR [LimiteOverride] >= 0) AND ([ExcepcionDelta] IS NULL OR [ExcepcionDelta] >= 0)");
                    table.ForeignKey(
                        name: "FK_ClientesCreditoConfiguraciones_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientesCreditoConfiguraciones_PuntajeCreditoLimites_CreditoPresetId",
                        column: x => x.CreditoPresetId,
                        principalTable: "PuntajeCreditoLimites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ClientesPuntajeHistorial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Puntaje = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    NivelRiesgo = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Observacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegistradoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientesPuntajeHistorial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientesPuntajeHistorial_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_PresetIdAlMomento",
                table: "Ventas",
                column: "PresetIdAlMomento");

            migrationBuilder.CreateIndex(
                name: "IX_ClientesCreditoConfiguraciones_CreditoPresetId",
                table: "ClientesCreditoConfiguraciones",
                column: "CreditoPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientesCreditoConfiguraciones_ExcepcionHasta",
                table: "ClientesCreditoConfiguraciones",
                column: "ExcepcionHasta");

            migrationBuilder.CreateIndex(
                name: "IX_ClientesPuntajeHistorial_ClienteId",
                table: "ClientesPuntajeHistorial",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientesPuntajeHistorial_ClienteId_Fecha",
                table: "ClientesPuntajeHistorial",
                columns: new[] { "ClienteId", "Fecha" });

            migrationBuilder.AddForeignKey(
                name: "FK_Ventas_PuntajeCreditoLimites_PresetIdAlMomento",
                table: "Ventas",
                column: "PresetIdAlMomento",
                principalTable: "PuntajeCreditoLimites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ventas_PuntajeCreditoLimites_PresetIdAlMomento",
                table: "Ventas");

            migrationBuilder.DropTable(
                name: "ClientesCreditoConfiguraciones");

            migrationBuilder.DropTable(
                name: "ClientesPuntajeHistorial");

            migrationBuilder.DropIndex(
                name: "IX_Ventas_PresetIdAlMomento",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "ExcepcionAlMomento",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "LimiteAplicado",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "OverrideAlMomento",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "PresetIdAlMomento",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "PuntajeAlMomento",
                table: "Ventas");
        }
    }
}

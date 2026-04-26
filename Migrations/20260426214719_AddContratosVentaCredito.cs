using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddContratosVentaCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlantillasContratoCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    NombreVendedor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DomicilioVendedor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DniVendedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CuitVendedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CiudadFirma = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Jurisdiccion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InteresMoraDiarioPorcentaje = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    TextoContrato = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextoPagare = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VigenteDesde = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VigenteHasta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantillasContratoCredito", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContratosVentaCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VentaId = table.Column<int>(type: "int", nullable: false),
                    CreditoId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    PlantillaContratoCreditoId = table.Column<int>(type: "int", nullable: false),
                    NumeroContrato = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NumeroPagare = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FechaGeneracionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioGeneracion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EstadoDocumento = table.Column<int>(type: "int", nullable: false),
                    RutaArchivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NombreArchivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TextoContratoSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextoPagareSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DatosSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaImpresionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContratosVentaCredito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContratosVentaCredito_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosVentaCredito_Creditos_CreditoId",
                        column: x => x.CreditoId,
                        principalTable: "Creditos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosVentaCredito_PlantillasContratoCredito_PlantillaContratoCreditoId",
                        column: x => x.PlantillaContratoCreditoId,
                        principalTable: "PlantillasContratoCredito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContratosVentaCredito_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_ClienteId",
                table: "ContratosVentaCredito",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_CreditoId",
                table: "ContratosVentaCredito",
                column: "CreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_FechaGeneracionUtc",
                table: "ContratosVentaCredito",
                column: "FechaGeneracionUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_NumeroContrato",
                table: "ContratosVentaCredito",
                column: "NumeroContrato",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_NumeroPagare",
                table: "ContratosVentaCredito",
                column: "NumeroPagare",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_PlantillaContratoCreditoId",
                table: "ContratosVentaCredito",
                column: "PlantillaContratoCreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosVentaCredito_VentaId",
                table: "ContratosVentaCredito",
                column: "VentaId",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasContratoCredito_Activa",
                table: "PlantillasContratoCredito",
                column: "Activa");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasContratoCredito_VigenteDesde",
                table: "PlantillasContratoCredito",
                column: "VigenteDesde");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasContratoCredito_VigenteHasta",
                table: "PlantillasContratoCredito",
                column: "VigenteHasta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContratosVentaCredito");

            migrationBuilder.DropTable(
                name: "PlantillasContratoCredito");
        }
    }
}

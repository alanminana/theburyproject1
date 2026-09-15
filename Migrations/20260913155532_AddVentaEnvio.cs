using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TieneEnvio",
                table: "Cotizaciones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VentaEnvio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VentaId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Destinatario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Domicilio = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Localidad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Provincia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CodigoPostal = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Transportista = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    NumeroSeguimiento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CostoEnvio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FechaProgramada = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaDespacho = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaEntregaReal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoNoEntrega = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_VentaEnvio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VentaEnvio_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VentaEnvio_Estado",
                table: "VentaEnvio",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_VentaEnvio_VentaId",
                table: "VentaEnvio",
                column: "VentaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VentaEnvio");

            migrationBuilder.DropColumn(
                name: "TieneEnvio",
                table: "Cotizaciones");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCambioPrecioEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CambioPrecioEventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Alcance = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValorPorcentaje = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FiltrosJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CantidadProductos = table.Column<int>(type: "int", nullable: false),
                    RevertidoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevertidoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambioPrecioEventos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CambioPrecioDetalles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventoId = table.Column<int>(type: "int", nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    PrecioAnterior = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrecioNuevo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambioPrecioDetalles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CambioPrecioDetalles_CambioPrecioEventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "CambioPrecioEventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CambioPrecioDetalles_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CambioPrecioDetalles_EventoId",
                table: "CambioPrecioDetalles",
                column: "EventoId");

            migrationBuilder.CreateIndex(
                name: "IX_CambioPrecioDetalles_ProductoId",
                table: "CambioPrecioDetalles",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_CambioPrecioEventos_Fecha",
                table: "CambioPrecioEventos",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_CambioPrecioEventos_RevertidoEn",
                table: "CambioPrecioEventos",
                column: "RevertidoEn");

            migrationBuilder.CreateIndex(
                name: "IX_CambioPrecioEventos_Usuario",
                table: "CambioPrecioEventos",
                column: "Usuario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CambioPrecioDetalles");

            migrationBuilder.DropTable(
                name: "CambioPrecioEventos");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class H4E2NotaCreditoCanje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotaCreditoId",
                table: "Ventas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotasCreditoUso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotaCreditoId = table.Column<int>(type: "int", nullable: false),
                    VentaId = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaUso = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FechaReversion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoReversion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotasCreditoUso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotasCreditoUso_NotasCredito_NotaCreditoId",
                        column: x => x.NotaCreditoId,
                        principalTable: "NotasCredito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotasCreditoUso_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ventas_NotaCreditoId",
                table: "Ventas",
                column: "NotaCreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasCreditoUso_NotaCreditoId",
                table: "NotasCreditoUso",
                column: "NotaCreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasCreditoUso_VentaId",
                table: "NotasCreditoUso",
                column: "VentaId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Ventas_NotasCredito_NotaCreditoId",
                table: "Ventas",
                column: "NotaCreditoId",
                principalTable: "NotasCredito",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ventas_NotasCredito_NotaCreditoId",
                table: "Ventas");

            migrationBuilder.DropTable(
                name: "NotasCreditoUso");

            migrationBuilder.DropIndex(
                name: "IX_Ventas_NotaCreditoId",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "NotaCreditoId",
                table: "Ventas");
        }
    }
}

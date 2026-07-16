using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionCreditoPersonalCuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionCreditoPersonalCuotas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: false),
                    TasaMensual = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false, defaultValue: 0m),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UsuarioActualizacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionCreditoPersonalCuotas", x => x.Id);
                    table.CheckConstraint("CK_ConfCreditoPersonalCuota_Cuotas", "[CantidadCuotas] >= 1 AND [CantidadCuotas] <= 120");
                    table.CheckConstraint("CK_ConfCreditoPersonalCuota_Tasa", "[TasaMensual] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionCreditoPersonalCuotas_CantidadCuotas",
                table: "ConfiguracionCreditoPersonalCuotas",
                column: "CantidadCuotas",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionCreditoPersonalCuotas");
        }
    }
}

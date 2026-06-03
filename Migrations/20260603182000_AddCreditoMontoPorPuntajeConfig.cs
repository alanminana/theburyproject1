using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditoMontoPorPuntajeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionCreditoMontosPorPuntaje",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Puntaje = table.Column<int>(type: "int", nullable: false),
                    MontoMaximoFinanciable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    RequiereAnalisis = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UsuarioActualizacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionCreditoMontosPorPuntaje", x => x.Id);
                    table.CheckConstraint("CK_ConfCreditoMontoPorPuntaje_Monto", "[MontoMaximoFinanciable] >= 0");
                    table.CheckConstraint("CK_ConfCreditoMontoPorPuntaje_Puntaje", "[Puntaje] >= 0 AND [Puntaje] <= 10");
                });

            migrationBuilder.InsertData(
                table: "ConfiguracionCreditoMontosPorPuntaje",
                columns: new[] { "Id", "Activo", "FechaActualizacion", "MontoMaximoFinanciable", "Orden", "Puntaje", "RequiereAnalisis", "UsuarioActualizacion" },
                values: new object[,]
                {
                    { 101, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 0,  0,  true,  "System" },
                    { 102, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 1,  1,  true,  "System" },
                    { 103, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 2,  2,  false, "System" },
                    { 104, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 3,  3,  false, "System" },
                    { 105, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 4,  4,  false, "System" },
                    { 106, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 5,  5,  false, "System" },
                    { 107, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 6,  6,  false, "System" },
                    { 108, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 7,  7,  false, "System" },
                    { 109, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 8,  8,  false, "System" },
                    { 110, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 9,  9,  false, "System" },
                    { 111, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m, 10, 10, false, "System" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionCreditoMontosPorPuntaje_Puntaje",
                table: "ConfiguracionCreditoMontosPorPuntaje",
                column: "Puntaje",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionCreditoMontosPorPuntaje");
        }
    }
}

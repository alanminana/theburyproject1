using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPuntajeCreditoLimites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PuntajeCreditoLimites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Puntaje = table.Column<int>(type: "int", nullable: false),
                    LimiteMonto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UsuarioActualizacion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuntajeCreditoLimites", x => x.Id);
                    table.CheckConstraint("CK_PuntajeCreditoLimites_Puntaje", "[Puntaje] >= 1 AND [Puntaje] <= 5");
                });

            migrationBuilder.InsertData(
                table: "PuntajeCreditoLimites",
                columns: new[] { "Id", "Activo", "FechaActualizacion", "Puntaje", "UsuarioActualizacion" },
                values: new object[,]
                {
                    { 1, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "System" },
                    { 2, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, "System" },
                    { 3, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3, "System" },
                    { 4, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4, "System" },
                    { 5, true, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 5, "System" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PuntajeCreditoLimites_Puntaje",
                table: "PuntajeCreditoLimites",
                column: "Puntaje",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PuntajeCreditoLimites");
        }
    }
}

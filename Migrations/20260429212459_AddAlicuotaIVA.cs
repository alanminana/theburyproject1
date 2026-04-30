using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddAlicuotaIVA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PorcentajeIVA",
                table: "Productos",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<int>(
                name: "AlicuotaIVAId",
                table: "Productos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlicuotaIVAId",
                table: "Categorias",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlicuotasIVA",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Porcentaje = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EsPredeterminada = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlicuotasIVA", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AlicuotasIVA",
                columns: new[] { "Id", "Activa", "Codigo", "CreatedAt", "CreatedBy", "EsPredeterminada", "IsDeleted", "Nombre", "Porcentaje", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1, true, "IVA_21", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", true, false, "IVA 21%", 21m, null, null });

            migrationBuilder.InsertData(
                table: "AlicuotasIVA",
                columns: new[] { "Id", "Activa", "Codigo", "CreatedAt", "CreatedBy", "IsDeleted", "Nombre", "Porcentaje", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 2, true, "IVA_10_5", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", false, "IVA 10.5%", 10.5m, null, null },
                    { 3, true, "IVA_27", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", false, "IVA 27%", 27m, null, null },
                    { 4, true, "IVA_EXENTO", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", false, "Exento 0%", 0m, null, null }
                });

            migrationBuilder.UpdateData(
                table: "Categorias",
                keyColumn: "Id",
                keyValue: 1,
                column: "AlicuotaIVAId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Categorias",
                keyColumn: "Id",
                keyValue: 2,
                column: "AlicuotaIVAId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Productos_AlicuotaIVAId",
                table: "Productos",
                column: "AlicuotaIVAId");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_AlicuotaIVAId",
                table: "Categorias",
                column: "AlicuotaIVAId");

            migrationBuilder.CreateIndex(
                name: "IX_AlicuotasIVA_Activa",
                table: "AlicuotasIVA",
                column: "Activa");

            migrationBuilder.CreateIndex(
                name: "IX_AlicuotasIVA_Codigo",
                table: "AlicuotasIVA",
                column: "Codigo",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AlicuotasIVA_EsPredeterminada",
                table: "AlicuotasIVA",
                column: "EsPredeterminada");

            migrationBuilder.AddForeignKey(
                name: "FK_Categorias_AlicuotasIVA_AlicuotaIVAId",
                table: "Categorias",
                column: "AlicuotaIVAId",
                principalTable: "AlicuotasIVA",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Productos_AlicuotasIVA_AlicuotaIVAId",
                table: "Productos",
                column: "AlicuotaIVAId",
                principalTable: "AlicuotasIVA",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categorias_AlicuotasIVA_AlicuotaIVAId",
                table: "Categorias");

            migrationBuilder.DropForeignKey(
                name: "FK_Productos_AlicuotasIVA_AlicuotaIVAId",
                table: "Productos");

            migrationBuilder.DropTable(
                name: "AlicuotasIVA");

            migrationBuilder.DropIndex(
                name: "IX_Productos_AlicuotaIVAId",
                table: "Productos");

            migrationBuilder.DropIndex(
                name: "IX_Categorias_AlicuotaIVAId",
                table: "Categorias");

            migrationBuilder.DropColumn(
                name: "AlicuotaIVAId",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "AlicuotaIVAId",
                table: "Categorias");

            migrationBuilder.AlterColumn<decimal>(
                name: "PorcentajeIVA",
                table: "Productos",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);
        }
    }
}

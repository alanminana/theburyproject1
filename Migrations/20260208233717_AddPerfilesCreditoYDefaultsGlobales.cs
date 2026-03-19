using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfilesCreditoYDefaultsGlobales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TasaInteresMensualCreditoPersonal",
                table: "ConfiguracionesPago",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GastosAdministrativosDefaultCreditoPersonal",
                table: "ConfiguracionesPago",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxCuotasDefaultCreditoPersonal",
                table: "ConfiguracionesPago",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinCuotasDefaultCreditoPersonal",
                table: "ConfiguracionesPago",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PerfilesCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TasaMensual = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    GastosAdministrativos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinCuotas = table.Column<int>(type: "int", nullable: false),
                    MaxCuotas = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerfilesCredito", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerfilesCredito_Nombre",
                table: "PerfilesCredito",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerfilesCredito_Orden",
                table: "PerfilesCredito",
                column: "Orden");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerfilesCredito");

            migrationBuilder.DropColumn(
                name: "GastosAdministrativosDefaultCreditoPersonal",
                table: "ConfiguracionesPago");

            migrationBuilder.DropColumn(
                name: "MaxCuotasDefaultCreditoPersonal",
                table: "ConfiguracionesPago");

            migrationBuilder.DropColumn(
                name: "MinCuotasDefaultCreditoPersonal",
                table: "ConfiguracionesPago");

            migrationBuilder.AlterColumn<decimal>(
                name: "TasaInteresMensualCreditoPersonal",
                table: "ConfiguracionesPago",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldPrecision: 8,
                oldScale: 4,
                oldNullable: true);
        }
    }
}

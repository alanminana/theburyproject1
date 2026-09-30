using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddServiciosVentaEnvioArmado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncluidoEnTotal",
                table: "VentaEnvio",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TipoEnvio",
                table: "VentaEnvio",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoPrecioUnitario",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmadoSubtotal",
                table: "VentaDetalles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EntregaCajaCerrada",
                table: "VentaDetalles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TipoArmado",
                table: "VentaDetalles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiciosVentaPrecios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiciosVentaPrecios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiciosVentaPrecios_Tipo",
                table: "ServiciosVentaPrecios",
                column: "Tipo",
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiciosVentaPrecios");

            migrationBuilder.DropColumn(
                name: "IncluidoEnTotal",
                table: "VentaEnvio");

            migrationBuilder.DropColumn(
                name: "TipoEnvio",
                table: "VentaEnvio");

            migrationBuilder.DropColumn(
                name: "ArmadoPrecioUnitario",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "ArmadoSubtotal",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "EntregaCajaCerrada",
                table: "VentaDetalles");

            migrationBuilder.DropColumn(
                name: "TipoArmado",
                table: "VentaDetalles");
        }
    }
}

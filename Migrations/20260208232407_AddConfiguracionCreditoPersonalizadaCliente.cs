using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionCreditoPersonalizadaCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CuotasMaximasPersonalizadas",
                table: "Clientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GastosAdministrativosPersonalizados",
                table: "Clientes",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoMaximoPersonalizado",
                table: "Clientes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoMinimoPersonalizado",
                table: "Clientes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TasaInteresMensualPersonalizada",
                table: "Clientes",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuotasMaximasPersonalizadas",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "GastosAdministrativosPersonalizados",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "MontoMaximoPersonalizado",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "MontoMinimoPersonalizado",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "TasaInteresMensualPersonalizada",
                table: "Clientes");
        }
    }
}

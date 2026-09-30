using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaContactoLibre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ClienteId",
                table: "Ventas",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "DniClienteLibre",
                table: "Ventas",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreClienteLibre",
                table: "Ventas",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelefonoClienteLibre",
                table: "Ventas",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DniClienteLibre",
                table: "Cotizaciones",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Ventas] WHERE [ClienteId] IS NULL) THROW 51000, 'No se puede revertir: existen ventas sin cliente registrado.', 1;");
            migrationBuilder.DropColumn(
                name: "DniClienteLibre",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "NombreClienteLibre",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "TelefonoClienteLibre",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "DniClienteLibre",
                table: "Cotizaciones");

            migrationBuilder.AlterColumn<int>(
                name: "ClienteId",
                table: "Ventas",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}

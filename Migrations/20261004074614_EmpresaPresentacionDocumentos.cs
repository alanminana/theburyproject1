using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class EmpresaPresentacionDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CondicionFiscalClientePorDefecto",
                table: "EmpresasConfiguracion",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DomicilioCompleto",
                table: "EmpresasConfiguracion",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreComercial",
                table: "EmpresasConfiguracion",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PagareVencimientoDias",
                table: "EmpresasConfiguracion",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PagareVencimientoModo",
                table: "EmpresasConfiguracion",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CondicionFiscalClientePorDefecto",
                table: "EmpresasConfiguracion");

            migrationBuilder.DropColumn(
                name: "DomicilioCompleto",
                table: "EmpresasConfiguracion");

            migrationBuilder.DropColumn(
                name: "NombreComercial",
                table: "EmpresasConfiguracion");

            migrationBuilder.DropColumn(
                name: "PagareVencimientoDias",
                table: "EmpresasConfiguracion");

            migrationBuilder.DropColumn(
                name: "PagareVencimientoModo",
                table: "EmpresasConfiguracion");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaValidacionUnificada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agregar nuevos campos a Ventas para validación unificada
            migrationBuilder.AddColumn<string>(
                name: "RazonesAutorizacionJson",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequisitosPendientesJson",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: true);

            // Nota: El nuevo valor de EstadoVenta.PendienteRequisitos = 6
            // es compatible con la columna existente (int)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RazonesAutorizacionJson",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "RequisitosPendientesJson",
                table: "Ventas");
        }
    }
}

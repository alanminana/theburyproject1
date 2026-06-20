using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditColumnsToConfiguracionesCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drift fix: ConfiguracionCredito hereda de AuditableEntity (CreatedBy/UpdatedBy/RowVersion),
            // pero la tabla ConfiguracionesCredito se creó (20251230200000_AddClienteAptitudCredito) sin
            // estas columnas y el snapshot/modelo sí las declara. Sin ellas, cualquier SELECT EF sobre la
            // tabla falla con "Invalid column name", rompiendo Cliente/Details y la evaluación de aptitud.
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ConfiguracionesCredito",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ConfiguracionesCredito",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "ConfiguracionesCredito",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "ConfiguracionesCredito");
        }
    }
}

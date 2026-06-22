using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionScoringCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesScoringCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PuntajeBase = table.Column<int>(type: "int", nullable: false),
                    PuntajeMinimo = table.Column<int>(type: "int", nullable: false),
                    PuntajeMaximo = table.Column<int>(type: "int", nullable: false),
                    AntiguedadActiva = table.Column<bool>(type: "bit", nullable: false),
                    AntiguedadMesesUmbral = table.Column<int>(type: "int", nullable: false),
                    AntiguedadPuntos = table.Column<int>(type: "int", nullable: false),
                    ActividadActiva = table.Column<bool>(type: "bit", nullable: false),
                    ActividadMesesUmbral = table.Column<int>(type: "int", nullable: false),
                    ActividadPuntos = table.Column<int>(type: "int", nullable: false),
                    PagoEnTerminoActivo = table.Column<bool>(type: "bit", nullable: false),
                    PagoEnTerminoPuntos = table.Column<int>(type: "int", nullable: false),
                    PagoConAtrasoPuntos = table.Column<int>(type: "int", nullable: false),
                    SueldoActivo = table.Column<bool>(type: "bit", nullable: false),
                    SueldoUmbral = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SueldoPuntos = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesScoringCliente", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesScoringCliente");
        }
    }
}

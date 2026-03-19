using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddVentaDatosCreditoPersonallJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DatosCreditoPersonallJson",
                table: "Ventas",
                type: "nvarchar(max)",
                nullable: true);

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

            migrationBuilder.AddColumn<int>(
                name: "EstadoCrediticio",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaUltimaEvaluacion",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LimiteCredito",
                table: "Clientes",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoNoApto",
                table: "Clientes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfiguracionesCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ValidarDocumentacion = table.Column<bool>(type: "bit", nullable: false),
                    TiposDocumentoRequeridos = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValidarVencimientoDocumentos = table.Column<bool>(type: "bit", nullable: false),
                    DiasGraciaVencimientoDocumento = table.Column<int>(type: "int", nullable: true),
                    ValidarLimiteCredito = table.Column<bool>(type: "bit", nullable: false),
                    LimiteCreditoMinimo = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LimiteCreditoDefault = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PorcentajeCupoMinimoRequerido = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValidarMora = table.Column<bool>(type: "bit", nullable: false),
                    DiasParaRequerirAutorizacion = table.Column<int>(type: "int", nullable: true),
                    DiasParaNoApto = table.Column<int>(type: "int", nullable: true),
                    MontoMoraParaRequerirAutorizacion = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MontoMoraParaNoApto = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CuotasVencidasParaNoApto = table.Column<int>(type: "int", nullable: true),
                    RecalculoAutomatico = table.Column<bool>(type: "bit", nullable: false),
                    DiasValidezEvaluacion = table.Column<int>(type: "int", nullable: true),
                    AuditoriaActiva = table.Column<bool>(type: "bit", nullable: false),
                    NotificacionesCambioEstado = table.Column<bool>(type: "bit", nullable: false),
                    MensajeConfiguracionDeshabilitada = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaUltimaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModificadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesCredito", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "DatosCreditoPersonallJson",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "RazonesAutorizacionJson",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "RequisitosPendientesJson",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "EstadoCrediticio",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "FechaUltimaEvaluacion",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "LimiteCredito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "MotivoNoApto",
                table: "Clientes");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteAptitudCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agregar campos de aptitud crediticia a Cliente
            migrationBuilder.AddColumn<int>(
                name: "EstadoCrediticio",
                table: "Clientes",
                type: "int",
                nullable: false,
                defaultValue: 0); // NoEvaluado

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

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaUltimaEvaluacion",
                table: "Clientes",
                type: "datetime2",
                nullable: true);

            // Crear tabla ConfiguracionCredito
            migrationBuilder.CreateTable(
                name: "ConfiguracionesCredito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    
                    // Documentación
                    ValidarDocumentacion = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TiposDocumentoRequeridos = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValidarVencimientoDocumentos = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DiasGraciaVencimientoDocumento = table.Column<int>(type: "int", nullable: true, defaultValue: 0),
                    
                    // Límite de crédito
                    ValidarLimiteCredito = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LimiteCreditoMinimo = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LimiteCreditoDefault = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PorcentajeCupoMinimoRequerido = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    
                    // Mora
                    ValidarMora = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DiasParaRequerirAutorizacion = table.Column<int>(type: "int", nullable: true, defaultValue: 1),
                    DiasParaNoApto = table.Column<int>(type: "int", nullable: true),
                    MontoMoraParaRequerirAutorizacion = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MontoMoraParaNoApto = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CuotasVencidasParaNoApto = table.Column<int>(type: "int", nullable: true),
                    
                    // General
                    RecalculoAutomatico = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DiasValidezEvaluacion = table.Column<int>(type: "int", nullable: true, defaultValue: 30),
                    AuditoriaActiva = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NotificacionesCambioEstado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MensajeConfiguracionDeshabilitada = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaUltimaModificacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModificadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    
                    // Campos de auditoría
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesCredito", x => x.Id);
                });

            // Insertar configuración por defecto
            migrationBuilder.InsertData(
                table: "ConfiguracionesCredito",
                columns: new[] 
                { 
                    "ValidarDocumentacion", "ValidarVencimientoDocumentos", 
                    "ValidarLimiteCredito", "ValidarMora",
                    "DiasParaRequerirAutorizacion", "RecalculoAutomatico", 
                    "DiasValidezEvaluacion", "AuditoriaActiva",
                    "MensajeConfiguracionDeshabilitada",
                    "CreatedAt", "UpdatedAt", "IsDeleted"
                },
                values: new object[] 
                { 
                    true, true, 
                    true, true,
                    1, true, 
                    30, true,
                    "La validación de aptitud crediticia no está configurada. Configure los parámetros en Administración.",
                    DateTime.UtcNow, DateTime.UtcNow, false
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesCredito");

            migrationBuilder.DropColumn(
                name: "EstadoCrediticio",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "LimiteCredito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "MotivoNoApto",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "FechaUltimaEvaluacion",
                table: "Clientes");
        }
    }
}

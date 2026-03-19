using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddMoraModuleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_ClienteId",
                table: "AlertasCobranza");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_CreditoId",
                table: "AlertasCobranza");

            migrationBuilder.AlterColumn<int>(
                name: "DiasGracia",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "ActualizarMoraAutomaticamente",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AlertasPreventivasActivas",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BaseCalculoMora",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BloqueoAutomaticoActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CambiarEstadoCuotaAuto",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CanalPreferido",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CuotasVencidasParaBloquear",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DesbloqueoAutomatico",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DiasAntesAlertaPreventiva",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasAntesNotificacionPreventiva",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasMaximosSinGestion",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasParaBloquear",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasParaCumplirPromesa",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasParaPrioridadAlta",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasParaPrioridadCritica",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasParaPrioridadMedia",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnviarFinDeSemana",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EscalonamientoActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FrecuenciaRecordatorioMora",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HoraEjecucionDiaria",
                table: "ConfiguracionesMora",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HoraFinEnvio",
                table: "ConfiguracionesMora",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HoraInicioEnvio",
                table: "ConfiguracionesMora",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImpactarScorePorMora",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaximoCuotasAcuerdo",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaximoNotificacionesDiarias",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaximoNotificacionesPorCuota",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoMoraParaBloquear",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoParaPrioridadAlta",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoParaPrioridadCritica",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoParaPrioridadMedia",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MoraMinima",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificacionesActivas",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotificarCuotaVencida",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotificarMoraAcumulada",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotificarProximoVencimiento",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PermitirCondonacionMora",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeMaximoCondonacion",
                table: "ConfiguracionesMora",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeMinimoEntrega",
                table: "ConfiguracionesMora",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PorcentajeRecuperacionScore",
                table: "ConfiguracionesMora",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProcesoAutomaticoActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PuntosMaximosARestar",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuntosRestarPorCuotaVencida",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PuntosRestarPorDiaMora",
                table: "ConfiguracionesMora",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecuperarScoreAlPagar",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TasaMoraBase",
                table: "ConfiguracionesMora",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TasaPrimerMes",
                table: "ConfiguracionesMora",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TasaSegundoMes",
                table: "ConfiguracionesMora",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TasaTercerMesEnAdelante",
                table: "ConfiguracionesMora",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoBloqueo",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoTasaMora",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoTopeMora",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TopeMaximoMoraActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorTopeMora",
                table: "ConfiguracionesMora",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppActivo",
                table: "ConfiguracionesMora",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Observaciones",
                table: "AlertasCobranza",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mensaje",
                table: "AlertasCobranza",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "CuotaId",
                table: "AlertasCobranza",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasAtraso",
                table: "AlertasCobranza",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstadoGestion",
                table: "AlertasCobranza",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAsignacion",
                table: "AlertasCobranza",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaPromesaPago",
                table: "AlertasCobranza",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GestorAsignadoId",
                table: "AlertasCobranza",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoMoraCalculada",
                table: "AlertasCobranza",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoPromesaPago",
                table: "AlertasCobranza",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoTotal",
                table: "AlertasCobranza",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "MotivoResolucion",
                table: "AlertasCobranza",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificacionesEnviadas",
                table: "AlertasCobranza",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaNotificacion",
                table: "AlertasCobranza",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcuerdosPago",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlertaCobranzaId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    CreditoId = table.Column<int>(type: "int", nullable: false),
                    NumeroAcuerdo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActivacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    MontoDeudaOriginal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoMoraOriginal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoCondonado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoTotalAcuerdo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoEntregaInicial = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EntregaInicialPagada = table.Column<bool>(type: "bit", nullable: false),
                    FechaPagoEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CantidadCuotas = table.Column<int>(type: "int", nullable: false),
                    FechaPrimeraCuota = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MontoCuotaAcuerdo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreadoPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AprobadoPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MotivoIncumplimiento = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MotivoCancelacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuerdosPago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuerdosPago_AlertasCobranza_AlertaCobranzaId",
                        column: x => x.AlertaCobranzaId,
                        principalTable: "AlertasCobranza",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcuerdosPago_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcuerdosPago_Creditos_CreditoId",
                        column: x => x.CreditoId,
                        principalTable: "Creditos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HistorialContactos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlertaCobranzaId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    GestorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FechaContacto = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TipoContacto = table.Column<int>(type: "int", nullable: false),
                    Resultado = table.Column<int>(type: "int", nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DuracionMinutos = table.Column<int>(type: "int", nullable: true),
                    ProximoContacto = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaPromesaPago = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MontoPromesaPago = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialContactos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistorialContactos_AlertasCobranza_AlertaCobranzaId",
                        column: x => x.AlertaCobranzaId,
                        principalTable: "AlertasCobranza",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistorialContactos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlantillasNotificacionMora",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Canal = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Asunto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_PlantillasNotificacionMora", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CuotasAcuerdo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcuerdoPagoId = table.Column<int>(type: "int", nullable: false),
                    NumeroCuota = table.Column<int>(type: "int", nullable: false),
                    MontoCapital = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoMora = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaVencimiento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MontoPagado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    ComprobantePago = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MedioPago = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuotasAcuerdo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CuotasAcuerdo_AcuerdosPago_AcuerdoPagoId",
                        column: x => x.AcuerdoPagoId,
                        principalTable: "AcuerdosPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_ClienteId_Resuelta",
                table: "AlertasCobranza",
                columns: new[] { "ClienteId", "Resuelta" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_Credito_Tipo_Resuelta",
                table: "AlertasCobranza",
                columns: new[] { "CreditoId", "Tipo", "Resuelta" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_CuotaId",
                table: "AlertasCobranza",
                column: "CuotaId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_EstadoGestion",
                table: "AlertasCobranza",
                column: "EstadoGestion");

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_GestorAsignadoId",
                table: "AlertasCobranza",
                column: "GestorAsignadoId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_AlertaCobranzaId",
                table: "AcuerdosPago",
                column: "AlertaCobranzaId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_ClienteId",
                table: "AcuerdosPago",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_CreditoId",
                table: "AcuerdosPago",
                column: "CreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_Estado",
                table: "AcuerdosPago",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_FechaCreacion",
                table: "AcuerdosPago",
                column: "FechaCreacion");

            migrationBuilder.CreateIndex(
                name: "IX_AcuerdosPago_NumeroAcuerdo",
                table: "AcuerdosPago",
                column: "NumeroAcuerdo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuotasAcuerdo_AcuerdoPagoId",
                table: "CuotasAcuerdo",
                column: "AcuerdoPagoId");

            migrationBuilder.CreateIndex(
                name: "IX_CuotasAcuerdo_AcuerdoPagoId_NumeroCuota",
                table: "CuotasAcuerdo",
                columns: new[] { "AcuerdoPagoId", "NumeroCuota" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuotasAcuerdo_Estado",
                table: "CuotasAcuerdo",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_CuotasAcuerdo_FechaVencimiento",
                table: "CuotasAcuerdo",
                column: "FechaVencimiento");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialContactos_AlertaCobranzaId",
                table: "HistorialContactos",
                column: "AlertaCobranzaId");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialContactos_ClienteId",
                table: "HistorialContactos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialContactos_FechaContacto",
                table: "HistorialContactos",
                column: "FechaContacto");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialContactos_Resultado",
                table: "HistorialContactos",
                column: "Resultado");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialContactos_TipoContacto",
                table: "HistorialContactos",
                column: "TipoContacto");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasNotificacionMora_Activa",
                table: "PlantillasNotificacionMora",
                column: "Activa");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasNotificacionMora_Tipo_Canal_Activa",
                table: "PlantillasNotificacionMora",
                columns: new[] { "Tipo", "Canal", "Activa" });

            migrationBuilder.AddForeignKey(
                name: "FK_AlertasCobranza_Cuotas_CuotaId",
                table: "AlertasCobranza",
                column: "CuotaId",
                principalTable: "Cuotas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlertasCobranza_Cuotas_CuotaId",
                table: "AlertasCobranza");

            migrationBuilder.DropTable(
                name: "CuotasAcuerdo");

            migrationBuilder.DropTable(
                name: "HistorialContactos");

            migrationBuilder.DropTable(
                name: "PlantillasNotificacionMora");

            migrationBuilder.DropTable(
                name: "AcuerdosPago");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_ClienteId_Resuelta",
                table: "AlertasCobranza");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_Credito_Tipo_Resuelta",
                table: "AlertasCobranza");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_CuotaId",
                table: "AlertasCobranza");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_EstadoGestion",
                table: "AlertasCobranza");

            migrationBuilder.DropIndex(
                name: "IX_AlertasCobranza_GestorAsignadoId",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "ActualizarMoraAutomaticamente",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "AlertasPreventivasActivas",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "BaseCalculoMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "BloqueoAutomaticoActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "CambiarEstadoCuotaAuto",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "CanalPreferido",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "CuotasVencidasParaBloquear",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DesbloqueoAutomatico",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasAntesAlertaPreventiva",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasAntesNotificacionPreventiva",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasMaximosSinGestion",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasParaBloquear",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasParaCumplirPromesa",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasParaPrioridadAlta",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasParaPrioridadCritica",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "DiasParaPrioridadMedia",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "EmailActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "EnviarFinDeSemana",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "EscalonamientoActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "FrecuenciaRecordatorioMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "HoraEjecucionDiaria",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "HoraFinEnvio",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "HoraInicioEnvio",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "ImpactarScorePorMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MaximoCuotasAcuerdo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MaximoNotificacionesDiarias",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MaximoNotificacionesPorCuota",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MontoMoraParaBloquear",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MontoParaPrioridadAlta",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MontoParaPrioridadCritica",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MontoParaPrioridadMedia",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "MoraMinima",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "NotificacionesActivas",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "NotificarCuotaVencida",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "NotificarMoraAcumulada",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "NotificarProximoVencimiento",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PermitirCondonacionMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PorcentajeMaximoCondonacion",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PorcentajeMinimoEntrega",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PorcentajeRecuperacionScore",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "ProcesoAutomaticoActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PuntosMaximosARestar",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PuntosRestarPorCuotaVencida",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "PuntosRestarPorDiaMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "RecuperarScoreAlPagar",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TasaMoraBase",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TasaPrimerMes",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TasaSegundoMes",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TasaTercerMesEnAdelante",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TipoBloqueo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TipoTasaMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TipoTopeMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "TopeMaximoMoraActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "ValorTopeMora",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "WhatsAppActivo",
                table: "ConfiguracionesMora");

            migrationBuilder.DropColumn(
                name: "CuotaId",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "DiasAtraso",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "EstadoGestion",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "FechaAsignacion",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "FechaPromesaPago",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "GestorAsignadoId",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "MontoMoraCalculada",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "MontoPromesaPago",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "MontoTotal",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "MotivoResolucion",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "NotificacionesEnviadas",
                table: "AlertasCobranza");

            migrationBuilder.DropColumn(
                name: "UltimaNotificacion",
                table: "AlertasCobranza");

            migrationBuilder.AlterColumn<int>(
                name: "DiasGracia",
                table: "ConfiguracionesMora",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Observaciones",
                table: "AlertasCobranza",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mensaje",
                table: "AlertasCobranza",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_ClienteId",
                table: "AlertasCobranza",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertasCobranza_CreditoId",
                table: "AlertasCobranza",
                column: "CreditoId");
        }
    }
}

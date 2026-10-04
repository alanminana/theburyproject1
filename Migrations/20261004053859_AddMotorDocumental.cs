using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorDocumental : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaquetesDocumentales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_PaquetesDocumentales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TiposDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Categoria = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    PermiteMultiples = table.Column<bool>(type: "bit", nullable: false),
                    RequiereFirma = table.Column<bool>(type: "bit", nullable: false),
                    Prefijo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UltimoNumero = table.Column<long>(type: "bigint", nullable: false),
                    EsSistema = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposDocumento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlantillasDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoDocumentoId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    VigenteDesde = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VigenteHasta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VersionActual = table.Column<int>(type: "int", nullable: false),
                    RequiereFirma = table.Column<bool>(type: "bit", nullable: false),
                    FirmantesRequeridos = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Copias = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantillasDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlantillasDocumento_TiposDocumento_TipoDocumentoId",
                        column: x => x.TipoDocumentoId,
                        principalTable: "TiposDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaquetesDocumentalesItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaqueteDocumentalId = table.Column<int>(type: "int", nullable: false),
                    PlantillaDocumentoId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PaquetesDocumentalesItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaquetesDocumentalesItems_PaquetesDocumentales_PaqueteDocumentalId",
                        column: x => x.PaqueteDocumentalId,
                        principalTable: "PaquetesDocumentales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaquetesDocumentalesItems_PlantillasDocumento_PlantillaDocumentoId",
                        column: x => x.PlantillaDocumentoId,
                        principalTable: "PlantillasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlantillasDocumentoVersion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlantillaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VariablesRequeridas = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Comentario = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantillasDocumentoVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlantillasDocumentoVersion_PlantillasDocumento_PlantillaDocumentoId",
                        column: x => x.PlantillaDocumentoId,
                        principalTable: "PlantillasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReglasDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EventoCodigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CondicionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlantillaDocumentoId = table.Column<int>(type: "int", nullable: true),
                    PaqueteDocumentalId = table.Column<int>(type: "int", nullable: true),
                    Prioridad = table.Column<int>(type: "int", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    Obligatoria = table.Column<bool>(type: "bit", nullable: false),
                    GrupoExclusion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExigeFirmaParaContinuar = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReglasDocumento_PaquetesDocumentales_PaqueteDocumentalId",
                        column: x => x.PaqueteDocumentalId,
                        principalTable: "PaquetesDocumentales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReglasDocumento_PlantillasDocumento_PlantillaDocumentoId",
                        column: x => x.PlantillaDocumentoId,
                        principalTable: "PlantillasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentosGenerados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoDocumentoId = table.Column<int>(type: "int", nullable: false),
                    PlantillaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    PlantillaDocumentoVersionId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    VentaId = table.Column<int>(type: "int", nullable: true),
                    CreditoId = table.Column<int>(type: "int", nullable: true),
                    CuotaId = table.Column<int>(type: "int", nullable: true),
                    PagoCuotaId = table.Column<int>(type: "int", nullable: true),
                    CotizacionId = table.Column<int>(type: "int", nullable: true),
                    EventoOrigen = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReglaDocumentoId = table.Column<int>(type: "int", nullable: true),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GrupoImpresionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    FechaGeneracionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioGeneracion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContenidoRenderizado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DatosSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequiereFirma = table.Column<bool>(type: "bit", nullable: false),
                    FirmantesRequeridos = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirmasJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExigeFirmaParaContinuar = table.Column<bool>(type: "bit", nullable: false),
                    ReemplazaADocumentoId = table.Column<int>(type: "int", nullable: true),
                    ReemplazadoPorDocumentoId = table.Column<int>(type: "int", nullable: true),
                    CanceladoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaCancelacionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoCancelacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContadorReimpresiones = table.Column<int>(type: "int", nullable: false),
                    UltimaReimpresionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContratoLegadoId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentosGenerados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_Creditos_CreditoId",
                        column: x => x.CreditoId,
                        principalTable: "Creditos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_PlantillasDocumentoVersion_PlantillaDocumentoVersionId",
                        column: x => x.PlantillaDocumentoVersionId,
                        principalTable: "PlantillasDocumentoVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_PlantillasDocumento_PlantillaDocumentoId",
                        column: x => x.PlantillaDocumentoId,
                        principalTable: "PlantillasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_TiposDocumento_TipoDocumentoId",
                        column: x => x.TipoDocumentoId,
                        principalTable: "TiposDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosGenerados_Ventas_VentaId",
                        column: x => x.VentaId,
                        principalTable: "Ventas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_ClaveIdempotencia",
                table: "DocumentosGenerados",
                column: "ClaveIdempotencia",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_ClienteId",
                table: "DocumentosGenerados",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_CreditoId",
                table: "DocumentosGenerados",
                column: "CreditoId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_FechaGeneracionUtc",
                table: "DocumentosGenerados",
                column: "FechaGeneracionUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_GrupoImpresionId",
                table: "DocumentosGenerados",
                column: "GrupoImpresionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_PagoCuotaId",
                table: "DocumentosGenerados",
                column: "PagoCuotaId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_PlantillaDocumentoId",
                table: "DocumentosGenerados",
                column: "PlantillaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_PlantillaDocumentoVersionId",
                table: "DocumentosGenerados",
                column: "PlantillaDocumentoVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_TipoDocumentoId_Numero",
                table: "DocumentosGenerados",
                columns: new[] { "TipoDocumentoId", "Numero" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosGenerados_VentaId",
                table: "DocumentosGenerados",
                column: "VentaId");

            migrationBuilder.CreateIndex(
                name: "IX_PaquetesDocumentales_Codigo",
                table: "PaquetesDocumentales",
                column: "Codigo",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaquetesDocumentalesItems_PaqueteDocumentalId",
                table: "PaquetesDocumentalesItems",
                column: "PaqueteDocumentalId");

            migrationBuilder.CreateIndex(
                name: "IX_PaquetesDocumentalesItems_PlantillaDocumentoId",
                table: "PaquetesDocumentalesItems",
                column: "PlantillaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasDocumento_Codigo",
                table: "PlantillasDocumento",
                column: "Codigo",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasDocumento_TipoDocumentoId",
                table: "PlantillasDocumento",
                column: "TipoDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantillasDocumentoVersion_PlantillaDocumentoId_Numero",
                table: "PlantillasDocumentoVersion",
                columns: new[] { "PlantillaDocumentoId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReglasDocumento_EventoCodigo_Activa_Prioridad",
                table: "ReglasDocumento",
                columns: new[] { "EventoCodigo", "Activa", "Prioridad" });

            migrationBuilder.CreateIndex(
                name: "IX_ReglasDocumento_PaqueteDocumentalId",
                table: "ReglasDocumento",
                column: "PaqueteDocumentalId");

            migrationBuilder.CreateIndex(
                name: "IX_ReglasDocumento_PlantillaDocumentoId",
                table: "ReglasDocumento",
                column: "PlantillaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_TiposDocumento_Codigo",
                table: "TiposDocumento",
                column: "Codigo",
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentosGenerados");

            migrationBuilder.DropTable(
                name: "PaquetesDocumentalesItems");

            migrationBuilder.DropTable(
                name: "ReglasDocumento");

            migrationBuilder.DropTable(
                name: "PlantillasDocumentoVersion");

            migrationBuilder.DropTable(
                name: "PaquetesDocumentales");

            migrationBuilder.DropTable(
                name: "PlantillasDocumento");

            migrationBuilder.DropTable(
                name: "TiposDocumento");
        }
    }
}

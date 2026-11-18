using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class D03_TesoreriaOrigenesAnticipos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PagosProveedor_ChequeId",
                table: "PagosProveedor");

            migrationBuilder.AddColumn<int>(
                name: "OrigenFondosId",
                table: "PagosProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrigenesFondos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_OrigenesFondos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnticiposProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProveedorId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OrigenFondosId = table.Column<int>(type: "int", nullable: false),
                    Medio = table.Column<int>(type: "int", nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeId = table.Column<int>(type: "int", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Anulado = table.Column<bool>(type: "bit", nullable: false),
                    AnuladoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnticiposProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnticiposProveedor_Cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "Cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnticiposProveedor_OrigenesFondos_OrigenFondosId",
                        column: x => x.OrigenFondosId,
                        principalTable: "OrigenesFondos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnticiposProveedor_Proveedores_ProveedorId",
                        column: x => x.ProveedorId,
                        principalTable: "Proveedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnticipoAplicaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnticipoProveedorId = table.Column<int>(type: "int", nullable: false),
                    ComprobanteProveedorId = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Anulada = table.Column<bool>(type: "bit", nullable: false),
                    AnuladaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnticipoAplicaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnticipoAplicaciones_AnticiposProveedor_AnticipoProveedorId",
                        column: x => x.AnticipoProveedorId,
                        principalTable: "AnticiposProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnticipoAplicaciones_ComprobantesProveedor_ComprobanteProveedorId",
                        column: x => x.ComprobanteProveedorId,
                        principalTable: "ComprobantesProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // El esquema anterior permitía que un mismo cheque respaldara varios pagos vigentes (solo era una referencia). Antes de crear el
            // índice único se concilia de forma conservadora y trazable: el pago MÁS ANTIGUO conserva el cheque y los demás pierden solo la
            // referencia, con una nota en sus observaciones. No se tocan importes, imputaciones, saldos ni estados: toda la historia queda.
            // En una base nueva (sin pagos) no afecta ninguna fila.
            migrationBuilder.Sql(@"
UPDATE p
SET p.ChequeId = NULL,
    p.Observaciones = LEFT(CONCAT(ISNULL(p.Observaciones + N' ', N''),
        N'[Migración D-03] Se quitó la referencia al cheque Nº ', c.Numero,
        N': un cheque físico respalda un único pago y ya lo respalda el pago #', CAST(k.IdConserva AS nvarchar(20)), N'.'), 1000)
FROM PagosProveedor p
JOIN Cheques c ON c.Id = p.ChequeId
JOIN (SELECT ChequeId, MIN(Id) AS IdConserva
      FROM PagosProveedor
      WHERE ChequeId IS NOT NULL AND Anulado = 0 AND IsDeleted = 0
      GROUP BY ChequeId HAVING COUNT(*) > 1) k ON k.ChequeId = p.ChequeId
WHERE p.ChequeId IS NOT NULL AND p.Anulado = 0 AND p.IsDeleted = 0 AND p.Id <> k.IdConserva;");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_ChequeId_Vigente",
                table: "PagosProveedor",
                column: "ChequeId",
                unique: true,
                filter: "[ChequeId] IS NOT NULL AND [Anulado] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_OrigenFondosId",
                table: "PagosProveedor",
                column: "OrigenFondosId");

            migrationBuilder.CreateIndex(
                name: "IX_AnticipoAplicaciones_AnticipoProveedorId_ClaveIdempotencia",
                table: "AnticipoAplicaciones",
                columns: new[] { "AnticipoProveedorId", "ClaveIdempotencia" });

            migrationBuilder.CreateIndex(
                name: "IX_AnticipoAplicaciones_ComprobanteProveedorId",
                table: "AnticipoAplicaciones",
                column: "ComprobanteProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_ChequeId_Vigente",
                table: "AnticiposProveedor",
                column: "ChequeId",
                unique: true,
                filter: "[ChequeId] IS NOT NULL AND [Anulado] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_OrigenFondosId",
                table: "AnticiposProveedor",
                column: "OrigenFondosId");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_ProveedorId_ClaveIdempotencia",
                table: "AnticiposProveedor",
                columns: new[] { "ProveedorId", "ClaveIdempotencia" },
                unique: true,
                filter: "ClaveIdempotencia IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_ProveedorId_Fecha",
                table: "AnticiposProveedor",
                columns: new[] { "ProveedorId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_OrigenesFondos_Activo_Orden",
                table: "OrigenesFondos",
                columns: new[] { "Activo", "Orden" });

            migrationBuilder.CreateIndex(
                name: "IX_OrigenesFondos_Codigo_Activo",
                table: "OrigenesFondos",
                column: "Codigo",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_PagosProveedor_OrigenesFondos_OrigenFondosId",
                table: "PagosProveedor",
                column: "OrigenFondosId",
                principalTable: "OrigenesFondos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagosProveedor_OrigenesFondos_OrigenFondosId",
                table: "PagosProveedor");

            migrationBuilder.DropTable(
                name: "AnticipoAplicaciones");

            migrationBuilder.DropTable(
                name: "AnticiposProveedor");

            migrationBuilder.DropTable(
                name: "OrigenesFondos");

            migrationBuilder.DropIndex(
                name: "IX_PagosProveedor_ChequeId_Vigente",
                table: "PagosProveedor");

            migrationBuilder.DropIndex(
                name: "IX_PagosProveedor_OrigenFondosId",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "OrigenFondosId",
                table: "PagosProveedor");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_ChequeId",
                table: "PagosProveedor",
                column: "ChequeId");
        }
    }
}

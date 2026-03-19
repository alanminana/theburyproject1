using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddSeguridadAuditoriaEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeguridadEventosAuditoria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaEvento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UsuarioNombre = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Modulo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Entidad = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DireccionIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeguridadEventosAuditoria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeguridadEventosAuditoria_Accion",
                table: "SeguridadEventosAuditoria",
                column: "Accion");

            migrationBuilder.CreateIndex(
                name: "IX_SeguridadEventosAuditoria_FechaEvento",
                table: "SeguridadEventosAuditoria",
                column: "FechaEvento");

            migrationBuilder.CreateIndex(
                name: "IX_SeguridadEventosAuditoria_Modulo",
                table: "SeguridadEventosAuditoria",
                column: "Modulo");

            migrationBuilder.CreateIndex(
                name: "IX_SeguridadEventosAuditoria_UsuarioNombre",
                table: "SeguridadEventosAuditoria",
                column: "UsuarioNombre");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeguridadEventosAuditoria");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminosCondicionesAceptacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminosCondicionesAceptaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UsuarioNombreUsuario = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NombreIngresado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VersionTerminos = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaAceptacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DireccionIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminosCondicionesAceptaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminosCondicionesAceptaciones_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminosCondicionesAceptaciones_UsuarioId_VersionTerminos",
                table: "TerminosCondicionesAceptaciones",
                columns: new[] { "UsuarioId", "VersionTerminos" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminosCondicionesAceptaciones");
        }
    }
}

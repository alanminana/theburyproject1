using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class NotasRapidasTipoEItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotasRapidas_UsuarioId_Completada_CreatedAt",
                table: "NotasRapidas");

            migrationBuilder.DropColumn(
                name: "Completada",
                table: "NotasRapidas");

            migrationBuilder.DropColumn(
                name: "FechaCompletada",
                table: "NotasRapidas");

            migrationBuilder.AddColumn<int>(
                name: "Tipo",
                table: "NotasRapidas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NotaRapidaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotaRapidaId = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Completado = table.Column<bool>(type: "bit", nullable: false),
                    FechaCompletado = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotaRapidaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotaRapidaItems_NotasRapidas_NotaRapidaId",
                        column: x => x.NotaRapidaId,
                        principalTable: "NotasRapidas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotasRapidas_UsuarioId_CreatedAt",
                table: "NotasRapidas",
                columns: new[] { "UsuarioId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotaRapidaItems_NotaRapidaId",
                table: "NotaRapidaItems",
                column: "NotaRapidaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotaRapidaItems");

            migrationBuilder.DropIndex(
                name: "IX_NotasRapidas_UsuarioId_CreatedAt",
                table: "NotasRapidas");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "NotasRapidas");

            migrationBuilder.AddColumn<bool>(
                name: "Completada",
                table: "NotasRapidas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCompletada",
                table: "NotasRapidas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotasRapidas_UsuarioId_Completada_CreatedAt",
                table: "NotasRapidas",
                columns: new[] { "UsuarioId", "Completada", "CreatedAt" });
        }
    }
}

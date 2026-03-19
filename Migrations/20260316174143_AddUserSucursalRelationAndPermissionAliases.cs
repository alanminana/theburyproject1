using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSucursalRelationAndPermissionAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Sucursal",
                table: "AspNetUsers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SucursalId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sucursales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sucursales", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [Sucursales] ([Nombre], [Activa], [CreatedAt], [CreatedBy], [IsDeleted])
                SELECT DISTINCT LEFT(LTRIM(RTRIM([Sucursal])), 120), 1, SYSUTCDATETIME(), N'Migration', 0
                FROM [AspNetUsers]
                WHERE [Sucursal] IS NOT NULL
                  AND LTRIM(RTRIM([Sucursal])) <> N'';

                IF NOT EXISTS (SELECT 1 FROM [Sucursales] WHERE [IsDeleted] = 0)
                BEGIN
                    INSERT INTO [Sucursales] ([Nombre], [Activa], [CreatedAt], [CreatedBy], [IsDeleted])
                    VALUES (N'Casa Central', 1, SYSUTCDATETIME(), N'Migration', 0);
                END;

                UPDATE [u]
                SET [u].[SucursalId] = [s].[Id],
                    [u].[Sucursal] = [s].[Nombre]
                FROM [AspNetUsers] AS [u]
                INNER JOIN [Sucursales] AS [s]
                    ON [s].[Nombre] = LEFT(LTRIM(RTRIM([u].[Sucursal])), 120)
                WHERE [u].[Sucursal] IS NOT NULL
                  AND LTRIM(RTRIM([u].[Sucursal])) <> N'';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SucursalId",
                table: "AspNetUsers",
                column: "SucursalId");

            migrationBuilder.CreateIndex(
                name: "IX_Sucursales_Nombre",
                table: "Sucursales",
                column: "Nombre",
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Sucursales_SucursalId",
                table: "AspNetUsers",
                column: "SucursalId",
                principalTable: "Sucursales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Sucursales_SucursalId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Sucursales");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SucursalId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SucursalId",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<string>(
                name: "Sucursal",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120,
                oldNullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class QaPrdP1MovimientoStockRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "MovimientosStock",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosStock_RequestId",
                table: "MovimientosStock",
                column: "RequestId",
                unique: true,
                filter: "RequestId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MovimientosStock_RequestId",
                table: "MovimientosStock");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "MovimientosStock");
        }
    }
}

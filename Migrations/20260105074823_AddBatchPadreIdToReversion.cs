using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchPadreIdToReversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchPadreId",
                table: "PriceChangeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeBatches_BatchPadreId",
                table: "PriceChangeBatches",
                column: "BatchPadreId",
                unique: true,
                filter: "[BatchPadreId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceChangeBatches_PriceChangeBatches_BatchPadreId",
                table: "PriceChangeBatches",
                column: "BatchPadreId",
                principalTable: "PriceChangeBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceChangeBatches_PriceChangeBatches_BatchPadreId",
                table: "PriceChangeBatches");

            migrationBuilder.DropIndex(
                name: "IX_PriceChangeBatches_BatchPadreId",
                table: "PriceChangeBatches");

            migrationBuilder.DropColumn(
                name: "BatchPadreId",
                table: "PriceChangeBatches");
        }
    }
}

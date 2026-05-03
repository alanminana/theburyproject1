using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260502073010_AddProductoMaxCuotasSinInteres")]
    public partial class AddProductoMaxCuotasSinInteres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxCuotasSinInteresPermitidas",
                table: "Productos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxCuotasSinInteresPermitidas",
                table: "Productos");
        }
    }
}

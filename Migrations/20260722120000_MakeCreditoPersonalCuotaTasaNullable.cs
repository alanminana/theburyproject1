using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class MakeCreditoPersonalCuotaTasaNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TasaMensual pasa a nullable: NULL = "heredar la tasa global".
            // Se elimina el DEFAULT 0 para que un NULL persista como NULL (heredar)
            // y no como 0 (0 % explicito). Los 0 existentes se conservan como 0 % explicito.
            migrationBuilder.AlterColumn<decimal>(
                name: "TasaMensual",
                table: "ProductoCreditoPersonalCuotas",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldPrecision: 8,
                oldScale: 4,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "TasaMensual",
                table: "ConfiguracionCreditoPersonalCuotas",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldPrecision: 8,
                oldScale: 4,
                oldDefaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversion: los NULL (heredar) se normalizan a 0 antes de volver a NOT NULL con DEFAULT 0.
            migrationBuilder.Sql("UPDATE [ProductoCreditoPersonalCuotas] SET [TasaMensual] = 0 WHERE [TasaMensual] IS NULL;");
            migrationBuilder.Sql("UPDATE [ConfiguracionCreditoPersonalCuotas] SET [TasaMensual] = 0 WHERE [TasaMensual] IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "TasaMensual",
                table: "ProductoCreditoPersonalCuotas",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldPrecision: 8,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TasaMensual",
                table: "ConfiguracionCreditoPersonalCuotas",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldPrecision: 8,
                oldScale: 4,
                oldNullable: true);
        }
    }
}

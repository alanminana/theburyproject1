using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513170000_BackfillProductoCreditoRestriccionesFromCondicionesPago")]
    public partial class BackfillProductoCreditoRestriccionesFromCondicionesPago : Migration
    {
        private const int TipoPagoCreditoPersonal = 5;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF EXISTS (
                    SELECT 1
                    FROM [ProductoCondicionesPago]
                    WHERE [TipoPago] = {TipoPagoCreditoPersonal}
                      AND [IsDeleted] = 0
                      AND [Activo] = 1
                    GROUP BY [ProductoId]
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51001, 'ProductoCondicionesPago contiene mas de una condicion activa CreditoPersonal por ProductoId. Resolver duplicados antes de migrar restricciones.', 1;
                END;

                INSERT INTO [ProductoCreditoRestricciones]
                    ([ProductoId], [Activo], [Permitido], [MaxCuotasCredito], [Observaciones],
                     [CreatedAt], [UpdatedAt], [CreatedBy], [UpdatedBy], [IsDeleted])
                SELECT
                    c.[ProductoId],
                    c.[Activo],
                    COALESCE(c.[Permitido], CAST(1 AS bit)),
                    c.[MaxCuotasCredito],
                    c.[Observaciones],
                    c.[CreatedAt],
                    c.[UpdatedAt],
                    c.[CreatedBy],
                    c.[UpdatedBy],
                    CAST(0 AS bit)
                FROM [ProductoCondicionesPago] AS c
                WHERE c.[TipoPago] = {TipoPagoCreditoPersonal}
                  AND c.[IsDeleted] = 0
                  AND c.[Activo] = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [ProductoCreditoRestricciones] AS r
                      WHERE r.[ProductoId] = c.[ProductoId]
                        AND r.[IsDeleted] = 0
                        AND r.[Activo] = 1
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No hay columna de origen/provenance que permita distinguir de forma segura
            // registros migrados de registros creados o editados posteriormente.
            // Se preservan los datos para no borrar historial ni decisiones operativas futuras.
            migrationBuilder.Sql("""
                PRINT N'Down no destructivo: ProductoCreditoRestricciones pobladas desde ProductoCondicionesPago no se eliminan porque no tienen marcador seguro de origen.';
                """);
        }
    }
}

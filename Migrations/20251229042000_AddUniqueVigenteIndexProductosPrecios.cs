using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20251229042000_AddUniqueVigenteIndexProductosPrecios")]
    public partial class AddUniqueVigenteIndexProductosPrecios : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure there is at most one vigente row per (ProductoId, ListaId)
            // before adding the unique filtered index.
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT
        [Id],
        [ProductoId],
        [ListaId],
        ROW_NUMBER() OVER (PARTITION BY [ProductoId], [ListaId] ORDER BY [VigenciaDesde] DESC, [Id] DESC) AS [rn],
        MAX([VigenciaDesde]) OVER (PARTITION BY [ProductoId], [ListaId]) AS [MaxVigencia]
    FROM [dbo].[ProductosPrecios]
    WHERE [IsDeleted] = 0 AND [EsVigente] = 1
)
UPDATE pp
SET
    [EsVigente] = 0,
    [VigenciaHasta] = DATEADD(SECOND, -1, r.[MaxVigencia])
FROM [dbo].[ProductosPrecios] pp
INNER JOIN Ranked r ON r.[Id] = pp.[Id]
WHERE r.[rn] > 1;
");

                migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ProductosPrecios_ProductoId_ListaId_Vigente'
      AND object_id = OBJECT_ID(N'[dbo].[ProductosPrecios]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductosPrecios_ProductoId_ListaId_Vigente]
    ON [dbo].[ProductosPrecios]([ProductoId], [ListaId])
    WHERE [IsDeleted] = 0 AND [EsVigente] = 1;
END
");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT
        Id,
        ProductoId,
        ListaId,
        ROW_NUMBER() OVER (PARTITION BY ProductoId, ListaId ORDER BY VigenciaDesde DESC, Id DESC) AS rn,
        MAX(VigenciaDesde) OVER (PARTITION BY ProductoId, ListaId) AS MaxVigencia
    FROM ProductosPrecios
    WHERE IsDeleted = 0 AND EsVigente = 1
)
UPDATE ProductosPrecios
SET
    EsVigente = 0,
    VigenciaHasta = datetime((SELECT MaxVigencia FROM Ranked WHERE Ranked.Id = ProductosPrecios.Id), '-1 second')
WHERE Id IN (SELECT Id FROM Ranked WHERE rn > 1);
");

                migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductosPrecios_ProductoId_ListaId_Vigente
ON ProductosPrecios(ProductoId, ListaId)
WHERE IsDeleted = 0 AND EsVigente = 1;
");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ProductosPrecios_ProductoId_ListaId_Vigente'
      AND object_id = OBJECT_ID(N'[dbo].[ProductosPrecios]')
)
BEGIN
    DROP INDEX [IX_ProductosPrecios_ProductoId_ListaId_Vigente] ON [dbo].[ProductosPrecios];
END
");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS IX_ProductosPrecios_ProductoId_ListaId_Vigente;");
            }
        }
    }
}

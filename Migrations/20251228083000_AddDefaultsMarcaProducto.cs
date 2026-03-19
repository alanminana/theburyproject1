using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheBuryProject.Data;

#nullable disable

namespace TheBuryProject.Migrations
{
		[DbContext(typeof(AppDbContext))]
		[Migration("20251228083000_AddDefaultsMarcaProducto")]
		public partial class AddDefaultsMarcaProducto : Migration
		{
				protected override void Up(MigrationBuilder migrationBuilder)
				{
						// Marca.Activo default = 1
						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Marcas]')
	AND [c].[name] = N'Activo';

IF @df IS NULL
BEGIN
		ALTER TABLE [dbo].[Marcas] ADD CONSTRAINT [DF_Marcas_Activo] DEFAULT ((1)) FOR [Activo];
END
");

						// Producto.UnidadMedida default = 'UN'
						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Productos]')
	AND [c].[name] = N'UnidadMedida';

IF @df IS NULL
BEGIN
		ALTER TABLE [dbo].[Productos] ADD CONSTRAINT [DF_Productos_UnidadMedida] DEFAULT (N'UN') FOR [UnidadMedida];
END
");

						// Producto.Activo default = 1
						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Productos]')
	AND [c].[name] = N'Activo';

IF @df IS NULL
BEGIN
		ALTER TABLE [dbo].[Productos] ADD CONSTRAINT [DF_Productos_Activo] DEFAULT ((1)) FOR [Activo];
END
");
				}

				protected override void Down(MigrationBuilder migrationBuilder)
				{
						// Drop defaults if present (regardless of the constraint name)
						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Marcas]')
	AND [c].[name] = N'Activo';

IF @df IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Marcas] DROP CONSTRAINT [' + @df + '];');
");

						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Productos]')
	AND [c].[name] = N'UnidadMedida';

IF @df IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Productos] DROP CONSTRAINT [' + @df + '];');
");

						migrationBuilder.Sql(@"
DECLARE @df sysname;
SELECT @df = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c]
		ON [d].[parent_object_id] = [c].[object_id]
		AND [d].[parent_column_id] = [c].[column_id]
WHERE [d].[parent_object_id] = OBJECT_ID(N'[dbo].[Productos]')
	AND [c].[name] = N'Activo';

IF @df IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Productos] DROP CONSTRAINT [' + @df + '];');
");
				}
		}
}

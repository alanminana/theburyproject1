using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace TheBuryProyect.Migrations
{
    /// <summary>
    /// SQL crudo que se ejecuta como batch propio dentro de la migracion.
    /// </summary>
    /// <remarks>
    /// En un script <c>--idempotent</c> EF emite cada migracion dentro de un unico batch <c>IF NOT EXISTS (...) BEGIN ... END</c>:
    /// un <c>UPDATE</c> que referencia una columna agregada (<c>ADD</c>) por la misma migracion falla en compilacion
    /// ("Invalid column name"), y dos <c>DECLARE</c> de la misma variable en la misma migracion colisionan.
    /// Envolver el SQL en <c>EXEC(N'...')</c> lo compila recien al ejecutarse y le da su propio ambito de variables.
    /// El efecto sobre la base es identico al de <c>MigrationBuilder.Sql</c>; no cambia el ID ni el snapshot de ninguna migracion.
    /// </remarks>
    public static class MigrationBuilderSqlExtensions
    {
        public static OperationBuilder<SqlOperation> SqlDiferido(this MigrationBuilder migrationBuilder, string sql)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrWhiteSpace(sql);
            return migrationBuilder.Sql("EXEC(N'" + sql.Replace("'", "''") + "');");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TheBuryProyect.Migrations;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// El SQL de backfill de las migraciones debe poder emitirse en un script <c>--idempotent</c> (un unico batch por migracion):
/// <c>SqlDiferido</c> lo envuelve en <c>EXEC(N'...')</c> escapando las comillas sin alterar su texto.
/// </summary>
public sealed class MigrationBuilderSqlDiferidoTests
{
    [Fact]
    public void SqlDiferido_EnvuelveEnExecYDuplicaComillasSimples()
    {
        var builder = new MigrationBuilder(activeProvider: "Microsoft.EntityFrameworkCore.SqlServer");

        builder.SqlDiferido("UPDATE [T] SET [C] = 'a''b' WHERE [D] = N'x';");

        var op = Assert.IsType<SqlOperation>(Assert.Single(builder.Operations));
        Assert.Equal("EXEC(N'UPDATE [T] SET [C] = ''a''''b'' WHERE [D] = N''x'';');", op.Sql);
    }

    [Fact]
    public void SqlDiferido_RechazaSqlVacio()
    {
        var builder = new MigrationBuilder(activeProvider: "Microsoft.EntityFrameworkCore.SqlServer");

        Assert.Throws<ArgumentException>(() => builder.SqlDiferido("  "));
    }
}

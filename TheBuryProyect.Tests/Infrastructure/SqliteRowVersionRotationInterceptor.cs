using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Tests.Infrastructure;

/// <summary>
/// PUN-ML9-D.1 (riesgo 2): SQL Server regenera <c>rowversion</c> automáticamente en cada UPDATE;
/// SQLite (usado como base de los tests) sólo lo genera al insertar (ver
/// <c>AppDbContext.OnModelCreating</c>, <c>ValueGeneratedOnAdd()</c> para providers no-SqlServer)
/// — sin esto, dos UPDATE sucesivos sobre la misma fila conservan idéntico <c>RowVersion</c> bajo
/// SQLite, y ningún caso de concurrencia optimista (aplicación o EF) puede detectar el cambio.
/// Antes esta rotación vivía dentro de <c>CreditoService.RegistrarPagoCuotaAsync</c> gateada por
/// <c>ProviderName.Contains("Sqlite")</c> — un chequeo de proveedor de base de datos dentro del
/// servicio financiero productivo, sólo para acomodar a los tests. Se movió acá: infraestructura
/// exclusiva de tests, sin que ningún servicio productivo conozca el proveedor de base de datos.
/// No altera en nada la semántica de SQL Server (el chequeo de proveedor vive en el interceptor,
/// no en el servicio, y sólo se registra en <see cref="CustomWebApplicationFactory"/> y en los
/// tests que construyen su propio <c>AppDbContext</c> con SQLite y ejercitan concurrencia real).
/// </summary>
public sealed class SqliteRowVersionRotationInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        RotarRowVersionesModificadas(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RotarRowVersionesModificadas(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RotarRowVersionesModificadas(DbContext? context)
    {
        if (context is null || !context.Database.IsSqlite())
            return;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State != EntityState.Modified)
                continue;

            entry.Entity.RowVersion = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            entry.Property(e => e.RowVersion).IsModified = true;
        }
    }
}

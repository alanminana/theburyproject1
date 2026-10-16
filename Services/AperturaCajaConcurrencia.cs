using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Recurso de serialización COMPARTIDO entre el cierre de caja y todo alta de <c>MovimientosCaja</c>.
    /// El recurso es la fila de <c>AperturasCaja</c> (H-CAJA-1).
    ///
    /// Por qué existe: ni el cierre ni los asientos tomaban ningún lock común. Un movimiento podía validar
    /// "apertura abierta", el cierre calcular sus totales y marcar <c>Cerrada</c>, y el movimiento insertarse
    /// después en una apertura ya cerrada, fuera del snapshot congelado del cierre.
    ///
    /// Reglas de uso:
    ///   1. Todo escritor de movimientos y el cierre toman ESTE lock y luego releen el estado de la apertura
    ///      desde la base (no del change tracker) antes de validar o calcular.
    ///   2. Es el ÚLTIMO lock de la cadena Crédito → Venta → Apertura: ningún flujo toma Crédito o Venta
    ///      reteniendo la apertura, así que no cierra ciclos con CONC-P0.
    ///   3. Debe invocarse con una transacción abierta: el lock vive hasta el commit/rollback.
    ///
    /// SQL Server: <c>UPDLOCK, HOLDLOCK, ROWLOCK</c> sobre la clave primaria. UPDLOCK hace que la lectura tome un
    /// lock de actualización aun con READ_COMMITTED_SNAPSHOT (el modo por versiones sólo afecta a lecturas sin
    /// hint), y dos locks de actualización sobre la misma fila son incompatibles: exclusión mutua en RCSI 0 y 1.
    /// Los lectores sin hint no se bloquean. Otros proveedores (SQLite de tests): un único escritor ya serializa
    /// todo, no-op.
    /// </summary>
    internal static class AperturaCajaConcurrencia
    {
        /// <summary>Toma el lock de actualización de la fila de la apertura hasta el fin de la transacción.</summary>
        public static async Task BloquearAsync(AppDbContext context, int aperturaId, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsSqlServer())
                return;

            await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [AperturasCaja] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {aperturaId}")
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Apertura leída de la BASE (no del change tracker): una entidad ya cargada antes de esperar el lock
        /// podría estar obsoleta. <c>null</c> si no existe o está eliminada.
        /// </summary>
        public static Task<AperturaCaja?> LeerAsync(AppDbContext context, int aperturaId, CancellationToken cancellationToken = default) =>
            context.AperturasCaja
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == aperturaId && !a.IsDeleted, cancellationToken);
    }
}

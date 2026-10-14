using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Recurso de serialización COMPARTIDO entre los flujos que cambian el estado financiero de un
    /// crédito: registrar un pago (individual, adelanto, primera cuota, múltiple) y cancelar la venta
    /// o el crédito. El recurso es la fila de <c>Creditos</c>.
    ///
    /// Por qué existe: la cancelación de venta serializaba solo contra otras cancelaciones (lock de la
    /// fila de <c>Ventas</c>) y el pago no leía <c>Ventas</c>; entre la guardia "no hay pagos" y la
    /// carga de las cuotas quedaba una ventana en la que un pago podía confirmar y la cancelación
    /// seguía igual (venta+crédito Cancelados con un pago aplicado sin revertir).
    ///
    /// Reglas de uso (orden de locks, para no generar deadlocks):
    ///   1. El lock del crédito se toma SIEMPRE PRIMERO, antes de leer o escribir cuotas, pagos o caja.
    ///   2. La cancelación de venta toma Crédito → Venta (no Venta → Crédito): el pago no toma Venta
    ///      pero puede leerla más adelante (emisión de documentos), así que ese es el único orden
    ///      consistente con el del pago.
    ///   3. Si hay varios créditos, se toman en orden ascendente de Id.
    ///   4. Debe invocarse con una transacción abierta: el lock vive hasta el commit/rollback.
    ///
    /// SQL Server: <c>UPDLOCK, HOLDLOCK, ROWLOCK</c> sobre la clave primaria (lock de actualización
    /// sobre una sola fila; no bloquea lectores en READ COMMITTED y, con READ_COMMITTED_SNAPSHOT,
    /// tampoco). Otros proveedores (SQLite de tests): un único escritor ya serializa todo, no-op.
    /// </summary>
    internal static class CreditoConcurrencia
    {
        /// <summary>Toma el lock de actualización de la fila del crédito hasta el fin de la transacción.</summary>
        public static async Task BloquearAsync(AppDbContext context, int creditoId, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsSqlServer())
                return;

            await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [Creditos] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {creditoId}")
                .ToListAsync(cancellationToken);
        }

        /// <summary>Toma los locks de varios créditos en orden ascendente (orden global consistente).</summary>
        public static async Task BloquearOrdenadosAsync(
            AppDbContext context, IEnumerable<int> creditoIds, CancellationToken cancellationToken = default)
        {
            foreach (var id in creditoIds.Distinct().OrderBy(x => x))
                await BloquearAsync(context, id, cancellationToken);
        }

        /// <summary>
        /// Estado del crédito leído de la BASE (no del change tracker): una entidad ya cargada antes
        /// de esperar el lock podría estar obsoleta. <c>null</c> si el crédito no existe.
        /// </summary>
        public static async Task<EstadoCredito?> ObtenerEstadoAsync(
            AppDbContext context, int creditoId, CancellationToken cancellationToken = default)
        {
            var estado = await context.Creditos
                .AsNoTracking()
                .Where(c => c.Id == creditoId)
                .Select(c => (EstadoCredito?)c.Estado)
                .FirstOrDefaultAsync(cancellationToken);
            return estado;
        }

        /// <summary>
        /// Estados en los que el crédito no admite cobros. Finalizado queda fuera a propósito: no tiene deuda
        /// exigible y el cobro ya se rechaza por "cuota pagada" con el contrato de error existente.
        /// </summary>
        public static bool NoAdmiteCobros(EstadoCredito estado) =>
            estado is EstadoCredito.Cancelado or EstadoCredito.Rechazado;
    }
}

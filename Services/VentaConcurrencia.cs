using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Recurso de serialización COMPARTIDO entre los flujos que deciden si una venta admite o no operaciones
    /// financieras sobre ella: cancelar la venta, crear una devolución y completar una devolución. El recurso
    /// es la fila de <c>Ventas</c>.
    ///
    /// Por qué existe (FIN-DEV-P0, H-DEV-1/2/3): cancelar y devolver se excluyen por regla comercial (no se
    /// cancela una venta con devoluciones vivas; no se devuelve una venta cancelada). Esa regla solo vale si
    /// ambas operaciones se serializan: con un simple AnyAsync en READ COMMITTED, una cancelación y una
    /// devolución simultáneas leen "no hay nada" y las dos confirman.
    ///
    /// Reglas de uso (orden de locks, para no generar deadlocks):
    ///   1. Orden global: Crédito → Venta → (stock, caja, devoluciones). La cancelación ya toma el crédito
    ///      primero; las operaciones de devolución no tocan el crédito y toman la Venta primero.
    ///   2. El lock se toma ANTES de leer lo que se va a validar (estado de la venta, devoluciones, cantidades
    ///      ya devueltas) y vive hasta el commit/rollback de la transacción; con transacción ambiental dura
    ///      hasta el commit de la externa.
    ///   3. Debe invocarse con una transacción abierta.
    ///
    /// SQL Server: <c>UPDLOCK, HOLDLOCK, ROWLOCK</c> sobre la PK. Es incompatible con otro UPDLOCK y con el
    /// lock exclusivo del <c>UPDATE</c> de la cancelación, y la lectura bloquea también con
    /// READ_COMMITTED_SNAPSHOT (UPDLOCK fuerza lectura con locks). Otros proveedores (SQLite de tests): un
    /// único escritor ya serializa, se lee el estado sin hint.
    /// </summary>
    internal static class VentaConcurrencia
    {
        /// <summary>
        /// Toma el lock de actualización de la fila de la venta hasta el fin de la transacción y devuelve su
        /// estado leído de la BASE bajo ese lock (no del change tracker). <c>null</c> si no existe o está borrada.
        /// </summary>
        public static async Task<EstadoVenta?> BloquearYObtenerEstadoAsync(
            AppDbContext context, int ventaId, CancellationToken cancellationToken = default)
        {
            if (context.Database.IsSqlServer())
            {
                var estados = await context.Database
                    .SqlQuery<int>($"SELECT [Estado] AS [Value] FROM [Ventas] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {ventaId} AND [IsDeleted] = 0")
                    .ToListAsync(cancellationToken);
                return estados.Count == 0 ? null : (EstadoVenta)estados[0];
            }

            return await context.Ventas
                .AsNoTracking()
                .Where(v => v.Id == ventaId && !v.IsDeleted)
                .Select(v => (EstadoVenta?)v.Estado)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}

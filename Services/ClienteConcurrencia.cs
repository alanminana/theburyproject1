using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// H-24: serializa la BAJA de un cliente contra el ALTA de sus créditos. El recurso compartido es la fila de
    /// <c>Clientes</c>: ambos flujos toman el lock de actualización antes de leer/escribir, de modo que
    /// "no tiene créditos vigentes" (baja) y "el cliente está activo" (alta de crédito) no se evalúan sobre estados
    /// intermedios: o el crédito se crea y la baja se rechaza, o la baja se confirma y el crédito se rechaza.
    ///
    /// Orden de locks: Cliente primero, nunca después de Crédito/Venta (el cobro y la cancelación toman Crédito y no
    /// leen Clientes con lock), así que no introduce deadlocks. Debe invocarse con una transacción abierta.
    /// SQL Server: <c>UPDLOCK, HOLDLOCK, ROWLOCK</c> sobre la PK; otros proveedores (SQLite de tests): no-op.
    /// </summary>
    internal static class ClienteConcurrencia
    {
        public static async Task BloquearAsync(AppDbContext context, int clienteId, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsSqlServer())
                return;

            await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [Clientes] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {clienteId}")
                .ToListAsync(cancellationToken);
        }

        /// <summary>Estados de crédito que ya no tienen deuda ni gestión pendiente: permiten dar de baja al cliente.</summary>
        public static bool EsCreditoCerrado(EstadoCredito estado) =>
            estado is EstadoCredito.Finalizado or EstadoCredito.Rechazado or EstadoCredito.Cancelado;

        /// <summary>Lee de la base (no del tracker) si el cliente sigue activo (existe y no está dado de baja).</summary>
        public static Task<bool> EstaActivoAsync(AppDbContext context, int clienteId, CancellationToken cancellationToken = default) =>
            context.Clientes.AsNoTracking().AnyAsync(c => c.Id == clienteId && !c.IsDeleted, cancellationToken);
    }
}

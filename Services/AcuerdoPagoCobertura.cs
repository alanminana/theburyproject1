using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Consultas de cobertura de un acuerdo de pago sobre las cuotas originales (deuda congelada al activarlo). Las usan los
    /// circuitos que mueven esas cuotas (cobro y punitorios) para respetar la regla: mientras el acuerdo está Activo ni se
    /// cobran por otro circuito ni se aplican o anulan punitorios, y una vez Cumplido no se aplican punitorios retroactivos.
    /// Se invocan DENTRO de la transacción del circuito, ya bajo el lock del crédito.
    /// </summary>
    internal static class AcuerdoPagoCobertura
    {
        public static Task<AcuerdoPago?> ActivoQueCubreAsync(AppDbContext context, int cuotaId, CancellationToken cancellationToken = default) =>
            QueCubreAsync(context, cuotaId, EstadoAcuerdo.Activo, cancellationToken);

        public static Task<AcuerdoPago?> CumplidoQueCubreAsync(AppDbContext context, int cuotaId, CancellationToken cancellationToken = default) =>
            QueCubreAsync(context, cuotaId, EstadoAcuerdo.Cumplido, cancellationToken);

        private static Task<AcuerdoPago?> QueCubreAsync(AppDbContext context, int cuotaId, EstadoAcuerdo estado, CancellationToken cancellationToken) =>
            (from cubierta in context.AcuerdoPagoCuotasCubiertas.AsNoTracking()
             join acuerdo in context.AcuerdosPago.AsNoTracking() on cubierta.AcuerdoPagoId equals acuerdo.Id
             where cubierta.CuotaId == cuotaId && !cubierta.IsDeleted && !acuerdo.IsDeleted && acuerdo.Estado == estado
             select acuerdo).FirstOrDefaultAsync(cancellationToken);

        /// <summary>
        /// ¿El cobro de esta cuota lo gestiona el propio acuerdo? Solo si la identidad de la operación coincide con una
        /// intención Pendiente de ese acuerdo para esa cuota. La identidad nunca viene de un cliente HTTP.
        /// </summary>
        public static Task<bool> EsCobroGestionadoPorElAcuerdoAsync(
            AppDbContext context, int acuerdoId, int cuotaId, Guid? operacionId, CancellationToken cancellationToken = default) =>
            operacionId.HasValue
                ? context.AcuerdoPagoImputaciones.AsNoTracking().AnyAsync(i =>
                    i.AcuerdoPagoId == acuerdoId && i.CuotaCreditoId == cuotaId && i.OperacionId == operacionId.Value
                    && i.Estado == EstadoImputacionAcuerdo.Pendiente && !i.IsDeleted, cancellationToken)
                : Task.FromResult(false);
    }
}

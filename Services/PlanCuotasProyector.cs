using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>Cuota proyectada de un crédito cuyo plan todavía no está persistido.</summary>
    public sealed record PlanCuotaProyectada(int Numero, decimal Capital, decimal Interes, decimal Total, DateTime Vencimiento, string Estado = "Pendiente");

    public interface IPlanCuotasProyector
    {
        /// <summary>
        /// Plan vigente de un crédito: sus cuotas persistidas tal cual (histórico cerrado) o, si todavía no existen,
        /// la proyección con el mismo cálculo canónico que usa la venta al generar las cuotas.
        /// </summary>
        Task<IReadOnlyList<PlanCuotaProyectada>> ObtenerPlanAsync(Venta venta, Credito credito);
    }

    /// <summary>
    /// Única fuente de la proyección del plan de cuotas para documentos (contrato, pagaré, presupuesto): el importe que
    /// muestra un documento es el mismo que luego se persiste en las cuotas.
    /// </summary>
    public class PlanCuotasProyector : IPlanCuotasProyector
    {
        private readonly IFinancialCalculationService _financialService;
        private readonly IConfiguracionPagoService _configuracionPagoService;

        public PlanCuotasProyector(IFinancialCalculationService financialService, IConfiguracionPagoService configuracionPagoService)
        {
            _financialService = financialService;
            _configuracionPagoService = configuracionPagoService;
        }

        public async Task<IReadOnlyList<PlanCuotaProyectada>> ObtenerPlanAsync(Venta venta, Credito credito)
        {
            // Las cuotas eliminadas pertenecen a una configuración anterior y no impiden proyectar el plan vigente.
            var persistidas = credito.Cuotas.Where(c => !c.IsDeleted).OrderBy(c => c.NumeroCuota).ToList();
            if (persistidas.Count > 0)
            {
                return persistidas
                    .Select(c => new PlanCuotaProyectada(c.NumeroCuota, c.MontoCapital, c.MontoInteres, c.MontoTotal, c.FechaVencimiento, c.Estado.ToString()))
                    .ToList();
            }

            if (credito.CantidadCuotas <= 0 || credito.MontoAprobado <= 0 || !credito.FechaPrimeraCuota.HasValue)
                return Array.Empty<PlanCuotaProyectada>();

            // La re-simulación recibe el mismo CuotasSinRecargo del plan global resuelto para esta cantidad:
            // nunca se infiere de Credito.TasaInteres ni se reconstruye a partir de importes.
            var cuotasSinRecargo = await ResolverCuotasSinRecargoAsync(venta, credito);

            var fecha = credito.FechaPrimeraCuota.Value.Date;
            var simulacion = _financialService.SimularPlanCredito(
                credito.MontoAprobado, 0m, credito.CantidadCuotas, credito.TasaInteres, 0m, fecha,
                cuotasSinRecargo: cuotasSinRecargo);

            var plan = new List<PlanCuotaProyectada>();
            foreach (var item in simulacion.Cuotas)
            {
                plan.Add(new PlanCuotaProyectada(item.NumeroCuota, item.Capital, item.Interes, item.Total, fecha));
                fecha = fecha.AddMonths(1);
            }

            return plan;
        }

        /// <summary>
        /// Resuelve el plan global de la venta para <c>credito.CantidadCuotas</c> y devuelve su <c>CuotasSinRecargo</c>.
        /// Vacía (sin exclusiones, comportamiento histórico) cuando no hay productos, rige la configuración única global
        /// o el plan resuelto ya no cubre esta cantidad: este método solo proyecta un plan aún no confirmado.
        /// </summary>
        private async Task<IReadOnlyList<int>> ResolverCuotasSinRecargoAsync(Venta venta, Credito credito)
        {
            var productoIds = venta.Detalles.Where(d => !d.IsDeleted).Select(d => d.ProductoId).Distinct().ToArray();
            if (productoIds.Length == 0)
                return Array.Empty<int>();

            var planesVenta = await _configuracionPagoService.ResolverPlanesCreditoPersonalAsync(productoIds);
            if (!planesVenta.EsValido || planesVenta.RigeConfiguracionUnicaGlobal)
                return Array.Empty<int>();

            return planesVenta.BuscarPlan(credito.CantidadCuotas)?.CuotasSinRecargo ?? Array.Empty<int>();
        }
    }
}

using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IConfiguracionPagoService
    {
        Task<List<ConfiguracionPagoViewModel>> GetAllAsync();
        Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id);
        Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago);
        /// <summary>
        /// Retorna la tasa mensual de crédito personal configurada.
        /// Retorna null si no existe configuración o si la tasa es 0 o no está definida:
        /// en esos casos la operación debe bloquearse en el caller.
        /// </summary>
        Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync();
        Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel);
        Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel);
        Task<bool> DeleteAsync(int id);

        Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync();
        Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync();
        Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id);
        Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento);
        Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto);

        /// <summary>
        /// Obtiene todos los perfiles de crédito (no eliminados), ordenados.
        /// </summary>
        Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync();

        /// <summary>
        /// Obtiene solo los perfiles de crédito activos (no eliminados), ordenados.
        /// </summary>
        Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync();

        /// <summary>
        /// Guarda defaults globales de crédito personal y perfiles (crear/actualizar).
        /// </summary>
        Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config);

        /// <summary>
        /// Resuelve los parámetros de crédito aplicables para un cliente según la cadena de prioridad:
        /// Personalizado por cliente > Perfil preferido del cliente > Global.
        /// </summary>
        Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal);

        /// <summary>
        /// Carga las entidades necesarias desde DB y resuelve el rango de cuotas permitidas
        /// para el método de cálculo especificado. Delega la lógica pura a CreditoConfiguracionHelper.
        /// También devuelve el nombre del perfil aplicado (null si no aplica).
        /// </summary>
        Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo,
            int? perfilId,
            int? clienteId);

        /// <summary>
        /// Calcula el máximo efectivo de cuotas sin interés para una tarjeta y un conjunto de productos.
        /// Devuelve null si la tarjeta no existe, no está activa, o no es TipoCuota.SinInteres.
        /// El resultado es min(tarjeta.CantidadMaximaCuotas, min(productos.MaxCuotasSinInteresPermitidas)).
        /// Productos sin restricción (null) no participan en el mínimo.
        /// </summary>
        Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId,
            IEnumerable<int> productoIds);

        /// <summary>
        /// Carga la configuración de monto por puntaje 0–10, garantizando 11 filas.
        /// Si faltan filas se inicializan en memoria con $0.
        /// </summary>
        Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync();

        /// <summary>
        /// Guarda la tabla de montos por puntaje 0–10.
        /// Actualiza filas existentes; crea las faltantes. No borra físico.
        /// </summary>
        Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(
            List<MontoPorPuntajeCreditoViewModel> items,
            string usuario);

        /// <summary>
        /// Carga todas las cuotas de Crédito Personal configuradas (activas e inactivas), para administración.
        /// </summary>
        Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync();

        /// <summary>
        /// Carga solo las cuotas de Crédito Personal activas, ordenadas por cantidad de cuotas.
        /// Lista vacía significa que no hay tabla configurada: los llamadores deben usar la tasa/rango global únicos.
        /// </summary>
        Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync();

        /// <summary>
        /// Porcentaje de ajuste del medio de pago para operaciones en un pago
        /// (positivo = recargo, negativo = descuento). Usa el plan general de 1 cuota del
        /// medio; si no existe, cae al recargo global del medio. Default 0 para stubs.
        /// </summary>
        Task<decimal> ObtenerPorcentajeAjusteUnPagoAsync(TipoPago tipoPago) => Task.FromResult(0m);

        /// <summary>
        /// Resuelve las cuotas efectivas de Crédito Personal para un conjunto de productos.
        /// Prioridad: planes específicos del producto → planes globales → tasa global única.
        /// Un producto sin planes propios hereda la configuración global; con varios productos
        /// la cantidad debe estar habilitada para todos y se aplica la tasa más alta (conservadora).
        /// </summary>
        Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalEfectivasAsync(IEnumerable<int> productoIds);

        /// <summary>
        /// Guarda la tabla de cuotas de Crédito Personal (cantidad + tasa mensual + activo).
        /// Actualiza filas existentes; crea las faltantes. No borra físico.
        /// </summary>
        Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(
            List<CuotaCreditoPersonalViewModel> items,
            string usuario);
    }
}

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

        /// <summary>
        /// Upsert transaccional de múltiples configuraciones de pago (una sola operación DB).
        /// </summary>
        Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones);
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
    }
}

using TheBuryProject.Models.Entities;
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
        /// Fuente canónica del porcentaje de recargo TOTAL único global de Crédito Personal
        /// (no una tasa mensual ni compuesta). Retorna null solo si no existe configuración
        /// persistida o si nunca fue definida: en esos casos la operación debe bloquearse en
        /// el caller. Un valor configurado de 0 (recargo cero, válido) NO retorna null.
        /// </summary>
        /// <remarks>
        /// Fallback financiero sobre planes de cuota: una fila global activa con porcentaje
        /// propio null hereda este valor al resolverse contra una venta (ver
        /// <c>ConfiguracionPagoService.ResolverPlanesCreditoPersonalAsync</c>). Sin fila global
        /// para esa cantidad (solo config de producto) sigue sin ser autoridad. También se usa
        /// como tasa efectiva cuando no existe ninguna tabla de planes en absoluto
        /// (compatibilidad de dobles de test) y como gate de "tasa global no configurada".
        /// </remarks>
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
        /// Resolución canónica de los planes de Crédito Personal de una venta.
        /// Prioridad: configuración personalizada del producto → configuración global. La
        /// personalizada REEMPLAZA a la global para ese producto. Con varios productos la
        /// cantidad debe estar habilitada para todos (intersección) y se aplica la tasa más alta.
        /// </summary>
        /// <remarks>
        /// El resultado distingue explícitamente "no hay tabla de planes" (rige la configuración
        /// única global) de "la intersección es vacía" (la venta no es financiable). Nunca
        /// interpretar una lista de planes vacía como permiso para usar el rango o la tasa global.
        /// </remarks>
        Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds);

        /// <summary>
        /// Guarda la tabla de cuotas de Crédito Personal (cantidad + tasa mensual + activo).
        /// Actualiza filas existentes; crea las faltantes. No borra físico.
        /// </summary>
        Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(
            List<CuotaCreditoPersonalViewModel> items,
            string usuario);

        /// <summary>
        /// Números de cuota que un plan global de Crédito Personal (<see cref="ConfiguracionCreditoPersonalCuota"/>)
        /// tiene marcados como "sin recargo comercial" (CSR-ML2), ordenados ascendente. Lista
        /// vacía = ninguna cuota marcada (default para todos los planes existentes, comportamiento
        /// actual preservado). Devuelve lista vacía también si el plan indicado no existe.
        /// </summary>
        /// <remarks>
        /// Default vacío para no romper implementaciones/stubs existentes (mismo estilo que
        /// <see cref="ObtenerPorcentajeAjusteUnPagoAsync"/>): ningún caller de producción de
        /// CSR-ML2 lo consume todavía (eso es CSR-ML3+), así que el default no puede enmascarar
        /// una regresión real.
        /// </remarks>
        Task<IReadOnlyList<int>> GetCuotasSinRecargoAsync(int configuracionCreditoPersonalCuotaId) =>
            Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        /// <summary>
        /// Reemplaza por completo la selección de cuotas sin recargo de un plan global (CSR-ML2).
        /// Valida: cada número en [1, CantidadCuotas] del plan, sin duplicados, y — cuando el plan
        /// tiene <c>TasaMensual</c> explícito mayor a 0 — que quede al menos una cuota con recargo
        /// (con <c>TasaMensual</c> = 0 puede marcarse la totalidad; con <c>TasaMensual</c> null el
        /// plan ya es inválido por el contrato ML2.1 ya congelado, no reabierto aquí). No persiste
        /// nada si hay errores de validación.
        /// </summary>
        /// <remarks>
        /// Default "no-op" (mismo motivo que <see cref="GetCuotasSinRecargoAsync"/>) para no
        /// romper implementaciones/stubs existentes; la implementación real vive en
        /// <c>ConfiguracionPagoService</c>.
        /// </remarks>
        Task<(bool Ok, List<string> Errores)> GuardarCuotasSinRecargoCreditoPersonalAsync(
            int configuracionCreditoPersonalCuotaId,
            IReadOnlyList<int> numerosCuota,
            string usuario) =>
            Task.FromResult((false, new List<string> { "GuardarCuotasSinRecargoCreditoPersonalAsync no implementado." }));
    }
}

using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio de gestión de promesas de pago.
    /// - Registro con auditoría
    /// - Verificación de cumplimiento
    /// - Basado en configuración
    /// </summary>
    public interface IPromesaPagoService
    {
        /// <summary>
        /// Registra una promesa de pago para una alerta de cobranza.
        /// Crea historial de contacto y actualiza la alerta.
        /// </summary>
        /// <param name="promesa">Datos de la promesa</param>
        /// <param name="gestorId">Id del usuario que registra</param>
        /// <returns>Resultado del registro</returns>
        Task<ResultadoPromesaPago> RegistrarPromesaAsync(
            PromesaPagoDto promesa,
            string gestorId);

        /// <summary>
        /// Obtiene las promesas vencidas pendientes de seguimiento.
        /// </summary>
        /// <param name="config">Configuración de mora (para tolerancia)</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>Lista de promesas vencidas</returns>
        Task<IReadOnlyList<PromesaVencidaDto>> ObtenerPromesasVencidasAsync(
            ConfiguracionMora config,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Marca una promesa como incumplida y actualiza el estado de la alerta.
        /// </summary>
        /// <param name="alertaId">Id de la alerta con promesa incumplida</param>
        /// <param name="gestorId">Usuario que marca el incumplimiento</param>
        /// <param name="observaciones">Notas adicionales</param>
        /// <returns>True si se actualizó correctamente</returns>
        Task<bool> MarcarPromesaIncumplidaAsync(
            int alertaId,
            string gestorId,
            string? observaciones = null);

        /// <summary>
        /// Marca una promesa como cumplida (cliente pagó).
        /// </summary>
        /// <param name="alertaId">Id de la alerta</param>
        /// <param name="gestorId">Usuario que registra</param>
        /// <param name="montoPagado">Monto efectivamente pagado</param>
        /// <returns>True si se actualizó correctamente</returns>
        Task<bool> MarcarPromesaCumplidaAsync(
            int alertaId,
            string gestorId,
            decimal montoPagado);

        /// <summary>
        /// Verifica si una promesa está próxima a vencer.
        /// </summary>
        /// <param name="alerta">Alerta a verificar</param>
        /// <param name="diasAnticipacion">Días de anticipación para alertar</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>True si vence dentro del plazo indicado</returns>
        bool EstaProximaAVencer(
            AlertaCobranza alerta,
            int diasAnticipacion,
            DateTime? fechaCalculo = null);

        /// <summary>
        /// Verifica si una promesa ya está vencida.
        /// </summary>
        /// <param name="alerta">Alerta a verificar</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>True si la promesa venció</returns>
        bool EstaVencida(AlertaCobranza alerta, DateTime? fechaCalculo = null);

        /// <summary>
        /// Calcula los días restantes para el vencimiento de la promesa.
        /// </summary>
        /// <param name="alerta">Alerta con promesa</param>
        /// <param name="fechaCalculo">Fecha de referencia (default: hoy)</param>
        /// <returns>Días restantes (negativo si ya venció)</returns>
        int DiasParaVencimiento(AlertaCobranza alerta, DateTime? fechaCalculo = null);
    }
}

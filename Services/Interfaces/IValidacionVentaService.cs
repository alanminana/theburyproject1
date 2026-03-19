using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio de validación unificada para ventas con crédito personal.
    /// Consolida la evaluación de documentación, cupo, mora y permisos.
    /// </summary>
    public interface IValidacionVentaService
    {
        /// <summary>
        /// Prevalida si un cliente puede recibir crédito para el monto especificado.
        /// NO persiste nada; solo devuelve resultado para informar en UI.
        /// Evalúa: documentación, cupo/límite, mora, estado de aptitud.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <param name="monto">Monto a validar</param>
        /// <returns>Resultado de prevalidación: Aprobable, RequiereAutorizacion, o NoViable</returns>
        Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto);

        /// <summary>
        /// Valida si una venta con crédito personal puede proceder.
        /// Evalúa documentación, cupo disponible, mora y estado de aptitud del cliente.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <param name="montoVenta">Monto total de la venta</param>
        /// <param name="creditoId">ID del crédito (opcional, si ya existe)</param>
        /// <returns>Resultado de validación con razones y requisitos</returns>
        Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
            int clienteId, 
            decimal montoVenta, 
            int? creditoId = null);

        /// <summary>
        /// Valida si una venta existente puede ser confirmada.
        /// Re-evalúa todos los requisitos antes de confirmar.
        /// </summary>
        /// <param name="ventaId">ID de la venta</param>
        /// <returns>Resultado de validación</returns>
        Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId);

        /// <summary>
        /// Verifica si el cliente puede recibir crédito para el monto especificado.
        /// Retorna true si puede proceder sin restricciones.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <param name="montoSolicitado">Monto solicitado</param>
        /// <returns>True si puede proceder sin restricciones</returns>
        Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado);

        /// <summary>
        /// Obtiene un resumen de la situación crediticia del cliente para mostrar en UI.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <returns>Resumen para mostrar en formulario de venta</returns>
        Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId);
    }

    /// <summary>
    /// Resumen de situación crediticia para mostrar en formulario de venta
    /// </summary>
    public class ResumenCrediticioClienteViewModel
    {
        /// <summary>
        /// Estado del semáforo de aptitud crediticia
        /// </summary>
        public string EstadoAptitud { get; set; } = "No Evaluado";

        /// <summary>
        /// Color del semáforo (success, warning, danger, secondary)
        /// </summary>
        public string ColorSemaforo { get; set; } = "secondary";

        /// <summary>
        /// Ícono a mostrar
        /// </summary>
        public string Icono { get; set; } = "bi-question-circle";

        /// <summary>
        /// Límite de crédito asignado
        /// </summary>
        public decimal? LimiteCredito { get; set; }

        /// <summary>
        /// Cupo disponible para nuevas ventas
        /// </summary>
        public decimal CupoDisponible { get; set; }

        /// <summary>
        /// Crédito ya utilizado
        /// </summary>
        public decimal CreditoUtilizado { get; set; }

        /// <summary>
        /// Indica si tiene documentación completa
        /// </summary>
        public bool DocumentacionCompleta { get; set; }

        /// <summary>
        /// Descripción de documentos faltantes
        /// </summary>
        public string? DocumentosFaltantes { get; set; }

        /// <summary>
        /// Indica si tiene mora activa
        /// </summary>
        public bool TieneMoraActiva { get; set; }

        /// <summary>
        /// Días de mora máximos
        /// </summary>
        public int? DiasMaxMora { get; set; }

        /// <summary>
        /// Indica si puede recibir crédito
        /// </summary>
        public bool PuedeRecibirCredito => 
            !string.IsNullOrEmpty(EstadoAptitud) && 
            !EstadoAptitud.StartsWith("No ", StringComparison.OrdinalIgnoreCase) &&
            !EstadoAptitud.Equals("Sin Evaluar", StringComparison.OrdinalIgnoreCase) &&
            (EstadoAptitud.Contains("Apto") || EstadoAptitud.Contains("Autorización"));

        /// <summary>
        /// Mensaje de advertencia para mostrar
        /// </summary>
        public string? MensajeAdvertencia { get; set; }

        /// <summary>
        /// Lista de créditos activos del cliente
        /// </summary>
        public List<CreditoActivoResumen> CreditosActivos { get; set; } = new();
    }

    /// <summary>
    /// Resumen de un crédito activo
    /// </summary>
    public class CreditoActivoResumen
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public decimal MontoAprobado { get; set; }
        public decimal SaldoDisponible { get; set; }
        public string Estado { get; set; } = string.Empty;
    }
}

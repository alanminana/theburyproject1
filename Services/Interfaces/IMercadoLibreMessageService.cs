using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Gestión de mensajes postventa de Mercado Libre dentro del ERP.
    /// Toda respuesta es manual. En ModoSimulacion (o fuera de Development real)
    /// nunca se llama a la API: el mensaje saliente se guarda local.
    /// </summary>
    public interface IMercadoLibreMessageService
    {
        Task<List<MercadoLibreMessage>> GetMensajesAsync(string? filtro = null, CancellationToken ct = default);

        /// <summary>Historial de mensajes de una orden (bloque del detalle de orden).</summary>
        Task<List<MercadoLibreMessage>> GetMensajesPorOrdenAsync(int orderId, CancellationToken ct = default);

        Task<MercadoLibreMessage?> GetMensajeAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Crea un mensaje entrante simulado local asociado a una orden. Solo
        /// habilitado en Development o con ModoSimulacion=true. Nunca llama a ML.
        /// </summary>
        Task<MercadoLibreMessageResult> SimularMensajeAsync(
            int orderId, string texto, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default);

        /// <summary>
        /// Responde a una conversación de orden con un mensaje saliente.
        /// En simulación guarda el mensaje local. El envío real exige
        /// ModoSimulacion=false Y confirmarReal=true; de lo contrario se bloquea.
        /// </summary>
        Task<MercadoLibreMessageResult> ResponderMensajeAsync(
            int orderId, string texto, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Idempotente por MessageId. Crea/actualiza un mensaje a partir de un
        /// webhook. En simulación guarda pendiente con el resource (no llama a ML).
        /// </summary>
        Task<int?> RegistrarDesdeWebhookAsync(
            string messageId, int accountId, string? resource, long? meliOrderId,
            MercadoLibreConfiguracion config, CancellationToken ct = default);
    }

    /// <summary>Filtros canónicos del listado de mensajes.</summary>
    public static class MercadoLibreMessageFiltro
    {
        public const string Recibidos = "recibidos";
        public const string Enviados = "enviados";
        public const string Simulados = "simulados";
        public const string ConError = "con-error";
        public const string SinVincular = "sin-vincular";
    }

    public sealed record MercadoLibreMessageResult(bool Ok, int? Id, string Mensaje);
}

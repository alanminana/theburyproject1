using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Gestión de preguntas preventa de Mercado Libre dentro del ERP.
    /// Toda respuesta es manual. En ModoSimulacion (o fuera de Development real)
    /// nunca se llama a la API de Mercado Libre: la respuesta se guarda local.
    /// </summary>
    public interface IMercadoLibreQuestionService
    {
        /// <summary>Lista de preguntas con filtro opcional (ver <see cref="MercadoLibreQuestionFiltro"/>).</summary>
        Task<List<MercadoLibreQuestion>> GetPreguntasAsync(string? filtro = null, CancellationToken ct = default);

        /// <summary>Preguntas de una publicación local (bloque del detalle de publicación).</summary>
        Task<List<MercadoLibreQuestion>> GetPreguntasPorListingAsync(int listingId, CancellationToken ct = default);

        Task<MercadoLibreQuestion?> GetPreguntaAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Crea una pregunta simulada local sobre una publicación. Solo habilitado
        /// en Development o con ModoSimulacion=true. Nunca llama a Mercado Libre.
        /// </summary>
        Task<MercadoLibreQuestionResult> SimularPreguntaAsync(
            int listingId, string texto, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default);

        /// <summary>
        /// Responde una pregunta. En simulación guarda la respuesta local y loguea.
        /// El envío real exige ModoSimulacion=false Y confirmarReal=true; de lo
        /// contrario se bloquea sin tocar Mercado Libre.
        /// </summary>
        Task<MercadoLibreQuestionResult> ResponderPreguntaAsync(
            int id, string respuesta, bool confirmarReal, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Idempotente por QuestionId. Crea/actualiza una pregunta a partir de un
        /// webhook. En simulación guarda pendiente con el resource (no llama a ML);
        /// en modo real puede completar el texto desde la API.
        /// </summary>
        Task<int?> RegistrarDesdeWebhookAsync(
            long questionId, int accountId, string? resource, MercadoLibreConfiguracion config, CancellationToken ct = default);
    }

    /// <summary>Filtros canónicos del listado de preguntas.</summary>
    public static class MercadoLibreQuestionFiltro
    {
        public const string Pendientes = "pendientes";
        public const string Respondidas = "respondidas";
        public const string Simuladas = "simuladas";
        public const string ConError = "con-error";
        public const string SinVincular = "sin-vincular";
    }

    /// <summary>Resultado de una operación sobre preguntas (no excepción para flujos esperados).</summary>
    public sealed record MercadoLibreQuestionResult(bool Ok, int? Id, string Mensaje);
}

using TheBuryProject.Models.DTOs;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Cliente REST de bajo nivel contra api.mercadolibre.com (HttpClientFactory).
    /// No conoce la base de datos: recibe tokens en texto plano por parámetro
    /// y nunca los loguea.
    /// </summary>
    public interface IMercadoLibreApiClient
    {
        /// <summary>
        /// Intercambia el code del callback OAuth por access_token + refresh_token.
        /// </summary>
        Task<MeliTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken ct = default);

        /// <summary>
        /// Refresca el access token. Mercado Libre devuelve SIEMPRE un refresh_token
        /// nuevo que debe persistirse reemplazando al anterior.
        /// </summary>
        Task<MeliTokenResponse> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);

        /// <summary>
        /// GET /users/me — identifica al vendedor y sirve como prueba de conexión.
        /// </summary>
        Task<MeliUserDto> GetCurrentUserAsync(string accessToken, CancellationToken ct = default);

        /// <summary>
        /// GET /users/{sellerId}/items/search con search_type=scan (recorre todo el inventario).
        /// Pasar scrollId null en la primera página y el devuelto en las siguientes.
        /// </summary>
        Task<MeliItemSearchPageDto> SearchItemIdsAsync(string accessToken, long sellerId, string? scrollId, int limit = 100, CancellationToken ct = default);

        /// <summary>
        /// GET /items?ids=... (multiget, lotes de hasta 20).
        /// </summary>
        Task<IReadOnlyList<MeliItemDto>> GetItemsAsync(string accessToken, IReadOnlyCollection<string> itemIds, CancellationToken ct = default);

        /// <summary>
        /// PUT /items/{itemId} con un payload mínimo construido por el caller.
        /// Nunca enviar arrays completos de variaciones: solo {id, campo} por
        /// variación a modificar (PUT no destructivo).
        /// </summary>
        Task<MeliItemDto> UpdateItemAsync(string accessToken, string itemId, object payload, CancellationToken ct = default);

        /// <summary>
        /// PUT /items/{itemId}/variations/{variationId} con payload minimo de la variacion.
        /// Usar para stock/precio de publicaciones con variaciones: evita enviar el
        /// array completo de variations en PUT /items/{itemId}.
        /// </summary>
        Task<MeliVariationDto> UpdateItemVariationAsync(
            string accessToken,
            string itemId,
            long variationId,
            object payload,
            CancellationToken ct = default);

        /// <summary>
        /// POST /items — crea una publicación nueva. SOLO debe llamarse desde el
        /// flujo de borradores con validación, flag de publicación habilitado,
        /// modo simulación desactivado y confirmación explícita.
        /// </summary>
        Task<MeliItemDto> CreateItemAsync(string accessToken, object payload, CancellationToken ct = default);

        /// <summary>
        /// GET /items/{itemId}/description — texto plano de la descripción.
        /// </summary>
        Task<string?> GetItemDescriptionAsync(string accessToken, string itemId, CancellationToken ct = default);

        /// <summary>
        /// PUT /items/{itemId}/description con {"plain_text": ...}.
        /// </summary>
        Task UpdateItemDescriptionAsync(string accessToken, string itemId, string plainText, CancellationToken ct = default);

        // ------------------------------------------------------------------
        // Categorías (árbol + predictor). Datos públicos del catálogo: el token
        // es OPCIONAL. Se envía si hay cuenta conectada (mejor cuota); si es null
        // se llama sin Authorization. El listado de categorías raíz del site SÍ
        // exige token (la API lo bloquea con 403 sin Authorization).
        // ------------------------------------------------------------------

        /// <summary>
        /// GET /sites/{siteId}/categories — categorías raíz del site. Requiere token
        /// (sin Authorization la API responde 403 PolicyAgent).
        /// </summary>
        Task<IReadOnlyList<MeliCategoryNodeDto>> GetSiteCategoriesAsync(
            string siteId, string? accessToken, CancellationToken ct = default);

        /// <summary>
        /// GET /categories/{categoryId} — detalle con path_from_root, children y settings.
        /// children vacío ⇒ categoría hoja. Token opcional.
        /// </summary>
        Task<MeliCategoryDto> GetCategoryAsync(
            string categoryId, string? accessToken, CancellationToken ct = default);

        /// <summary>
        /// GET /sites/{siteId}/domain_discovery/search?q=... — predictor de categoría
        /// a partir de un texto (título del producto). Token opcional.
        /// </summary>
        Task<IReadOnlyList<MeliCategoryPredictionDto>> PredictCategoriesAsync(
            string siteId, string query, string? accessToken, int limit = 8, CancellationToken ct = default);

        /// <summary>
        /// GET /orders/{orderId} — orden completa.
        /// </summary>
        Task<MeliOrderDto> GetOrderAsync(string accessToken, long orderId, CancellationToken ct = default);

        /// <summary>
        /// GET /orders/search?seller={sellerId} ordenado por fecha desc, con
        /// filtro opcional de fecha de creación desde.
        /// </summary>
        Task<IReadOnlyList<MeliOrderDto>> SearchOrdersAsync(string accessToken, long sellerId, DateTime? desdeUtc = null, int limit = 50, CancellationToken ct = default);

        /// <summary>
        /// GET /shipments/{shipmentId} — estado del envío. Null si ML devuelve 404.
        /// </summary>
        Task<MeliShipmentDto?> GetShipmentAsync(string accessToken, long shipmentId, CancellationToken ct = default);

        // ------------------------------------------------------------------
        // Preguntas y mensajes (Fase 16)
        // Estos métodos SOLO deben invocarse en modo real, con ModoSimulacion
        // desactivado y confirmación explícita. En QA/simulación nunca se llaman.
        // ------------------------------------------------------------------

        /// <summary>
        /// GET /questions/{questionId} — pregunta preventa completa. Null si 404.
        /// </summary>
        Task<MeliQuestionDto?> GetQuestionAsync(string accessToken, long questionId, CancellationToken ct = default);

        /// <summary>
        /// POST /answers — responde una pregunta preventa. SOLO modo real confirmado.
        /// </summary>
        Task AnswerQuestionAsync(string accessToken, long questionId, string text, CancellationToken ct = default);

        /// <summary>
        /// GET /messages/packs/{packId}/sellers/{sellerId} — conversación postventa.
        /// </summary>
        Task<MeliMessagesResponseDto> GetMessagesAsync(
            string accessToken, long packId, long sellerId, CancellationToken ct = default);

        /// <summary>
        /// POST /messages/packs/{packId}/sellers/{sellerId} — envía un mensaje
        /// postventa. SOLO modo real confirmado.
        /// </summary>
        Task SendMessageAsync(
            string accessToken, long packId, long sellerId, long buyerUserId, string text, CancellationToken ct = default);
    }
}

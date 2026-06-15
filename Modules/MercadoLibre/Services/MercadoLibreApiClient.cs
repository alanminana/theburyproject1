using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Exceptions;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    public class MercadoLibreApiClient : IMercadoLibreApiClient
    {
        private const int MultigetBatchSize = 20;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private readonly MercadoLibreOptions _options;
        private readonly ILogger<MercadoLibreApiClient> _logger;

        public MercadoLibreApiClient(
            HttpClient http,
            IOptions<MercadoLibreOptions> options,
            ILogger<MercadoLibreApiClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<MeliTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken ct = default)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri
            };

            return await PostTokenAsync(form, "exchange authorization_code", ct);
        }

        public async Task<MeliTokenResponse> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken
            };

            return await PostTokenAsync(form, "refresh_token", ct);
        }

        public async Task<MeliUserDto> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, "/users/me", accessToken);
            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "GET /users/me", ct);

            var user = await response.Content.ReadFromJsonAsync<MeliUserDto>(JsonOptions, ct);
            return user ?? throw new MercadoLibreApiException("GET /users/me devolvió un body vacío.");
        }

        public async Task<MeliItemSearchPageDto> SearchItemIdsAsync(
            string accessToken, long sellerId, string? scrollId, int limit = 100, CancellationToken ct = default)
        {
            var url = $"/users/{sellerId}/items/search?search_type=scan&limit={limit}";

            if (!string.IsNullOrEmpty(scrollId))
                url += $"&scroll_id={Uri.EscapeDataString(scrollId)}";

            using var request = CreateAuthorizedRequest(HttpMethod.Get, url, accessToken);
            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "GET /users/{id}/items/search", ct);

            var page = await response.Content.ReadFromJsonAsync<MeliItemSearchPageDto>(JsonOptions, ct);
            return page ?? new MeliItemSearchPageDto();
        }

        public async Task<IReadOnlyList<MeliItemDto>> GetItemsAsync(
            string accessToken, IReadOnlyCollection<string> itemIds, CancellationToken ct = default)
        {
            var items = new List<MeliItemDto>(itemIds.Count);

            foreach (var batch in itemIds.Chunk(MultigetBatchSize))
            {
                var ids = string.Join(',', batch);
                using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/items?ids={ids}", accessToken);
                using var response = await _http.SendAsync(request, ct);

                await EnsureSuccessAsync(response, "GET /items (multiget)", ct);

                var entries = await response.Content.ReadFromJsonAsync<List<MeliMultigetEntryDto>>(JsonOptions, ct)
                              ?? new List<MeliMultigetEntryDto>();

                foreach (var entry in entries)
                {
                    if (entry.Code is >= 200 and < 300 && entry.Body is not null)
                    {
                        items.Add(entry.Body);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Multiget de items devolvió code {Code} para una publicación (id: {ItemId})",
                            entry.Code, entry.Body?.Id ?? "desconocido");
                    }
                }
            }

            return items;
        }

        public async Task<MeliItemDto> UpdateItemAsync(
            string accessToken, string itemId, object payload, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"/items/{itemId}", accessToken);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, $"PUT /items/{itemId}", ct);

            var item = await response.Content.ReadFromJsonAsync<MeliItemDto>(JsonOptions, ct);
            return item ?? throw new MercadoLibreApiException($"PUT /items/{itemId} devolvió un body vacío.");
        }

        public async Task<MeliVariationDto> UpdateItemVariationAsync(
            string accessToken, string itemId, long variationId, object payload, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"/items/{itemId}/variations/{variationId}", accessToken);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, $"PUT /items/{itemId}/variations/{variationId}", ct);

            var variation = await response.Content.ReadFromJsonAsync<MeliVariationDto>(JsonOptions, ct);
            return variation ?? throw new MercadoLibreApiException(
                $"PUT /items/{itemId}/variations/{variationId} devolvió un body vacío.");
        }

        public async Task<MeliItemDto> CreateItemAsync(
            string accessToken, object payload, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "/items", accessToken);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "POST /items", ct);

            var item = await response.Content.ReadFromJsonAsync<MeliItemDto>(JsonOptions, ct);
            return item ?? throw new MercadoLibreApiException("POST /items devolvió un body vacío.");
        }

        public async Task<string?> GetItemDescriptionAsync(
            string accessToken, string itemId, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/items/{itemId}/description", accessToken);
            using var response = await _http.SendAsync(request, ct);

            // Publicaciones sin descripción devuelven 404: no es un error.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await EnsureSuccessAsync(response, $"GET /items/{itemId}/description", ct);

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            return doc.TryGetProperty("plain_text", out var texto) ? texto.GetString() : null;
        }

        public async Task UpdateItemDescriptionAsync(
            string accessToken, string itemId, string plainText, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Put, $"/items/{itemId}/description", accessToken);
            request.Content = JsonContent.Create(new { plain_text = plainText }, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, $"PUT /items/{itemId}/description", ct);
        }

        public async Task<MeliOrderDto> GetOrderAsync(
            string accessToken, long orderId, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/orders/{orderId}", accessToken);
            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "GET /orders/{id}", ct);

            var order = await response.Content.ReadFromJsonAsync<MeliOrderDto>(JsonOptions, ct);
            return order ?? throw new MercadoLibreApiException($"GET /orders/{orderId} devolvió un body vacío.");
        }

        public async Task<IReadOnlyList<MeliOrderDto>> SearchOrdersAsync(
            string accessToken, long sellerId, DateTime? desdeUtc = null, int limit = 50, CancellationToken ct = default)
        {
            var url = $"/orders/search?seller={sellerId}&sort=date_desc&limit={limit}";

            if (desdeUtc.HasValue)
                url += $"&order.date_created.from={Uri.EscapeDataString(desdeUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))}";

            using var request = CreateAuthorizedRequest(HttpMethod.Get, url, accessToken);
            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "GET /orders/search", ct);

            var page = await response.Content.ReadFromJsonAsync<MeliOrderSearchPageDto>(JsonOptions, ct);
            return page?.Results ?? new List<MeliOrderDto>();
        }

        public async Task<MeliShipmentDto?> GetShipmentAsync(
            string accessToken, long shipmentId, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/shipments/{shipmentId}", accessToken);
            using var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await EnsureSuccessAsync(response, "GET /shipments/{id}", ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            var shipment = JsonSerializer.Deserialize<MeliShipmentDto>(body, JsonOptions);
            if (shipment is not null)
                shipment.RawJson = body;

            return shipment;
        }

        // ------------------------------------------------------------------
        // Preguntas y mensajes (Fase 16).
        // Solo se invocan en modo real confirmado; en QA/simulación los
        // servicios cortan antes de llegar acá.
        // ------------------------------------------------------------------

        public async Task<MeliQuestionDto?> GetQuestionAsync(
            string accessToken, long questionId, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/questions/{questionId}", accessToken);
            using var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await EnsureSuccessAsync(response, "GET /questions/{id}", ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<MeliQuestionDto>(body, JsonOptions);
        }

        public async Task AnswerQuestionAsync(
            string accessToken, long questionId, string text, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "/answers", accessToken);
            request.Content = JsonContent.Create(new { question_id = questionId, text }, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "POST /answers", ct);
        }

        public async Task<MeliMessagesResponseDto> GetMessagesAsync(
            string accessToken, long packId, long sellerId, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(
                HttpMethod.Get, $"/messages/packs/{packId}/sellers/{sellerId}", accessToken);
            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "GET /messages/packs/{packId}/sellers/{sellerId}", ct);

            var page = await response.Content.ReadFromJsonAsync<MeliMessagesResponseDto>(JsonOptions, ct);
            return page ?? new MeliMessagesResponseDto();
        }

        public async Task SendMessageAsync(
            string accessToken, long packId, long sellerId, long buyerUserId, string text, CancellationToken ct = default)
        {
            using var request = CreateAuthorizedRequest(
                HttpMethod.Post, $"/messages/packs/{packId}/sellers/{sellerId}", accessToken);

            request.Content = JsonContent.Create(new
            {
                from = new { user_id = sellerId },
                to = new { user_id = buyerUserId },
                text
            }, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);

            await EnsureSuccessAsync(response, "POST /messages/packs/{packId}/sellers/{sellerId}", ct);
        }

        private async Task<MeliTokenResponse> PostTokenAsync(
            Dictionary<string, string> form, string operacion, CancellationToken ct)
        {
            using var content = new FormUrlEncodedContent(form);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token") { Content = content };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);

            // IMPORTANTE: nunca loguear el body de /oauth/token (contiene tokens).
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Mercado Libre /oauth/token ({Operacion}) falló con status {Status}",
                    operacion, (int)response.StatusCode);

                throw new MercadoLibreApiException(
                    $"Mercado Libre rechazó la operación de token ({operacion}). Status: {(int)response.StatusCode}.",
                    response.StatusCode);
            }

            var token = await response.Content.ReadFromJsonAsync<MeliTokenResponse>(JsonOptions, ct);

            if (token is null || string.IsNullOrEmpty(token.AccessToken))
                throw new MercadoLibreApiException($"Respuesta inválida de /oauth/token ({operacion}).");

            return token;
        }

        private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string accessToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private async Task EnsureSuccessAsync(HttpResponseMessage response, string operacion, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
                return;

            string excerpt;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                excerpt = body.Length > 500 ? body[..500] : body;
            }
            catch
            {
                excerpt = "(no se pudo leer el body de error)";
            }

            _logger.LogError(
                "Mercado Libre {Operacion} falló con status {Status}: {Excerpt}",
                operacion, (int)response.StatusCode, excerpt);

            throw new MercadoLibreApiException(
                $"Error llamando a Mercado Libre ({operacion}). Status: {(int)response.StatusCode}.",
                response.StatusCode,
                excerpt);
        }
    }
}

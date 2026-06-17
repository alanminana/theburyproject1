using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Modules.MercadoLibre.DTOs;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Infraestructura compartida de los tests del módulo MercadoLibre:
/// SQLite in-memory + factory de contexto + fake del API client.
/// </summary>
internal static class MercadoLibreTestDb
{
    public static (TestDbContextFactory Factory, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new AppDbContext(options))
        {
            context.Database.EnsureCreated();
        }

        return (new TestDbContextFactory(options), connection);
    }
}

internal sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

    public AppDbContext CreateDbContext() => new(_options);
}

/// <summary>
/// ProductoService REAL con dependencias mínimas, para tests que necesitan el
/// alta canónica de productos (validaciones + movimiento de stock inicial).
/// </summary>
internal static class MercadoLibreTestProductoService
{
    public static ProductoService Crear(AppDbContext context)
        => new(
            context,
            NullLogger<ProductoService>.Instance,
            new FakePrecioHistoricoService(),
            new FakeCurrentUserService(),
            new NullPrecioVigenteResolver());

    internal sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string GetUsername() => "tester";
        public string GetUserId() => "tester-id";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    internal sealed class FakePrecioHistoricoService : IPrecioHistoricoService
    {
        public Task<PrecioHistorico> RegistrarCambioAsync(
            int productoId, decimal precioCompraAnterior, decimal precioCompraNuevo,
            decimal precioVentaAnterior, decimal precioVentaNuevo, string? motivoCambio, string usuarioModificacion)
            => Task.FromResult(new PrecioHistorico());

        public Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId) => throw new NotSupportedException();
        public Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId) => throw new NotSupportedException();
        public Task<bool> RevertirCambioAsync(int historialId) => throw new NotSupportedException();
        public Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(DateTime? fechaDesde, DateTime? fechaHasta) => throw new NotSupportedException();
        public Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro) => throw new NotSupportedException();
        public Task<PrecioSimulacionViewModel> SimularCambioAsync(int productoId, decimal precioCompraNuevo, decimal precioVentaNuevo) => throw new NotSupportedException();
        public Task MarcarComoNoReversibleAsync(int historialId) => throw new NotSupportedException();
    }

    internal sealed class NullPrecioVigenteResolver : IPrecioVigenteResolver
    {
        public Task<PrecioVigenteResultado?> ResolverAsync(int productoId, int? listaId = null, DateTime? fecha = null)
            => Task.FromResult<PrecioVigenteResultado?>(null);

        public Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
            IEnumerable<int> productoIds, int? listaId = null, DateTime? fecha = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, PrecioVigenteResultado>>(
                new Dictionary<int, PrecioVigenteResultado>());
    }
}

/// <summary>
/// Fake configurable de IMercadoLibreApiClient (el proyecto de tests no usa mocking library).
/// </summary>
internal sealed class FakeMercadoLibreApiClient : IMercadoLibreApiClient
{
    public MeliTokenResponse? TokenParaCode { get; set; }
    public MeliTokenResponse? TokenParaRefresh { get; set; }
    public MeliUserDto? Usuario { get; set; }
    public List<List<string>> PaginasScan { get; set; } = new();
    public List<MeliItemDto> Items { get; set; } = new();

    public int ExchangeCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public string? UltimoRefreshTokenUsado { get; private set; }
    public string? UltimoAccessTokenUsado { get; private set; }

    public Task<MeliTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken ct = default)
    {
        ExchangeCalls++;
        return Task.FromResult(TokenParaCode ?? throw new InvalidOperationException("TokenParaCode no configurado"));
    }

    public Task<MeliTokenResponse> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        RefreshCalls++;
        UltimoRefreshTokenUsado = refreshToken;
        return Task.FromResult(TokenParaRefresh ?? throw new InvalidOperationException("TokenParaRefresh no configurado"));
    }

    public Task<MeliUserDto> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        return Task.FromResult(Usuario ?? throw new InvalidOperationException("Usuario no configurado"));
    }

    public Task<MeliItemSearchPageDto> SearchItemIdsAsync(
        string accessToken, long sellerId, string? scrollId, int limit = 100, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;

        var indice = scrollId is null ? 0 : int.Parse(scrollId);

        if (indice >= PaginasScan.Count)
            return Task.FromResult(new MeliItemSearchPageDto());

        return Task.FromResult(new MeliItemSearchPageDto
        {
            Results = PaginasScan[indice],
            ScrollId = (indice + 1).ToString()
        });
    }

    public Task<IReadOnlyList<MeliItemDto>> GetItemsAsync(
        string accessToken, IReadOnlyCollection<string> itemIds, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;

        IReadOnlyList<MeliItemDto> resultado = Items
            .Where(i => itemIds.Contains(i.Id))
            .ToList();

        return Task.FromResult(resultado);
    }

    // ------------------------------------------------------------------
    // Push / órdenes / envíos (Fases B, C, E, H)
    // ------------------------------------------------------------------

    /// <summary>Registro de cada PUT /items: (itemId, payload).</summary>
    public List<(string ItemId, object Payload)> UpdateItemCalls { get; } = new();

    /// <summary>Registro de cada PUT /items/{itemId}/variations/{variationId}: (itemId, variationId, payload).</summary>
    public List<(string ItemId, long VariationId, object Payload)> UpdateItemVariationCalls { get; } = new();

    /// <summary>Respuesta del PUT por itemId; si no hay, eco del item en Items o uno mínimo.</summary>
    public Dictionary<string, MeliItemDto> UpdateItemRespuestas { get; } = new();

    /// <summary>Respuesta del PUT por variacion; si no hay, eco de la variacion local o una minima.</summary>
    public Dictionary<(string ItemId, long VariationId), MeliVariationDto> UpdateItemVariationRespuestas { get; } = new();

    /// <summary>ItemIds cuyo PUT debe fallar (simula error de la API).</summary>
    public HashSet<string> UpdateItemFallan { get; } = new();

    /// <summary>Variaciones cuyo PUT debe fallar (simula error de la API).</summary>
    public HashSet<(string ItemId, long VariationId)> UpdateItemVariationFallan { get; } = new();

    public Dictionary<string, string?> Descripciones { get; } = new();
    public List<(string ItemId, string PlainText)> UpdateDescripcionCalls { get; } = new();

    public Dictionary<long, MeliOrderDto> Ordenes { get; } = new();
    public List<long> GetOrderCalls { get; } = new();

    public Dictionary<long, MeliShipmentDto> Shipments { get; } = new();

    public Task<MeliItemDto> UpdateItemAsync(
        string accessToken, string itemId, object payload, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        UpdateItemCalls.Add((itemId, payload));

        if (UpdateItemFallan.Contains(itemId))
            throw new TheBuryProject.Modules.MercadoLibre.Exceptions.MercadoLibreApiException(
                $"Error simulado en PUT /items/{itemId}.", System.Net.HttpStatusCode.BadRequest);

        if (UpdateItemRespuestas.TryGetValue(itemId, out var respuesta))
            return Task.FromResult(respuesta);

        var existente = Items.FirstOrDefault(i => i.Id == itemId);
        return Task.FromResult(existente ?? new MeliItemDto { Id = itemId });
    }

    public Task<MeliVariationDto> UpdateItemVariationAsync(
        string accessToken, string itemId, long variationId, object payload, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        UpdateItemVariationCalls.Add((itemId, variationId, payload));

        if (UpdateItemVariationFallan.Contains((itemId, variationId)))
            throw new TheBuryProject.Modules.MercadoLibre.Exceptions.MercadoLibreApiException(
                $"Error simulado en PUT /items/{itemId}/variations/{variationId}.", System.Net.HttpStatusCode.BadRequest);

        if (UpdateItemVariationRespuestas.TryGetValue((itemId, variationId), out var respuesta))
            return Task.FromResult(respuesta);

        var existente = Items.FirstOrDefault(i => i.Id == itemId)
            ?.Variations.FirstOrDefault(v => v.Id == variationId);

        return Task.FromResult(existente ?? new MeliVariationDto { Id = variationId });
    }

    /// <summary>Registro de cada POST /items (payloads de publicación).</summary>
    public List<object> CreateItemCalls { get; } = new();

    /// <summary>Respuesta del POST /items; si no hay, un item mínimo activo.</summary>
    public MeliItemDto? CreateItemRespuesta { get; set; }

    public bool CreateItemFalla { get; set; }

    /// <summary>Body de error (excerpt) que acompaña el rechazo simulado del POST /items.</summary>
    public string? CreateItemErrorExcerpt { get; set; }

    public Task<MeliItemDto> CreateItemAsync(
        string accessToken, object payload, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        CreateItemCalls.Add(payload);

        if (CreateItemFalla)
            throw new TheBuryProject.Modules.MercadoLibre.Exceptions.MercadoLibreApiException(
                "Error simulado en POST /items.", System.Net.HttpStatusCode.BadRequest, CreateItemErrorExcerpt);

        return Task.FromResult(CreateItemRespuesta ?? new MeliItemDto { Id = "MLA900000001", Status = "active" });
    }

    public Task<string?> GetItemDescriptionAsync(
        string accessToken, string itemId, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        return Task.FromResult(Descripciones.GetValueOrDefault(itemId));
    }

    public Task UpdateItemDescriptionAsync(
        string accessToken, string itemId, string plainText, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        UpdateDescripcionCalls.Add((itemId, plainText));
        Descripciones[itemId] = plainText;
        return Task.CompletedTask;
    }

    public Task<MeliOrderDto> GetOrderAsync(
        string accessToken, long orderId, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        GetOrderCalls.Add(orderId);

        return Task.FromResult(Ordenes.TryGetValue(orderId, out var orden)
            ? orden
            : throw new InvalidOperationException($"Orden {orderId} no configurada en el fake"));
    }

    public Task<IReadOnlyList<MeliOrderDto>> SearchOrdersAsync(
        string accessToken, long sellerId, DateTime? desdeUtc = null, int limit = 50, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;

        IReadOnlyList<MeliOrderDto> resultado = Ordenes.Values
            .Where(o => !desdeUtc.HasValue || (o.DateCreated.HasValue && o.DateCreated.Value.UtcDateTime >= desdeUtc.Value))
            .OrderByDescending(o => o.DateCreated)
            .Take(limit)
            .ToList();

        return Task.FromResult(resultado);
    }

    public Task<MeliShipmentDto?> GetShipmentAsync(
        string accessToken, long shipmentId, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        return Task.FromResult(Shipments.GetValueOrDefault(shipmentId));
    }

    // ------------------------------------------------------------------
    // Preguntas y mensajes (Fase 16). En QA no deberían invocarse: los
    // contadores permiten asertar que la simulación NO llamó a la API.
    // ------------------------------------------------------------------

    public Dictionary<long, MeliQuestionDto> Preguntas { get; } = new();
    public List<(long QuestionId, string Text)> AnswerQuestionCalls { get; } = new();
    public List<(long PackId, long SellerId, long BuyerUserId, string Text)> SendMessageCalls { get; } = new();
    public Dictionary<(long PackId, long SellerId), MeliMessagesResponseDto> Mensajes { get; } = new();

    public Task<MeliQuestionDto?> GetQuestionAsync(
        string accessToken, long questionId, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        return Task.FromResult(Preguntas.GetValueOrDefault(questionId));
    }

    public Task AnswerQuestionAsync(
        string accessToken, long questionId, string text, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        AnswerQuestionCalls.Add((questionId, text));
        return Task.CompletedTask;
    }

    public Task<MeliMessagesResponseDto> GetMessagesAsync(
        string accessToken, long packId, long sellerId, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        return Task.FromResult(Mensajes.GetValueOrDefault((packId, sellerId)) ?? new MeliMessagesResponseDto());
    }

    public Task SendMessageAsync(
        string accessToken, long packId, long sellerId, long buyerUserId, string text, CancellationToken ct = default)
    {
        UltimoAccessTokenUsado = accessToken;
        SendMessageCalls.Add((packId, sellerId, buyerUserId, text));
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Categorías (árbol + predictor). Token opcional: se registra el último
    // valor recibido (puede ser null) para asertar la resolución de token.
    // ------------------------------------------------------------------

    public List<MeliCategoryNodeDto> SiteCategories { get; } = new();
    public Dictionary<string, MeliCategoryDto> Categorias { get; } = new();
    public List<MeliCategoryPredictionDto> Predicciones { get; } = new();

    public string? UltimoTokenCategoria { get; private set; }
    public bool UltimoTokenCategoriaRecibido { get; private set; }
    public string? UltimaQueryPredictor { get; private set; }

    public Task<IReadOnlyList<MeliCategoryNodeDto>> GetSiteCategoriesAsync(
        string siteId, string? accessToken, CancellationToken ct = default)
    {
        RegistrarTokenCategoria(accessToken);
        return Task.FromResult<IReadOnlyList<MeliCategoryNodeDto>>(SiteCategories.ToList());
    }

    public Task<MeliCategoryDto> GetCategoryAsync(
        string categoryId, string? accessToken, CancellationToken ct = default)
    {
        RegistrarTokenCategoria(accessToken);
        return Task.FromResult(Categorias.TryGetValue(categoryId, out var cat)
            ? cat
            : throw new InvalidOperationException($"Categoría {categoryId} no configurada en el fake"));
    }

    public Task<IReadOnlyList<MeliCategoryPredictionDto>> PredictCategoriesAsync(
        string siteId, string query, string? accessToken, int limit = 8, CancellationToken ct = default)
    {
        RegistrarTokenCategoria(accessToken);
        UltimaQueryPredictor = query;
        return Task.FromResult<IReadOnlyList<MeliCategoryPredictionDto>>(Predicciones.Take(limit).ToList());
    }

    private void RegistrarTokenCategoria(string? accessToken)
    {
        UltimoTokenCategoria = accessToken;
        UltimoTokenCategoriaRecibido = true;
    }
}

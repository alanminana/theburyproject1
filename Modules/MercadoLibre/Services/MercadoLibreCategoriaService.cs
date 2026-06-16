using Microsoft.Extensions.Options;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    /// <summary>
    /// Consume el árbol de categorías de ML online (predictor + detalle on-demand),
    /// sin persistir el árbol. Resuelve un token de la cuenta conectada cuando existe;
    /// si no, llama a los recursos públicos sin Authorization.
    /// </summary>
    public class MercadoLibreCategoriaService : IMercadoLibreCategoriaService
    {
        private const int ConsultaMinima = 2;

        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibreAuthService _authService;
        private readonly MercadoLibreOptions _options;
        private readonly ILogger<MercadoLibreCategoriaService> _logger;

        public MercadoLibreCategoriaService(
            IMercadoLibreApiClient apiClient,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibreAuthService authService,
            IOptions<MercadoLibreOptions> options,
            ILogger<MercadoLibreCategoriaService> logger)
        {
            _apiClient = apiClient;
            _configuracionService = configuracionService;
            _authService = authService;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<CategoriaSugerenciaVm>> SugerirAsync(
            string? consulta, CancellationToken ct = default)
        {
            consulta = consulta?.Trim();
            if (string.IsNullOrEmpty(consulta) || consulta.Length < ConsultaMinima)
                return Array.Empty<CategoriaSugerenciaVm>();

            var token = await ResolverTokenAsync(ct);

            var predicciones = await _apiClient.PredictCategoriesAsync(
                _options.SiteId, consulta, token, 8, ct);

            return predicciones
                .Where(p => !string.IsNullOrEmpty(p.CategoryId))
                .Select(p => new CategoriaSugerenciaVm
                {
                    CategoryId = p.CategoryId,
                    Nombre = p.CategoryName,
                    Dominio = p.DomainName
                })
                .ToList();
        }

        public async Task<CategoriaNivelVm> ListarHijosAsync(
            string? categoryId, CancellationToken ct = default)
        {
            var token = await ResolverTokenAsync(ct);

            categoryId = categoryId?.Trim();

            // Raíz del site: requiere token (la API responde 403 sin Authorization).
            if (string.IsNullOrEmpty(categoryId))
            {
                var raiz = await _apiClient.GetSiteCategoriesAsync(_options.SiteId, token, ct);
                return new CategoriaNivelVm
                {
                    EsHoja = false,
                    Hijos = raiz.Select(n => new CategoriaNodoVm { CategoryId = n.Id, Nombre = n.Name }).ToList()
                };
            }

            var categoria = await _apiClient.GetCategoryAsync(categoryId, token, ct);

            return new CategoriaNivelVm
            {
                CategoryId = categoria.Id,
                Nombre = categoria.Name,
                Path = categoria.PathString,
                EsHoja = categoria.EsHoja,
                Hijos = categoria.ChildrenCategories
                    .Select(n => new CategoriaNodoVm { CategoryId = n.Id, Nombre = n.Name })
                    .ToList()
            };
        }

        public async Task<CategoriaResueltaVm> ResolverAsync(string categoryId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
                throw new InvalidOperationException("Indicá una categoría a resolver.");

            var token = await ResolverTokenAsync(ct);
            var categoria = await _apiClient.GetCategoryAsync(categoryId.Trim(), token, ct);

            return new CategoriaResueltaVm
            {
                CategoryId = categoria.Id,
                Nombre = categoria.Name,
                Path = categoria.PathString,
                EsHoja = categoria.EsHoja,
                ListingAllowed = categoria.Settings?.ListingAllowed ?? false,
                MaxTitleLength = categoria.Settings?.MaxTitleLength
            };
        }

        /// <summary>
        /// Devuelve un access token de la cuenta conectada, o null para usar los
        /// recursos públicos. Nunca rompe el flujo si el token no se puede obtener.
        /// </summary>
        private async Task<string?> ResolverTokenAsync(CancellationToken ct)
        {
            try
            {
                var config = await _configuracionService.GetAsync(ct);
                if (!config.AccountId.HasValue)
                    return null;

                return await _authService.GetValidAccessTokenAsync(config.AccountId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo resolver el token de ML para categorías; se usan los recursos públicos.");
                return null;
            }
        }
    }
}

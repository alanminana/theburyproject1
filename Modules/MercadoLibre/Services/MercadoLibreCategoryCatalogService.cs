using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;
using TheBuryProject.Modules.MercadoLibre.ViewModels;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    /// <summary>
    /// Consulta el catálogo local de categorías ML. Solo lee la base; no toca la API.
    /// </summary>
    public class MercadoLibreCategoryCatalogService : IMercadoLibreCategoryCatalogService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public MercadoLibreCategoryCatalogService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<bool> HayCatalogoAsync(string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            return await ctx.MercadoLibreCategories.AnyAsync(c => c.SiteId == siteId, ct);
        }

        public async Task<MercadoLibreCatalogoEstadoVm> GetEstadoAsync(string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

            var estado = await ctx.MercadoLibreCategorySyncStates
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.SiteId == siteId, ct);

            var categorias = await ctx.MercadoLibreCategories.CountAsync(c => c.SiteId == siteId, ct);

            var vm = new MercadoLibreCatalogoEstadoVm
            {
                SiteId = siteId,
                Importado = categorias > 0
            };

            if (estado is not null)
            {
                vm.UltimaImportacionUtc = estado.LastImportedAtUtc;
                vm.UltimoExitoUtc = estado.LastSuccessAtUtc;
                vm.Categorias = estado.ImportedCategories;
                vm.Hojas = estado.LeafCategories;
                vm.Publicables = estado.ListingAllowedCategories;
                vm.Atributos = estado.ImportedAttributes;
                vm.UltimoError = estado.LastError;
                vm.SourceFilePath = estado.SourceFilePath;
                vm.DurationMs = estado.DurationMs;
            }

            // Si hay datos pero el sync-state quedó corto, reflejar el conteo real.
            if (vm.Categorias == 0 && categorias > 0)
                vm.Categorias = categorias;

            return vm;
        }

        public async Task<IReadOnlyList<CatalogoCategoriaVm>> BuscarCategoriasAsync(
            string? texto, int limit = 20, string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            texto = texto?.Trim() ?? string.Empty;
            if (texto.Length == 0)
                return Array.Empty<CatalogoCategoriaVm>();

            if (limit <= 0) limit = 20;
            var textoLower = texto.ToLowerInvariant();

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

            var query = ctx.MercadoLibreCategories
                .AsNoTracking()
                .Where(c => c.SiteId == siteId)
                .Where(c =>
                    c.CategoryId == texto
                    || c.Name.ToLower().Contains(textoLower)
                    || (c.PathFromRootJson != null && c.PathFromRootJson.ToLower().Contains(textoLower)));

            var categorias = await query
                .OrderByDescending(c => c.CategoryId == texto)        // coincidencia exacta primero
                .ThenByDescending(c => c.IsLeaf && c.ListingAllowed)  // publicables
                .ThenByDescending(c => c.IsLeaf)
                .ThenByDescending(c => c.TotalItemsInThisCategory ?? 0)
                .Take(limit)
                .ToListAsync(ct);

            return categorias.Select(ToCategoriaVm).ToList();
        }

        public async Task<CatalogoCategoriaVm?> GetCategoryAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            categoryId = categoryId?.Trim() ?? string.Empty;
            if (categoryId.Length == 0) return null;

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var cat = await ctx.MercadoLibreCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.SiteId == siteId && c.CategoryId == categoryId, ct);

            return cat is null ? null : ToCategoriaVm(cat);
        }

        public async Task<IReadOnlyList<CatalogoAtributoVm>> GetAttributesAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            categoryId = categoryId?.Trim() ?? string.Empty;
            if (categoryId.Length == 0) return Array.Empty<CatalogoAtributoVm>();

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var atributos = await ctx.MercadoLibreCategoryAttributes
                .AsNoTracking()
                .Where(a => a.SiteId == siteId && a.CategoryId == categoryId)
                .OrderBy(a => a.Relevance ?? 999)
                .ThenBy(a => a.Name)
                .ToListAsync(ct);

            return atributos.Select(a => ToAtributoVm(a, "new")).ToList();
        }

        public async Task<IReadOnlyList<CatalogoAtributoVm>> GetRequiredAttributesAsync(
            string categoryId, string condition = "new", string? listingTypeId = null,
            string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            categoryId = categoryId?.Trim() ?? string.Empty;
            if (categoryId.Length == 0) return Array.Empty<CatalogoAtributoVm>();

            var esNuevo = string.Equals(condition?.Trim(), "new", StringComparison.OrdinalIgnoreCase);

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var atributos = await ctx.MercadoLibreCategoryAttributes
                .AsNoTracking()
                .Where(a => a.SiteId == siteId && a.CategoryId == categoryId)
                // Mostrables: required / new_required / conditional_required / catalog_required.
                .Where(a => a.Required || a.NewRequired || a.ConditionalRequired || a.CatalogRequired)
                // Nunca read_only; ocultos solo si son required.
                .Where(a => !a.ReadOnly)
                .Where(a => !a.Hidden || a.Required)
                .OrderBy(a => a.Relevance ?? 999)
                .ThenBy(a => a.Name)
                .ToListAsync(ct);

            return atributos.Select(a => ToAtributoVm(a, condition ?? "new")).ToList();
        }

        public async Task<(bool Existe, bool EsHoja, bool ListingAllowed)> IsLeafListingAllowedAsync(
            string categoryId, string siteId = "MLA", CancellationToken ct = default)
        {
            siteId = Normalizar(siteId);
            categoryId = categoryId?.Trim() ?? string.Empty;
            if (categoryId.Length == 0) return (false, false, false);

            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var cat = await ctx.MercadoLibreCategories
                .AsNoTracking()
                .Where(c => c.SiteId == siteId && c.CategoryId == categoryId)
                .Select(c => new { c.IsLeaf, c.ListingAllowed })
                .FirstOrDefaultAsync(ct);

            return cat is null ? (false, false, false) : (true, cat.IsLeaf, cat.ListingAllowed);
        }

        // ── Mapeo ──────────────────────────────────────────────────────────────
        private static CatalogoCategoriaVm ToCategoriaVm(MercadoLibreCategory c) => new()
        {
            CategoryId = c.CategoryId,
            Name = c.Name,
            Path = PathDesdeJson(c.PathFromRootJson),
            EsHoja = c.IsLeaf,
            ListingAllowed = c.ListingAllowed,
            TotalItems = c.TotalItemsInThisCategory,
            MaxTitleLength = c.MaxTitleLength
        };

        private static CatalogoAtributoVm ToAtributoVm(MercadoLibreCategoryAttribute a, string condition)
        {
            var esNuevo = string.Equals(condition?.Trim(), "new", StringComparison.OrdinalIgnoreCase);
            var bloqueante = a.Required || (a.NewRequired && esNuevo);

            return new CatalogoAtributoVm
            {
                AttributeId = a.AttributeId,
                Name = a.Name,
                ValueType = a.ValueType,
                Required = a.Required,
                CatalogRequired = a.CatalogRequired,
                ConditionalRequired = a.ConditionalRequired,
                NewRequired = a.NewRequired,
                ReadOnly = a.ReadOnly,
                Hidden = a.Hidden,
                Multivalued = a.Multivalued,
                ValueMaxLength = a.ValueMaxLength,
                DefaultUnit = a.DefaultUnit,
                Hint = a.Hint,
                Tooltip = a.Tooltip,
                AttributeGroupName = a.AttributeGroupName,
                Values = ParseValores(a.ValuesJson),
                AllowedUnits = ParseValores(a.AllowedUnitsJson),
                EsBloqueante = bloqueante,
                EsRecomendado = !bloqueante
            };
        }

        private static List<CatalogoValorVm> ParseValores(string? json)
        {
            var lista = new List<CatalogoValorVm>();
            if (string.IsNullOrWhiteSpace(json)) return lista;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return lista;

                foreach (var v in doc.RootElement.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Object) continue;
                    var id = v.TryGetProperty("id", out var idEl)
                        ? (idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString())
                        : null;
                    var name = v.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()
                        : null;
                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(id)) continue;
                    lista.Add(new CatalogoValorVm { Id = id ?? string.Empty, Name = name ?? id ?? string.Empty });
                }
            }
            catch
            {
                // JSON inválido/legacy: devolver lo que se pudo (o vacío).
            }

            return lista;
        }

        private static string? PathDesdeJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                var nombres = new List<string>();
                foreach (var n in doc.RootElement.EnumerateArray())
                {
                    if (n.ValueKind == JsonValueKind.Object &&
                        n.TryGetProperty("name", out var nameEl) &&
                        nameEl.ValueKind == JsonValueKind.String)
                    {
                        var name = nameEl.GetString();
                        if (!string.IsNullOrEmpty(name)) nombres.Add(name);
                    }
                }
                return nombres.Count == 0 ? null : string.Join(" > ", nombres);
            }
            catch
            {
                return null;
            }
        }

        private static string Normalizar(string? siteId)
            => string.IsNullOrWhiteSpace(siteId) ? "MLA" : siteId.Trim().ToUpperInvariant();
    }
}

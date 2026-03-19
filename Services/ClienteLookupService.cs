using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ClienteLookupService : IClienteLookupService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };

        private const string ClientesCacheKey = "clientes_selectlist_all";

        public ClienteLookupService(IDbContextFactory<AppDbContext> contextFactory, IMemoryCache cache)
        {
            _contextFactory = contextFactory;
            _cache = cache;
        }

        public async Task<List<SelectListItem>> GetClientesSelectListAsync(int? selectedId = null, bool limitarACliente = false)
        {
            // If caller explicitly wants to limit to a single cliente, fetch only that record (no need for full cache)
            if (limitarACliente && selectedId.HasValue)
            {
                var single = await GetClienteByIdAsync(selectedId.Value);
                if (!single.HasValue)
                    return new List<SelectListItem>();

                var (id, display) = single.Value;
                return new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = id.ToString(),
                        Text = display,
                        Selected = selectedId.Value == id
                    }
                };
            }

            // Try cached list
            if (_cache.TryGetValue(ClientesCacheKey, out List<SelectListItem>? cached) && cached != null)
            {
                // If a selectedId is requested, mark selection on a copied list
                if (selectedId.HasValue)
                {
                    return cached
                        .Select(i => new SelectListItem(i.Text, i.Value, i.Value == selectedId.Value.ToString()))
                        .ToList();
                }

                return cached;
            }

            await using var context = await _contextFactory.CreateDbContextAsync();

            var clientes = await context.Clientes
                .AsNoTracking()
                .Where(c => c.Activo && !c.IsDeleted)
                .OrderBy(c => c.Apellido)
                .ThenBy(c => c.Nombre)
                .ToListAsync();

            var selectItems = clientes
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.ToDisplayName()
                })
                .ToList();

            _cache.Set(ClientesCacheKey, selectItems, _cacheOptions);

            if (selectedId.HasValue)
            {
                return selectItems
                    .Select(i => new SelectListItem(i.Text, i.Value, i.Value == selectedId.Value.ToString()))
                    .ToList();
            }

            return selectItems;
        }

        public async Task<string?> GetClienteDisplayNameAsync(int clienteId)
        {
            var key = $"cliente_display_{clienteId}";

            if (_cache.TryGetValue(key, out string? display))
                return display;

            var client = await GetClienteByIdAsync(clienteId);
            if (!client.HasValue)
                return null;

            var (_, displayName) = client.Value;
            _cache.Set(key, displayName, _cacheOptions);
            return displayName;
        }

        private async Task<(int Id, string DisplayName)?> GetClienteByIdAsync(int clienteId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var cliente = await context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && c.Activo && !c.IsDeleted);

            if (cliente == null)
                return null;

            return (cliente.Id, cliente.ToDisplayName());
        }
    }
}
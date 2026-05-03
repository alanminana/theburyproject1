using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class CatalogLookupService : ICatalogLookupService
    {
        private readonly ICategoriaService _categoriaService;
        private readonly IMarcaService _marcaService;
        private readonly IProductoService _productoService;
        private readonly AppDbContext _context;

        public CatalogLookupService(
            ICategoriaService categoriaService,
            IMarcaService marcaService,
            IProductoService productoService,
            AppDbContext context)
        {
            _categoriaService = categoriaService;
            _marcaService = marcaService;
            _productoService = productoService;
            _context = context;
        }

        public async Task<(IEnumerable<Categoria> categorias, IEnumerable<Marca> marcas)> GetCategoriasYMarcasAsync()
        {
            var categorias = await _categoriaService.GetAllAsync();
            var marcas = await _marcaService.GetAllAsync();
            return (categorias, marcas);
        }

        public async Task<(IEnumerable<Categoria> categorias, IEnumerable<Marca> marcas, IEnumerable<Producto> productos)> GetCategoriasMarcasYProductosAsync()
        {
            var categorias = await _categoriaService.GetAllAsync();
            var marcas = await _marcaService.GetAllAsync();
            var productos = await _productoService.GetAllAsync();
            return (categorias, marcas, productos);
        }

        public async Task<IEnumerable<Categoria>> GetSubcategoriasAsync(int categoriaId)
        {
            return await _categoriaService.GetChildrenAsync(categoriaId);
        }

        public async Task<IEnumerable<Marca>> GetSubmarcasAsync(int marcaId)
        {
            return await _marcaService.GetChildrenAsync(marcaId);
        }

        public async Task<List<AlicuotaIVAFormItem>> ObtenerAlicuotasIVAParaFormAsync()
        {
            var raw = await _context.AlicuotasIVA
                .AsNoTracking()
                .Where(a => a.Activa && !a.IsDeleted)
                .OrderByDescending(a => a.EsPredeterminada)
                .ThenBy(a => (double)a.Porcentaje)
                .Select(a => new
                {
                    a.Id,
                    a.Porcentaje,
                    Texto = a.EsPredeterminada ? $"{a.Nombre} (predeterminada)" : a.Nombre
                })
                .ToListAsync();

            return raw.Select(a => new AlicuotaIVAFormItem(
                a.Id,
                a.Porcentaje.ToString(CultureInfo.InvariantCulture),
                a.Texto)).ToList();
        }

        public async Task<decimal?> ObtenerPorcentajeAlicuotaAsync(int alicuotaIVAId)
        {
            return await _context.AlicuotasIVA
                .AsNoTracking()
                .Where(a => a.Id == alicuotaIVAId && a.Activa && !a.IsDeleted)
                .Select(a => (decimal?)a.Porcentaje)
                .FirstOrDefaultAsync();
        }
    }
}

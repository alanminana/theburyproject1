using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class CatalogLookupService : ICatalogLookupService
    {
        private readonly ICategoriaService _categoriaService;
        private readonly IMarcaService _marcaService;
        private readonly IProductoService _productoService;

        public CatalogLookupService(
            ICategoriaService categoriaService,
            IMarcaService marcaService,
            IProductoService productoService)
        {
            _categoriaService = categoriaService;
            _marcaService = marcaService;
            _productoService = productoService;
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
    }
}

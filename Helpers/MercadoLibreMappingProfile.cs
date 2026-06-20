using AutoMapper;
using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Perfil de AutoMapper del módulo MercadoLibre, separado del MappingProfile
    /// central para mantener el módulo aislado.
    /// </summary>
    public class MercadoLibreMappingProfile : Profile
    {
        public MercadoLibreMappingProfile()
        {
            CreateMap<MercadoLibreAccount, MercadoLibreCuentaViewModel>()
                .ForMember(d => d.TotalListings, o => o.Ignore())
                .ForMember(d => d.ListingsVinculadas, o => o.Ignore());

            CreateMap<MercadoLibreListing, MercadoLibreListingViewModel>()
                .ForMember(d => d.CantidadVariaciones, o => o.MapFrom(s => s.Variaciones.Count(v => !v.IsDeleted)))
                .ForMember(d => d.ProductoCodigo, o => o.MapFrom(s => s.Producto != null ? s.Producto.Codigo : null))
                .ForMember(d => d.ProductoNombre, o => o.MapFrom(s => s.Producto != null ? s.Producto.Nombre : null))
                .ForMember(d => d.ProductoStockActual, o => o.MapFrom(s => s.Producto != null ? (decimal?)s.Producto.StockActual : null))
                .ForMember(d => d.ProductoPrecioVenta, o => o.MapFrom(s => s.Producto != null ? (decimal?)s.Producto.PrecioVenta : null))
                .ForMember(d => d.ProductoSugeridoId, o => o.Ignore())
                .ForMember(d => d.ProductoSugeridoNombre, o => o.Ignore());
        }
    }
}

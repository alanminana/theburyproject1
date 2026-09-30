using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ServiciosVentaAdminViewModel
    {
        public List<ServicioVentaPrecioItemViewModel> Envios { get; set; } = new();
        public List<ServicioVentaPrecioItemViewModel> Armados { get; set; } = new();
    }

    public class ServicioVentaPrecioItemViewModel
    {
        public TipoServicioVenta Tipo { get; set; }

        public string Nombre { get; set; } = string.Empty;

        public bool Domiciliario { get; set; }

        [Range(0, 999999999.99, ErrorMessage = "El precio no puede ser negativo.")]
        public decimal Precio { get; set; }

        public bool Activo { get; set; } = true;
    }
}

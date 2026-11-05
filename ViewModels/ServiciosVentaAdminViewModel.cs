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

        [TheBuryProject.Validation.MontoArgentino]
        [TheBuryProject.Validation.MontoSinAmbiguedad]
        public decimal Precio { get; set; }

        /// <summary>Costo estándar para el negocio de prestar el servicio (no lo ve el cliente).</summary>
        [TheBuryProject.Validation.MontoArgentino]
        [TheBuryProject.Validation.MontoSinAmbiguedad]
        public decimal Costo { get; set; }

        public bool Activo { get; set; } = true;
    }
}

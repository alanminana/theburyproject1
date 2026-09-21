using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class VentaEnvioViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Estado del envío")]
        public EstadoEnvio Estado { get; set; } = EstadoEnvio.Pendiente;

        [Display(Name = "Destinatario")]
        [Required(ErrorMessage = "El destinatario es requerido")]
        [StringLength(200)]
        public string Destinatario { get; set; } = string.Empty;

        [Display(Name = "Teléfono")]
        [StringLength(30)]
        public string? Telefono { get; set; }

        [Display(Name = "Domicilio de entrega")]
        [Required(ErrorMessage = "El domicilio de entrega es requerido")]
        [StringLength(300)]
        public string Domicilio { get; set; } = string.Empty;

        [Display(Name = "Localidad")]
        [StringLength(100)]
        public string? Localidad { get; set; }

        [Display(Name = "Provincia")]
        [StringLength(100)]
        public string? Provincia { get; set; }

        [Display(Name = "Código postal")]
        [StringLength(20)]
        public string? CodigoPostal { get; set; }

        [Display(Name = "Transportista")]
        [StringLength(150)]
        public string? Transportista { get; set; }

        [Display(Name = "N° de seguimiento")]
        [StringLength(100)]
        public string? NumeroSeguimiento { get; set; }

        [Display(Name = "Costo de envío"), DataType(DataType.Currency)]
        [Range(0, 999999999.99, ErrorMessage = "El costo de envío no puede ser negativo.")]
        public decimal? CostoEnvio { get; set; }

        [Display(Name = "Fecha programada")]
        [DataType(DataType.Date)]
        public DateTime? FechaProgramada { get; set; }

        public DateTime? FechaDespacho { get; set; }
        public DateTime? FechaEntregaReal { get; set; }

        [StringLength(500)]
        public string? MotivoNoEntrega { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }

        #region Presentación

        public string EstadoDisplay => Estado switch
        {
            EstadoEnvio.Pendiente => "Pendiente",
            EstadoEnvio.Preparando => "Preparando",
            EstadoEnvio.Despachado => "Despachado",
            EstadoEnvio.EnCamino => "En camino",
            EstadoEnvio.Entregado => "Entregado",
            EstadoEnvio.Fallido => "Entrega fallida",
            EstadoEnvio.Cancelado => "Cancelado",
            _ => Estado.ToString()
        };

        public string EstadoPillClass => Estado switch
        {
            EstadoEnvio.Pendiente => "pill-slate",
            EstadoEnvio.Preparando => "pill-blue",
            EstadoEnvio.Despachado => "pill-cyan",
            EstadoEnvio.EnCamino => "pill-amber",
            EstadoEnvio.Entregado => "pill-green",
            EstadoEnvio.Fallido => "pill-red",
            EstadoEnvio.Cancelado => "pill-red",
            _ => "pill-slate"
        };

        public bool EsTerminal => Estado is EstadoEnvio.Entregado or EstadoEnvio.Cancelado;

        /// <summary>
        /// Espejo, sólo para poblar el &lt;select&gt; del modal "Actualizar estado de
        /// envío", de la máquina de estados real en VentaEnvioService. La autoridad que
        /// de verdad valida y escribe sigue siendo el service (fail-closed); esto es
        /// nada más que una ayuda de UI para no ofrecer transiciones imposibles.
        /// </summary>
        public IEnumerable<EstadoEnvio> EstadosSiguientesPosibles => Estado switch
        {
            EstadoEnvio.Pendiente => new[] { EstadoEnvio.Preparando, EstadoEnvio.Cancelado },
            EstadoEnvio.Preparando => new[] { EstadoEnvio.Despachado, EstadoEnvio.Cancelado },
            EstadoEnvio.Despachado => new[] { EstadoEnvio.EnCamino, EstadoEnvio.Entregado, EstadoEnvio.Fallido },
            EstadoEnvio.EnCamino => new[] { EstadoEnvio.Entregado, EstadoEnvio.Fallido },
            EstadoEnvio.Fallido => new[] { EstadoEnvio.Preparando, EstadoEnvio.Cancelado },
            _ => Array.Empty<EstadoEnvio>()
        };

        public static string EstadoDisplayFor(EstadoEnvio estado) => estado switch
        {
            EstadoEnvio.Pendiente => "Pendiente",
            EstadoEnvio.Preparando => "Preparando",
            EstadoEnvio.Despachado => "Despachado",
            EstadoEnvio.EnCamino => "En camino",
            EstadoEnvio.Entregado => "Entregado",
            EstadoEnvio.Fallido => "Entrega fallida",
            EstadoEnvio.Cancelado => "Cancelado",
            _ => estado.ToString()
        };

        #endregion
    }
}

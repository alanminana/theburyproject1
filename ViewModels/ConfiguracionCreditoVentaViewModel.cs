using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionCreditoVentaViewModel
    {
        [Required]
        public int CreditoId { get; set; }

        public int? VentaId { get; set; }

        [Display(Name = "Cliente")]
        public string ClienteNombre { get; set; } = string.Empty;

        public int ClienteId { get; set; }

        [Display(Name = "Número de Crédito")]
        public string? NumeroCredito { get; set; }

        [Display(Name = "Fuente de Configuración")]
        public FuenteConfiguracionCredito FuenteConfiguracion { get; set; } = FuenteConfiguracionCredito.Global;

        [Display(Name = "Método de cálculo")]
        [Required(ErrorMessage = "Debe seleccionar un método de cálculo")]
        public MetodoCalculoCredito? MetodoCalculo { get; set; }

        public int? PerfilCreditoSeleccionadoId { get; set; }

        [Display(Name = "Monto del Crédito")]
        public decimal Monto { get; set; }

        /// <summary>
        /// Anticipo opcional. Si vacío, se normaliza a 0 en el backend.
        /// </summary>
        [Display(Name = "Anticipo")]
        [Range(0, double.MaxValue, ErrorMessage = "El anticipo no puede ser negativo")]
        public decimal? Anticipo { get; set; }

        [Display(Name = "Monto financiado")]
        public decimal MontoFinanciado { get; set; }

        [Display(Name = "Cantidad de cuotas")]
        [Range(1, 120, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 120")]
        public int CantidadCuotas { get; set; } = 1;

        /// <summary>
        /// Tasa mensual en %. Si vacío, se usa la tasa default del sistema.
        /// </summary>
        [Display(Name = "Tasa mensual (%)")]
        [Range(0.01, 100, ErrorMessage = "La tasa debe ser mayor a 0 y no superar el 100%")]
        public decimal? TasaMensual { get; set; }

        /// <summary>
        /// Gastos administrativos opcionales. Si vacío, se normaliza a 0.
        /// </summary>
        [Display(Name = "Gastos administrativos")]
        [Range(0, 1000000, ErrorMessage = "El valor debe ser mayor o igual a 0")]
        public decimal? GastosAdministrativos { get; set; }

        [Display(Name = "Fecha de primera cuota")]
        [DataType(DataType.Date)]
        [Required(ErrorMessage = "Debe indicar la fecha de la primera cuota")]
        public DateTime? FechaPrimeraCuota { get; set; }

        public bool CreditoEstaConfigurado { get; set; }

        public bool ContratoGenerado { get; set; }

        public bool PlantillaActivaDisponible { get; set; }

        public int CuotasMinPermitidas { get; set; } = 1;

        public int CuotasMaxPermitidas { get; set; } = 120;

        public int? MaxCuotasCreditoProducto { get; set; }

        public string? RestriccionCreditoProductoDescripcion { get; set; }

        public int MaxCuotasBase { get; set; } = 120;

        public int? ProductoIdRestrictivo { get; set; }

        public string? ProductoRestrictivoNombre { get; set; }

        public ClienteConfigCreditoVentaViewModel ClienteConfigPersonalizada { get; set; } = new();

        public List<PerfilCreditoActivoViewModel> PerfilesActivos { get; set; } = new();

        public bool PuedeGenerarContrato => VentaId.HasValue && CreditoEstaConfigurado && !ContratoGenerado && PlantillaActivaDisponible;
    }

    public class ClienteConfigCreditoVentaViewModel
    {
        public bool TieneTasaPersonalizada { get; set; }
        public decimal? TasaPersonalizada { get; set; }
        public decimal? GastosPersonalizados { get; set; }
        public int? CuotasMaximas { get; set; }
        public int? CuotasMinimas { get; set; }
        public decimal TasaGlobal { get; set; }
        public decimal GastosGlobales { get; set; }
        public bool TienePerfilPreferido { get; set; }
        public int? PerfilPreferidoId { get; set; }
        public string? PerfilNombre { get; set; }
        public decimal? PerfilTasa { get; set; }
        public decimal? PerfilGastos { get; set; }
        public int? PerfilMinCuotas { get; set; }
        public int? PerfilMaxCuotas { get; set; }
        public bool TieneConfiguracionCliente { get; set; }
        public decimal? MontoMinimo { get; set; }
        public decimal? MontoMaximo { get; set; }
        public int? MaxCuotasCreditoProducto { get; set; }
        public string? RestriccionCreditoProductoDescripcion { get; set; }
        public int MaxCuotasBase { get; set; } = 120;
        public int? ProductoIdRestrictivo { get; set; }
        public string? ProductoRestrictivoNombre { get; set; }
    }

    public class PerfilCreditoActivoViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public decimal TasaMensual { get; set; }
        public decimal GastosAdministrativos { get; set; }
        public int MinCuotas { get; set; }
        public int MaxCuotas { get; set; }
    }
}

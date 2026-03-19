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

        // TAREA 6: Mantener por compatibilidad con código existente
        [Display(Name = "Fuente de Configuración")]
        public FuenteConfiguracionCredito FuenteConfiguracion { get; set; } = FuenteConfiguracionCredito.Global;

        // TAREA 9: Nuevo método de cálculo más intuitivo
        // PUNTO 1: Sin default automático - el usuario debe seleccionar explícitamente
        [Display(Name = "Método de cálculo")]
        [Required(ErrorMessage = "Debe seleccionar un método de cálculo")]
        public MetodoCalculoCredito? MetodoCalculo { get; set; }

        // TAREA 9: Perfil seleccionado cuando MetodoCalculo = UsarPerfil
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
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    public class CreditoSolicitudViewModel
    {
        // Cliente
        public ClienteResumenViewModel Cliente { get; set; } = new();

        public ClienteResumenViewModel? Garante { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un cliente")]
        [Display(Name = "Cliente")]
        public int ClienteId
        {
            get => Cliente.Id;
            set => Cliente.Id = value;
        }

        [Display(Name = "Cliente")]
        public string? ClienteNombre
        {
            get => Cliente.NombreCompleto;
            set => Cliente.NombreCompleto = value ?? string.Empty;
        }

        [Display(Name = "Sueldo del Cliente")]
        [DisplayFormat(DataFormatString = "{0:C}")]
        public decimal? SueldoCliente { get; set; }

        [Display(Name = "Puntaje de Riesgo")]
        public decimal PuntajeRiesgo { get; set; }

        // Monto solicitado
        [Required(ErrorMessage = "El monto solicitado es requerido")]
        [Range(1000, 10000000, ErrorMessage = "El monto debe estar entre $1.000 y $10.000.000")]
        [Display(Name = "Monto Solicitado")]
        [DisplayFormat(DataFormatString = "{0:C}", ApplyFormatInEditMode = true)]
        public decimal MontoSolicitado { get; set; }

        // Plazo
        [Required(ErrorMessage = "Debe seleccionar la cantidad de cuotas")]
        [Range(1, 60, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 60")]
        [Display(Name = "Cantidad de Cuotas")]
        public int CantidadCuotas { get; set; }

        // Tasa (se calculará automáticamente pero se puede editar manualmente)
        [Required(ErrorMessage = "La tasa de interés es requerida")]
        [Range(0, 100, ErrorMessage = "La tasa debe estar entre 0% y 100%")]
        [Display(Name = "Tasa de Interés Mensual (%)")]
        [DisplayFormat(DataFormatString = "{0:N2}", ApplyFormatInEditMode = true)]
        public decimal TasaInteres { get; set; }

        // Garante
        [Display(Name = "¿Requiere Garante?")]
        public bool RequiereGarante { get; set; }

        [Display(Name = "Garante")]
        public int? GaranteId
        {
            get => Garante?.Id;
            set
            {
                if (value.HasValue)
                {
                    Garante ??= new ClienteResumenViewModel();
                    Garante.Id = value.Value;
                }
                else
                {
                    Garante = null;
                }
            }
        }

        [Display(Name = "Garante")]
        public string? GaranteNombre
        {
            get => Garante?.NombreCompleto;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Garante ??= new ClienteResumenViewModel();
                    Garante.NombreCompleto = value;
                }
            }
        }

        // Observaciones
        [StringLength(1000, ErrorMessage = "Las observaciones no pueden exceder los 1000 caracteres")]
        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Calculados (readonly)
        [Display(Name = "Monto por Cuota")]
        [DisplayFormat(DataFormatString = "{0:C}")]
        public decimal MontoCuota { get; set; }

        [Display(Name = "Monto Total a Pagar")]
        [DisplayFormat(DataFormatString = "{0:C}")]
        public decimal MontoTotal { get; set; }

        [Display(Name = "Total de Intereses")]
        [DisplayFormat(DataFormatString = "{0:C}")]
        public decimal TotalIntereses { get; set; }

        // Evaluación
        [Display(Name = "% del Sueldo")]
        [DisplayFormat(DataFormatString = "{0:N2}%")]
        public decimal? PorcentajeSueldo { get; set; }

        [Display(Name = "¿Cumple requisito de sueldo (30%)?")]
        public bool CumpleRequisitoSueldo { get; set; }

        [Display(Name = "¿Tiene documentación completa?")]
        public bool TieneDocumentacionCompleta { get; set; }

        [Display(Name = "Evaluación Automática")]
        public string? ResultadoEvaluacion { get; set; }

        // Dropdowns
        public SelectList? Clientes { get; set; }
        public SelectList? Garantes { get; set; }
        public SelectList? Plazos { get; set; }
    }
}
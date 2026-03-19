using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Mora
{
    /// <summary>
    /// ViewModel para registrar un nuevo contacto con cliente moroso
    /// </summary>
    public class RegistrarContactoViewModel
    {
        [Required(ErrorMessage = "El cliente es obligatorio")]
        public int ClienteId { get; set; }
        
        public int? AlertaId { get; set; }
        
        [Required(ErrorMessage = "El tipo de contacto es obligatorio")]
        [Display(Name = "Tipo de Contacto")]
        public TipoContacto TipoContacto { get; set; }
        
        [Required(ErrorMessage = "El resultado es obligatorio")]
        [Display(Name = "Resultado")]
        public ResultadoContacto Resultado { get; set; }
        
        [Display(Name = "Teléfono Usado")]
        [StringLength(50)]
        public string? Telefono { get; set; }
        
        [Display(Name = "Email Usado")]
        [StringLength(200)]
        [EmailAddress(ErrorMessage = "El email no es válido")]
        public string? Email { get; set; }
        
        [Display(Name = "Observaciones")]
        [StringLength(2000)]
        public string? Observaciones { get; set; }
        
        [Display(Name = "Duración (minutos)")]
        [Range(0, 999, ErrorMessage = "La duración debe ser entre 0 y 999 minutos")]
        public int? DuracionMinutos { get; set; }
        
        [Display(Name = "Próximo Contacto")]
        [DataType(DataType.Date)]
        public DateTime? ProximoContacto { get; set; }
        
        // Para promesa de pago
        [Display(Name = "Fecha Promesa de Pago")]
        [DataType(DataType.Date)]
        public DateTime? FechaPromesaPago { get; set; }
        
        [Display(Name = "Monto Prometido")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal? MontoPromesaPago { get; set; }
        
        // Datos de contexto para la vista
        public string? NombreCliente { get; set; }
        public string? DocumentoCliente { get; set; }
        public decimal? MontoVencido { get; set; }
        public int? DiasAtraso { get; set; }
    }

    /// <summary>
    /// ViewModel para registrar una promesa de pago
    /// </summary>
    public class RegistrarPromesaViewModel
    {
        [Required(ErrorMessage = "La alerta es obligatoria")]
        public int AlertaId { get; set; }
        
        [Required(ErrorMessage = "El cliente es obligatorio")]
        public int ClienteId { get; set; }
        
        [Required(ErrorMessage = "La fecha de pago es obligatoria")]
        [Display(Name = "Fecha de Pago Prometida")]
        [DataType(DataType.Date)]
        public DateTime FechaPromesa { get; set; }
        
        [Required(ErrorMessage = "El monto es obligatorio")]
        [Display(Name = "Monto Prometido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
        public decimal MontoPromesa { get; set; }
        
        [Display(Name = "Observaciones")]
        [StringLength(1000)]
        public string? Observaciones { get; set; }
        
        // Datos de contexto
        public string? NombreCliente { get; set; }
        public decimal? MontoVencidoTotal { get; set; }
    }

    /// <summary>
    /// ViewModel para crear un acuerdo de pago
    /// </summary>
    public class CrearAcuerdoViewModel
    {
        [Required(ErrorMessage = "La alerta es obligatoria")]
        public int AlertaId { get; set; }
        
        [Required(ErrorMessage = "El cliente es obligatorio")]
        public int ClienteId { get; set; }
        
        [Required(ErrorMessage = "El crédito es obligatorio")]
        public int CreditoId { get; set; }
        
        [Display(Name = "Monto de Deuda Original")]
        public decimal MontoDeudaOriginal { get; set; }
        
        [Display(Name = "Monto de Mora Original")]
        public decimal MontoMoraOriginal { get; set; }
        
        [Display(Name = "Monto a Condonar")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal MontoCondonar { get; set; }
        
        [Display(Name = "Total a Pagar")]
        public decimal MontoTotalAcuerdo => MontoDeudaOriginal + MontoMoraOriginal - MontoCondonar;
        
        [Required(ErrorMessage = "La entrega inicial es obligatoria")]
        [Display(Name = "Entrega Inicial")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto debe ser positivo")]
        public decimal MontoEntregaInicial { get; set; }
        
        [Required(ErrorMessage = "La cantidad de cuotas es obligatoria")]
        [Display(Name = "Cantidad de Cuotas")]
        [Range(1, 60, ErrorMessage = "Las cuotas deben ser entre 1 y 60")]
        public int CantidadCuotas { get; set; }
        
        [Required(ErrorMessage = "La fecha de la primera cuota es obligatoria")]
        [Display(Name = "Fecha Primera Cuota")]
        [DataType(DataType.Date)]
        public DateTime FechaPrimeraCuota { get; set; }
        
        [Display(Name = "Observaciones")]
        [StringLength(2000)]
        public string? Observaciones { get; set; }
        
        // Datos de contexto
        public string? NombreCliente { get; set; }
        public int? MaximoCuotasPermitido { get; set; }
        public decimal? PorcentajeMinEntrega { get; set; }
        public bool PermiteCondonacion { get; set; }
        public decimal? MaximoCondonacionPermitido { get; set; }
    }
}

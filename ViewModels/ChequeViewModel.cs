using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ChequeViewModel
    {
        public int Id { get; set; }

        public byte[]? RowVersion { get; set; }

        [Required(ErrorMessage = "El número de cheque es obligatorio")]
        [StringLength(50, ErrorMessage = "El número no puede tener más de 50 caracteres")]
        [Display(Name = "Número de Cheque")]
        public string Numero { get; set; } = string.Empty;

        [Required(ErrorMessage = "El banco es obligatorio")]
        [StringLength(100, ErrorMessage = "El nombre del banco no puede tener más de 100 caracteres")]
        [Display(Name = "Banco")]
        public string Banco { get; set; } = string.Empty;

        [Required(ErrorMessage = "El monto es obligatorio")]
        [Display(Name = "Monto")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        [Range(0.01, 999999999.99, ErrorMessage = "El monto debe ser mayor a 0")]
        public decimal Monto { get; set; }

        [Required(ErrorMessage = "La fecha de emisión es obligatoria")]
        [Display(Name = "Fecha de Emisión")]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.Today;

        [Display(Name = "Fecha de Vencimiento")]
        [DataType(DataType.Date)]
        public DateTime? FechaVencimiento { get; set; }

        [Required(ErrorMessage = "El estado es obligatorio")]
        [Display(Name = "Estado")]
        public EstadoCheque Estado { get; set; } = EstadoCheque.Emitido;

        [Display(Name = "Estado")]
        public string? EstadoNombre { get; set; }

        [Required(ErrorMessage = "El proveedor es obligatorio")]
        [Display(Name = "Proveedor")]
        public int ProveedorId { get; set; }

        [Display(Name = "Proveedor")]
        public string? ProveedorNombre { get; set; }

        [Display(Name = "Orden de Compra")]
        public int? OrdenCompraId { get; set; }

        [Display(Name = "Orden de Compra")]
        public string? OrdenCompraNumero { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden tener más de 500 caracteres")]
        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Propiedades calculadas
        [Display(Name = "Días para Vencer")]
        public int DiasPorVencer { get; set; }

        public bool EstaVencido => FechaVencimiento.HasValue && FechaVencimiento.Value < DateTime.Today;

        public bool EstaPorVencer => FechaVencimiento.HasValue &&
            FechaVencimiento.Value >= DateTime.Today &&
            FechaVencimiento.Value <= DateTime.Today.AddDays(7);

        // Información de auditoría
        [Display(Name = "Fecha de Creación")]
        public DateTime CreatedAt { get; set; }

        [Display(Name = "Última Modificación")]
        public DateTime UpdatedAt { get; set; }

  
    }
}
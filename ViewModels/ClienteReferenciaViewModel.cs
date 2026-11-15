using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.Validation;

namespace TheBuryProject.ViewModels
{
    /// <summary>Referencia informativa de un cliente (no afecta aptitud ni límite de crédito).</summary>
    public class ClienteReferenciaViewModel
    {
        public int Id { get; set; }
        public int ClienteId { get; set; }

        [Display(Name = "Tipo")]
        public TipoReferenciaCliente Tipo { get; set; } = TipoReferenciaCliente.Personal;

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150, ErrorMessage = "El nombre no puede superar los 150 caracteres.")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "La relación no puede superar los 100 caracteres.")]
        public string? Relacion { get; set; }

        [Required(ErrorMessage = "El teléfono es obligatorio.")]
        [StringLength(30, ErrorMessage = "El teléfono no puede superar los 30 caracteres.")]
        [TelefonoArgentino]
        public string Telefono { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "El email no puede superar los 200 caracteres.")]
        [EmailAddress(ErrorMessage = "El email no es válido.")]
        public string? Email { get; set; }

        [StringLength(150, ErrorMessage = "La empresa no puede superar los 150 caracteres.")]
        public string? Empresa { get; set; }

        [StringLength(1000, ErrorMessage = "Las observaciones no pueden superar los 1000 caracteres.")]
        public string? Observaciones { get; set; }

        public EstadoVerificacionReferencia EstadoVerificacion { get; set; } = EstadoVerificacionReferencia.Pendiente;
        public DateTime? FechaVerificacion { get; set; }
        public string? VerificadaPor { get; set; }
        public byte[]? RowVersion { get; set; }
    }

    /// <summary>Pedido para cambiar el estado de verificación de una referencia.</summary>
    public class ClienteReferenciaVerificacionRequest
    {
        public EstadoVerificacionReferencia Estado { get; set; }

        [StringLength(1000, ErrorMessage = "Las observaciones no pueden superar los 1000 caracteres.")]
        public string? Observaciones { get; set; }

        /// <summary>RowVersion leído al abrir el diálogo (control de concurrencia).</summary>
        public byte[]? RowVersion { get; set; }
    }

    /// <summary>Pedido para dar de baja una referencia.</summary>
    public class ClienteReferenciaBajaRequest
    {
        /// <summary>RowVersion leído al listar (control de concurrencia).</summary>
        public byte[]? RowVersion { get; set; }
    }
}

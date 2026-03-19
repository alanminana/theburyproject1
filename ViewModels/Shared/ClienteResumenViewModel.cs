using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Información resumida de la persona (cliente o garante) utilizada en distintas vistas.
    /// </summary>
    public class ClienteResumenViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Cliente")]
        public string NombreCompleto { get; set; } = string.Empty;

        public string NumeroDocumento { get; set; } = string.Empty;

        public string Telefono { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? Domicilio { get; set; }

        public decimal PuntajeRiesgo { get; set; }

        public decimal? Sueldo { get; set; }
    }
}

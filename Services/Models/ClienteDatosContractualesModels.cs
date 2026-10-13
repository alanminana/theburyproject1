using System.ComponentModel.DataAnnotations;
using TheBuryProject.Validation;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Datos del Cliente que exige el contrato de venta a crédito
    /// (ver ContratoVentaCreditoService.ValidarCliente). Los mismos campos, nada más.
    /// Las reglas de formato son las mismas que usa ClienteViewModel (Validation/ArgentinaValidationAttributes.cs).
    /// </summary>
    public class ClienteDatosContractualesDto
    {
        public int ClienteId { get; set; }

        [SoloLetras(MinLength = 2)]
        public string Nombre { get; set; } = string.Empty;

        [SoloLetras(MinLength = 2)]
        public string Apellido { get; set; } = string.Empty;

        public string TipoDocumento { get; set; } = string.Empty;

        [Display(Name = "Número de documento")]
        [DocumentoArgentino(nameof(TipoDocumento))]
        public string NumeroDocumento { get; set; } = string.Empty;

        public string Domicilio { get; set; } = string.Empty;
        public string Localidad { get; set; } = string.Empty;

        [Display(Name = "Teléfono")]
        [TelefonoArgentino]
        public string Telefono { get; set; } = string.Empty;
    }

    public class ClienteDatosContractualesEstado
    {
        public ClienteDatosContractualesDto Datos { get; init; } = new();

        /// <summary>Nombres de propiedad de <see cref="ClienteDatosContractualesDto"/> que están vacíos.</summary>
        public List<string> Faltantes { get; init; } = new();
    }

    public class ClienteDatosContractualesResultado
    {
        public bool Exito { get; init; }
        public List<string> Errores { get; init; } = new();
    }
}

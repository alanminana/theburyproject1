using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Punitorio
{
    /// <summary>
    /// Únicos valores admitidos desde el formulario HTTP para aplicar un punitorio. Los
    /// identificadores provienen de la ruta y todos los valores financieros se recalculan en el
    /// servicio canónico.
    /// </summary>
    public sealed class AplicarPunitorioHttpViewModel
    {
        private string? _motivo;

        [Required(ErrorMessage = "El motivo es obligatorio.")]
        public string? Motivo
        {
            get => _motivo;
            set => _motivo = value?.Trim();
        }

        [Required(ErrorMessage = "El panel está desactualizado. Recargalo e intentá nuevamente.")]
        public string? CuotaRowVersionBase64 { get; set; }
    }

    /// <summary>
    /// Únicos valores admitidos desde el formulario HTTP para anular una aplicación. El id de la
    /// aplicación pertenece a la ruta, nunca al body.
    /// </summary>
    public sealed class AnularPunitorioHttpViewModel
    {
        private string? _motivo;

        [Required(ErrorMessage = "El motivo es obligatorio.")]
        public string? Motivo
        {
            get => _motivo;
            set => _motivo = value?.Trim();
        }

        [Required(ErrorMessage = "El panel está desactualizado. Recargalo e intentá nuevamente.")]
        public string? PunitorioAplicadoRowVersionBase64 { get; set; }
    }

    /// <summary>Contrato JSON uniforme de las operaciones del panel de punitorios.</summary>
    public sealed class PunitorioOperacionResponseViewModel
    {
        public bool Success { get; init; }
        public required string Message { get; init; }
        public bool ReloadPanel { get; init; }
        public decimal? ImporteAplicadoReal { get; init; }
        public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
    }
}

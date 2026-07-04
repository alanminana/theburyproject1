using TheBuryProject.Services;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Resultado de la validación y estado de un garante vinculado a un cliente.
    /// </summary>
    public class GaranteInfoViewModel
    {
        public int GaranteRegistroId { get; set; }
        public int GaranteClienteId { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;

        /// <summary>Puntaje EFECTIVO del garante (manual si hay override, si no el automático). Es el que decide la validez.</summary>
        public int PuntajeCliente { get; set; }

        /// <summary>Origen del puntaje efectivo: "Manual" o "Automatico".</summary>
        public string FuentePuntaje { get; set; } = PuntajeCreditoEfectivo.FuenteAutomatico;

        public bool ClienteActivo { get; set; }
        public bool TieneCompras { get; set; }
        public int CantidadGarantiasActivas { get; set; }
        public DateTime FechaAlta { get; set; }
        public string? Observacion { get; set; }

        /// <summary>True si cumple TODAS las reglas del negocio al momento de la consulta.</summary>
        public bool EsValido { get; set; }

        /// <summary>Motivos por los que el garante NO es válido (vacío si EsValido = true).</summary>
        public List<string> MotivosInvalidez { get; set; } = new();
    }

    /// <summary>
    /// Request para asignar un garante a un cliente.
    /// </summary>
    public class AsignarGaranteRequest
    {
        public int ClienteId { get; set; }
        public int GaranteClienteId { get; set; }
        public string? Observacion { get; set; }
    }

    /// <summary>
    /// Request para remover el garante de un cliente.
    /// </summary>
    public class RemoverGaranteRequest
    {
        public int ClienteId { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }
}

using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests;

public sealed class DiagnosticarCondicionesPagoCarritoRequest
{
    [Required]
    public List<int> ProductoIds { get; set; } = new();

    public TipoPago TipoPago { get; set; }

    public int? ConfiguracionTarjetaId { get; set; }

    public TipoTarjeta? TipoTarjeta { get; set; }

    public decimal? TotalReferencia { get; set; }

    public int? MaxCuotasSinInteresGlobal { get; set; }

    public int? MaxCuotasConInteresGlobal { get; set; }

    public int? MaxCuotasCreditoGlobal { get; set; }
}

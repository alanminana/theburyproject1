using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Plan global de pago por medio y, opcionalmente, por tarjeta.
/// ConfiguracionTarjetaId null representa un plan general del medio.
/// AjustePorcentaje negativo = descuento, cero = sin ajuste, positivo = recargo.
/// </summary>
public class ConfiguracionPagoPlan : AuditableEntity
{
    public int ConfiguracionPagoId { get; set; }

    public int? ConfiguracionTarjetaId { get; set; }

    public TipoPago TipoPago { get; set; }

    public int CantidadCuotas { get; set; }

    public bool Activo { get; set; } = true;

    public TipoAjustePagoPlan TipoAjuste { get; set; } = TipoAjustePagoPlan.Porcentaje;

    public decimal AjustePorcentaje { get; set; }

    [StringLength(100)]
    public string? Etiqueta { get; set; }

    public int Orden { get; set; }

    [StringLength(500)]
    public string? Observaciones { get; set; }

    public virtual ConfiguracionPago ConfiguracionPago { get; set; } = null!;

    public virtual ConfiguracionTarjeta? ConfiguracionTarjeta { get; set; }
}

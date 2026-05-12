namespace TheBuryProject.Services.Models;

public sealed class AjustePagoGlobalRequest
{
    public decimal BaseVenta { get; init; }
    public decimal PorcentajeAjuste { get; init; }
    public int CantidadCuotas { get; init; } = 1;
    public bool MedioActivo { get; init; } = true;
    public bool? TarjetaActiva { get; init; }
    public bool? PlanActivo { get; init; }
}

public sealed class PlanPagoGlobalDto
{
    public int CantidadCuotas { get; init; } = 1;
    public bool Activo { get; init; } = true;
    public decimal AjustePorcentaje { get; init; }
    public string? Etiqueta { get; init; }
}

public sealed class AjustePagoGlobalResultado
{
    public bool EsValido => Estado == EstadoValidacionPagoGlobal.Valido;
    public EstadoValidacionPagoGlobal Estado { get; init; }
    public string? Mensaje { get; init; }
    public decimal BaseVenta { get; init; }
    public decimal PorcentajeAjuste { get; init; }
    public decimal MontoAjuste { get; init; }
    public decimal TotalFinal { get; init; }
    public int CantidadCuotas { get; init; }
    public decimal ValorCuota { get; init; }
    public bool EsPagoEnCuotas => CantidadCuotas > 1;
}

public enum EstadoValidacionPagoGlobal
{
    Valido = 0,
    MedioInactivo = 1,
    TarjetaInactiva = 2,
    PlanInactivo = 3,
    BaseNegativa = 4,
    CuotasInvalidas = 5,
    DescuentoMayorAlTotal = 6
}

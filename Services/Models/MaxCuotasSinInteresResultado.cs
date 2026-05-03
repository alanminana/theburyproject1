namespace TheBuryProject.Services.Models;

/// <summary>
/// Resultado del cálculo de máximo efectivo de cuotas sin interés para una venta.
/// Solo se devuelve cuando la tarjeta tiene TipoCuota.SinInteres y CantidadMaximaCuotas configurado.
/// Null en el método de origen significa que la restricción no aplica (tarjeta con interés, no encontrada, etc.).
/// </summary>
public sealed class MaxCuotasSinInteresResultado
{
    /// <summary>ID de la tarjeta evaluada.</summary>
    public int TarjetaId { get; init; }

    /// <summary>
    /// Máximo efectivo de cuotas sin interés aplicable a la venta.
    /// Es el mínimo entre el máximo de la tarjeta y el mínimo de restricciones de productos.
    /// Siempre >= 1.
    /// </summary>
    public int MaxCuotas { get; init; }

    /// <summary>
    /// True si algún producto del carrito tiene una restricción menor al máximo de la tarjeta.
    /// Útil para mostrar mensaje explicativo en la UI.
    /// </summary>
    public bool LimitadoPorProducto { get; init; }
}

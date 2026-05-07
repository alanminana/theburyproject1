namespace TheBuryProject.Services.Exceptions;

/// <summary>
/// Excepcion de dominio para rechazos de venta por condiciones de pago del carrito.
/// </summary>
public sealed class CondicionesPagoVentaException : InvalidOperationException
{
    public CondicionesPagoVentaException(string message)
        : base(message)
    {
    }
}

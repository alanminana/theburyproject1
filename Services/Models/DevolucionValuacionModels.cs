namespace TheBuryProject.Services.Models;

/// <summary>
/// P-02: valuación de lo reintegrable por producto de una venta. Es la ÚNICA fuente del importe que ve el operador en la vista
/// previa del modal «Crear devolución» y del que persiste <c>CrearDevolucionAsync</c>.
/// <c>ValorLista - DescuentoLineas + AjusteMedioPago = TotalReintegrable</c> (el ajuste lleva signo: recargo +, descuento −).
/// </summary>
public sealed record DevolucionValuacionProducto(
    int ProductoId,
    int CantidadVendida,
    decimal ValorLista,
    decimal DescuentoLineas,
    decimal AjusteMedioPago,
    decimal TotalReintegrable)
{
    /// <summary>Precio unitario reintegrable (redondeado a 2 decimales), igual al que se persiste en el detalle.</summary>
    public decimal PrecioUnitarioReintegrable => CantidadVendida > 0 ? Math.Round(TotalReintegrable / CantidadVendida, 2) : 0m;
}

/// <summary>
/// Vista previa por producto: valuación + unidades disponibles + remanente reintegrable (lo cobrado menos lo ya devuelto).
/// El importe de N unidades es <c>N == disponible ? remanente : min(N x precio unitario, remanente)</c> (<c>SubtotalDevolucion</c>).
/// </summary>
public sealed record DevolucionPreviewProducto(
    DevolucionValuacionProducto Valuacion,
    int CantidadDisponible,
    decimal Remanente);

using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

public sealed class CotizacionPagoCalculator : ICotizacionPagoCalculator
{
    public Task<CotizacionSimulacionResultado> SimularAsync(
        CotizacionSimulacionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var fechaCalculo = request.FechaCotizacion ?? DateTime.Today;

        if (request.Productos.Count == 0)
        {
            return Task.FromResult(new CotizacionSimulacionResultado
            {
                Exitoso = false,
                FechaCalculo = fechaCalculo,
                Errores = { "Debe agregar al menos un producto para cotizar." }
            });
        }

        var errores = new List<string>();

        foreach (var producto in request.Productos)
        {
            if (producto.ProductoId <= 0)
                errores.Add("Todos los productos de la cotizacion deben tener un ProductoId valido.");

            if (producto.Cantidad <= 0)
                errores.Add("Todos los productos de la cotizacion deben tener una cantidad mayor a cero.");
        }

        return Task.FromResult(new CotizacionSimulacionResultado
        {
            Exitoso = errores.Count == 0,
            FechaCalculo = fechaCalculo,
            Errores = errores,
            Advertencias =
            {
                "Cotizacion V1A valida solo el contrato base; el calculo de pagos queda para V1B."
            }
        });
    }
}

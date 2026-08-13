using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: el descuento de línea es porcentaje (0-100) en
/// todos los DTOs públicos que lo exponen. Cubre los dos puntos de entrada reales: el request de
/// preview de totales (API) y el ViewModel de línea usado por Venta/Create y Venta/Edit (MVC).
/// </summary>
public class VentaDescuentoLineaValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    private static DetalleCalculoVentaRequest DetalleRequest(decimal descuento) => new()
    {
        ProductoId = 1,
        Cantidad = 1,
        PrecioUnitario = 100m,
        Descuento = descuento
    };

    private static VentaDetalleViewModel DetalleViewModel(decimal descuento) => new()
    {
        ProductoId = 1,
        Cantidad = 1,
        PrecioUnitario = 100m,
        Descuento = descuento
    };

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void DetalleCalculoVentaRequest_DescuentoFueraDeRango_Invalido(decimal descuento)
    {
        var results = Validate(DetalleRequest(descuento));

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(DetalleCalculoVentaRequest.Descuento)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void DetalleCalculoVentaRequest_DescuentoEnRango_Valido(decimal descuento)
    {
        var results = Validate(DetalleRequest(descuento));

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(DetalleCalculoVentaRequest.Descuento)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void VentaDetalleViewModel_DescuentoFueraDeRango_Invalido(decimal descuento)
    {
        var results = Validate(DetalleViewModel(descuento));

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(VentaDetalleViewModel.Descuento)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void VentaDetalleViewModel_DescuentoEnRango_Valido(decimal descuento)
    {
        var results = Validate(DetalleViewModel(descuento));

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(VentaDetalleViewModel.Descuento)));
    }
}

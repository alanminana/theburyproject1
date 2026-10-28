using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L9 — reglas de campos de uso de nota de crédito y observaciones de alertas de stock.</summary>
public class DevolucionAlertaCamposValidacionTests
{
    [Theory]
    [InlineData(1.0, null, true)]
    [InlineData(0.01, "Venta 123", true)]
    [InlineData(20000000.0, null, true)]
    [InlineData(0.0, null, false)]
    [InlineData(-5.0, null, false)]
    [InlineData(20000000.01, null, false)]
    [InlineData(10.555, null, false)]
    public void UsoNotaCredito_ValidaMonto(double monto, string? referencia, bool valido)
        => Assert.Equal(valido, DevolucionController.ValidarUsoNotaCredito((decimal)monto, referencia) == null);

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void UsoNotaCredito_ValidaLargoDeReferencia(int largo, bool valido)
        => Assert.Equal(valido, DevolucionController.ValidarUsoNotaCredito(10m, new string('r', largo)) == null);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public void Alerta_ObservacionesExcedidas(object? valor, bool excedida)
    {
        var texto = valor switch { int n => new string('o', n), string s => s, _ => null };
        Assert.Equal(excedida, AlertaStockController.ObservacionesExcedidas(texto));
    }
}

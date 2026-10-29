using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L9 — reglas de campos de observaciones de alertas de stock.</summary>
public class DevolucionAlertaCamposValidacionTests
{
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

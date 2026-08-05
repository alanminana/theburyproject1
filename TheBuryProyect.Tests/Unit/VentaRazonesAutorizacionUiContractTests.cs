namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-F: contrato de UI del partial Views/Venta/_VentaRazonesAutorizacion.cshtml.
/// Corrige el quirk documentado (ValorAsociado = aptitud.Mora?.DiasMaximoMora aplicado también
/// a razones de tipo Punitorio) — nunca debe rotular "Monto:" un valor que representa días, y
/// nunca debe depender de un único ValorAsociado sin unidad para decidir el rótulo.
/// </summary>
public class VentaRazonesAutorizacionUiContractTests
{
    [Fact]
    public void VentaRazonesAutorizacionView_UsaCamposTipadosMontoYDias()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "_VentaRazonesAutorizacion.cshtml"));

        // El partial debe distinguir explícitamente monto de días (nunca un único campo
        // decimal genérico rotulado siempre "Monto").
        Assert.Contains("razon.MontoAsociado", view);
        Assert.Contains("razon.DiasAsociado", view);
        Assert.Contains("Días:", view);
    }

    [Fact]
    public void VentaRazonesAutorizacionView_NoRotulaDiasComoMontoIncondicionalmente()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "_VentaRazonesAutorizacion.cshtml"));

        // El bug original: "Monto:" se mostraba siempre que ValorAsociado.HasValue, sin
        // condicionar por unidad. Ahora el bloque "Monto:" debe depender de mostrarMonto
        // (o del campo tipado), nunca directamente de razon.ValorAsociado.HasValue a secas.
        Assert.DoesNotContain("@if (razon.ValorAsociado.HasValue || razon.ValorLimite.HasValue)", view);
    }

    [Fact]
    public void VentaRazonesAutorizacionView_MantieneCompatibilidadConValorAsociadoLegacy()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "_VentaRazonesAutorizacion.cshtml"));

        // Registros previos a PUN-ML10-F (sin MontoAsociado/DiasAsociado poblados) deben
        // seguir interpretándose vía razon.Unidad — no queda un vacío en pantalla.
        Assert.Contains("razon.Unidad", view);
        Assert.Contains("razon.ValorAsociado", view);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML10-G: contrato de UI del panel de crédito del cliente (Views/Credito/_PanelClientePartial.cshtml).
/// Mismo hallazgo/carryover documentado al cerrar PUN-ML10-F: el rótulo "En mora" mezclaba mora de
/// capital con el campo legacy congelado <c>Cuota.MontoPunitorio</c> (vía <c>CuotaViewModel.SaldoPendiente</c>).
/// Reglas congeladas cubiertas acá: rótulo no ambiguo, separación estricta capital/punitorio, sin
/// <c>Cuota.MontoPunitorio</c>, sin bloque de punitorio vacío/con $0,00 ficticio.
/// </summary>
public class CreditoPanelClienteMoraPunitorioUiContractTests
{
    [Fact]
    public void PanelCliente_NoUsaRotuloAmbiguoEnMora()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "_PanelClientePartial.cshtml"));

        Assert.DoesNotContain("<div class=\"mm-label\">En mora</div>", view);
        Assert.Contains("<div class=\"mm-label\">Mora de capital</div>", view);
    }

    [Fact]
    public void PanelCliente_MoraDeCapitalUsaCampoAutoritativoDedicado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "_PanelClientePartial.cshtml"));

        // Autoridad: CreditoClienteIndexViewModel.MontoMoraCapital (CreditoUiQueryService, predicado
        // canónico EstadoCuotaResolver.EstaEnMoraCapitalDerivado) — nunca una suma de SaldoPendiente
        // en la vista (esa suma mezclaba capital con el campo legacy Cuota.MontoPunitorio).
        Assert.Contains("Model.MontoMoraCapital.ToString(\"C0\")", view);
        Assert.DoesNotContain(".Sum(c => c.SaldoPendiente)", view);
    }

    [Fact]
    public void PanelCliente_PunitorioEsBloqueSeparadoCondicionadoANoVacio()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "_PanelClientePartial.cshtml")).Replace("\r\n", "\n");

        var indiceTitulo = view.IndexOf("Punitorio aplicado pendiente</div>", StringComparison.Ordinal);
        Assert.True(indiceTitulo > 0, "El bloque de punitorio debe existir en la vista.");

        var indiceIf = view.LastIndexOf("@if", indiceTitulo, StringComparison.Ordinal);
        Assert.True(indiceIf > 0, "El bloque de punitorio debe estar condicionado por un @if.");

        var condicion = view.Substring(indiceIf, indiceTitulo - indiceIf);
        Assert.Contains("Model.TienePunitorioAplicadoPendiente", condicion);

        Assert.Contains("Model.MontoPunitorioAplicadoPendiente.ToString(\"C0\")", view);
        Assert.Contains("Model.CuotasConPunitorioAplicadoPendiente", view);
    }

    [Fact]
    public void PanelCliente_NuncaLeeElCampoLegacyCongeladoDeCuota()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "_PanelClientePartial.cshtml"));

        // La única mención admisible de "MontoPunitorio" en este archivo es dentro del comentario que
        // explica por qué NO se lee (ya presente antes de este lote, sobre la celda por-cuota).
        Assert.DoesNotContain("cuota.MontoPunitorio", view);
        Assert.DoesNotContain("c.MontoPunitorio", view);
    }

    [Fact]
    public void PanelCliente_BloqueDePunitorioReutilizaComponentesExistentes()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "_PanelClientePartial.cshtml"));

        var inicioBloque = view.IndexOf("Punitorio aplicado pendiente", StringComparison.Ordinal);
        Assert.True(inicioBloque > 0, "El bloque de punitorio debe existir en la vista.");
        var fragmento = view.Substring(Math.Max(0, inicioBloque - 400), 900);
        Assert.Contains("alert alert-warn", fragmento);
        Assert.Contains("class=\"kv", fragmento);
        Assert.Contains("kv-row", fragmento);
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

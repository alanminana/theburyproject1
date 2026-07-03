using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// FASE 11D: contrato de UI del chip/panel BCRA en Cliente/Details. Verifica que
/// el chip de cabecera distinga nunca-consultado / consulta OK / error sin ultimo
/// exito / error usando ultima consulta valida (en vez de colapsar todo error en
/// "Pendiente"), que el refresh AJAX tenga selectores para sincronizar el chip, y
/// que el ViewModel exponga los campos de ultimo exito BCRA (FASE 11C).
/// </summary>
public class ClienteDetailsBcraUiContractTests
{
    [Fact]
    public void DetailsView_ChipBcraDistingueEstados_NoColapsaTodoErrorEnPendiente()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        Assert.Contains("\"Consulta OK\"", view);
        Assert.Contains("\"Sin consultar\"", view);
        Assert.Contains("\"Sin CUIL\"", view);
        Assert.Contains("\"Usando ultima consulta valida\"", view);
        Assert.Contains("\"Error BCRA\"", view);

        // El bug de FASE 11A (bcraOk ? "Consulta OK" : "Pendiente") ya no debe existir:
        // "Pendiente" solo debe usarse para "Pendiente de consulta" (fecha), no como
        // label del chip de estado.
        Assert.DoesNotContain("bcraOk ? \"Consulta OK\" : \"Pendiente\"", view);
    }

    [Fact]
    public void DetailsView_ChipBcraTieneSelectoresParaSincronizarPorAjax()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        Assert.Contains("id=\"bcra-chip\"", view);
        Assert.Contains("id=\"bcra-chip-icon\"", view);
        Assert.Contains("id=\"bcra-chip-label\"", view);
        Assert.Contains("id=\"bcra-aviso\"", view);
    }

    [Fact]
    public void DetailsView_UsaClasificadorPuroDeAptitudParaSituacionEfectiva()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cliente", "Details_tw.cshtml"));

        // La situacion/descripcion mostradas deben salir de apt.Bcra (mismo
        // clasificador que la tarjeta de aptitud), no de una copia local que
        // ignore el ultimo exito (FASE 11C).
        Assert.Contains("apt?.Bcra?.Situacion", view);
        Assert.Contains("apt?.Bcra?.Mensaje", view);
        Assert.Contains("bcraUsandoUltimoExito", view);
        Assert.Contains("bcraNuncaConsultado", view);
    }

    [Fact]
    public void ClienteDetailsJs_SincronizaChipYAvisoEnRefreshAjax()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-details.js"));

        Assert.Contains("bcra-chip", script);
        Assert.Contains("bcra-chip-icon", script);
        Assert.Contains("bcra-chip-label", script);
        Assert.Contains("bcra-aviso", script);
        Assert.Contains("usandoUltimoExito", script);
        Assert.Contains("nuncaConsultado", script);
        Assert.Contains("tieneCuil", script);
    }

    [Fact]
    public void ClienteController_ActualizarBcraSource_DevuelveCamposDeEstadoEfectivo()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "ClienteController.cs"));

        // El endpoint AJAX debe exponer los flags que el JS necesita para no quedar
        // stale (FASE 11D), y reusar el clasificador puro en vez de duplicar reglas.
        Assert.Contains("ConstruirBcraDetalle(", controller);
        Assert.Contains("usandoUltimoExito", controller);
        Assert.Contains("nuncaConsultado", controller);
        Assert.Contains("tieneCuil", controller);
        Assert.Contains("mensaje = detalle.Mensaje", controller);
    }

    [Fact]
    public void ClienteViewModel_ExponeCamposUltimoExitoBcraFase11C_DefaultNull()
    {
        var vm = new ClienteViewModel();

        Assert.Null(vm.SituacionCrediticiaBcraUltimoExito);
        Assert.Null(vm.SituacionCrediticiaDescripcionUltimoExito);
        Assert.Null(vm.SituacionCrediticiaPeriodoUltimoExito);
        Assert.Null(vm.SituacionCrediticiaUltimoExitoUtc);
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

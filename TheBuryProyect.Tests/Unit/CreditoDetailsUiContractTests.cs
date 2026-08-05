using System.Text.RegularExpressions;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class CreditoDetailsUiContractTests
{
    // ── Tests de vista (contrato de presencia de elementos) ──────────────────

    [Fact]
    public void DetailsView_ContieneBloqueTrazabilidadRestriccionCuotas()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("FuenteRestriccionCuotasSnap", view);
        Assert.Contains("CuotasMaximasPermitidas", view);
        Assert.Contains("MaxCuotasBaseSnap", view);
        Assert.Contains("data-credito-restriccion-cuotas", view);
    }

    [Fact]
    public void DetailsView_MuestraBadgeRestringidoPorProductoCuandoFuenteEsProducto()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Restringido por producto", view);
        Assert.Contains("\"Producto\"", view);
    }

    [Fact]
    public void DetailsView_MuestraSinRestriccionPorProductoCuandoFuenteEsGlobal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Sin restricción por producto", view);
        Assert.Contains("\"Global\"", view);
    }

    [Fact]
    public void DetailsView_MuestraProductoIdRestrictivoSnap()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("ProductoIdRestrictivoSnap", view);
        Assert.Contains("snapshot", view);
    }

    [Fact]
    public void DetailsView_BloqueTrazabilidadEsCondicionalParaNullsSeguros()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        // El bloque debe estar guardado con una condición null-safe
        Assert.Contains("FuenteRestriccionCuotasSnap != null", view);
        Assert.Contains("CuotasMaximasPermitidas.HasValue", view);
        Assert.Contains("MaxCuotasBaseSnap.HasValue", view);
    }

    [Fact]
    public void DetailsView_MuestraMaxCuotasBaseVsEfectivoParaComparar()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Máx. cuotas base", view);
        Assert.Contains("Máx. cuotas efectivo", view);
    }

    // ── Tests de contrato del ViewModel ─────────────────────────────────────

    [Fact]
    public void CreditoViewModel_ExponeCamposSnapDeFase95b_DefaultNull()
    {
        var vm = new CreditoViewModel();

        Assert.Null(vm.FuenteRestriccionCuotasSnap);
        Assert.Null(vm.ProductoIdRestrictivoSnap);
        Assert.Null(vm.MaxCuotasBaseSnap);
        Assert.Null(vm.CuotasMaximasPermitidas);
        Assert.Null(vm.CuotasMinimasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_AceptaFuenteProducto()
    {
        var vm = new CreditoViewModel
        {
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap = 42,
            MaxCuotasBaseSnap = 24,
            CuotasMaximasPermitidas = 12,
        };

        Assert.Equal("Producto", vm.FuenteRestriccionCuotasSnap);
        Assert.Equal(42, vm.ProductoIdRestrictivoSnap);
        Assert.Equal(24, vm.MaxCuotasBaseSnap);
        Assert.Equal(12, vm.CuotasMaximasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_AceptaFuenteGlobal()
    {
        var vm = new CreditoViewModel
        {
            FuenteRestriccionCuotasSnap = "Global",
            MaxCuotasBaseSnap = 36,
            CuotasMaximasPermitidas = 36,
        };

        Assert.Equal("Global", vm.FuenteRestriccionCuotasSnap);
        Assert.Null(vm.ProductoIdRestrictivoSnap);
        Assert.Equal(vm.MaxCuotasBaseSnap, vm.CuotasMaximasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_CamposSnap_NoAlteranTotalesNiCuotasFinancieras()
    {
        var vm = new CreditoViewModel
        {
            MontoAprobado = 100_000,
            CantidadCuotas = 12,
            MontoCuota = 9_500,
            TotalAPagar = 114_000,
            SaldoPendiente = 114_000,
            // Snap — no deben afectar los campos financieros
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap = 7,
            MaxCuotasBaseSnap = 24,
            CuotasMaximasPermitidas = 12,
        };

        Assert.Equal(100_000, vm.MontoAprobado);
        Assert.Equal(12, vm.CantidadCuotas);
        Assert.Equal(9_500, vm.MontoCuota);
        Assert.Equal(114_000, vm.TotalAPagar);
        Assert.Equal(114_000, vm.SaldoPendiente);
    }

    // ── CFTEA de presentación (no confiar en el 0 histórico) ─────────────────

    [Fact]
    public void DetailsView_UsaCfteaPresentacionYMuestraNoCalculadoCuandoEsNull()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("cr.CfteaPresentacion.HasValue", view);
        Assert.Contains("No calculado", view);
    }

    [Fact]
    public void DetailsView_NoImprimeElSnapshotCFTEACrudo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        // El snapshot cr.CFTEA no debe imprimirse directo: en créditos históricos
        // vale 0 sin que eso signifique 0% real (nunca se calculó).
        Assert.DoesNotContain("cr.CFTEA.ToString", view);
    }

    [Fact]
    public void CreditoViewModel_CfteaPresentacion_DefaultNull()
    {
        var vm = new CreditoViewModel();

        Assert.Null(vm.CfteaPresentacion);
    }

    // ── PUN-ML9-B2: detalle read-only por cuota ────────────────────────────

    [Fact]
    public void DetailsView_CargaPunitoriosBajoDemanda_SinMontoPunitorioLegacy()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("data-punitorio-toggle", view);
        Assert.Contains("DetallePunitorioCuota", view);
        Assert.Contains("aria-expanded=\"false\"", view);
        Assert.Contains("aria-controls=\"punitorio-panel-@cuota.Id\"", view);
        Assert.Contains("id=\"punitorio-panel-@cuota.Id\"", view);
        Assert.DoesNotContain("MontoPunitorio", view);
        Assert.DoesNotContain("cuota.SaldoPendiente", view);
    }

    [Fact]
    public void Partial_DistingueCalculadoAplicadoYTotalCobrable()
    {
        var partial = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Views", "Credito", "_PunitorioCuotaDetallePartial.cshtml"));

        Assert.Contains("Capital pendiente", partial);
        Assert.Contains("Punitorio calculado hoy", partial);
        Assert.Contains("Informativo; no se suma hasta estar aplicado", partial);
        Assert.Contains("Punitorio aplicado pendiente", partial);
        Assert.Contains("Total cobrable actual", partial);
        Assert.Contains("FechaCalculoComercial", partial);
    }

    [Fact]
    public void Partial_SeparaAplicacionesYPagos_YProtegeHistorialIncompleto()
    {
        var partial = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Views", "Credito", "_PunitorioCuotaDetallePartial.cshtml"));

        Assert.Contains("Aplicaciones", partial);
        Assert.Contains("Pagos", partial);
        Assert.Contains("Historial incompleto", partial);
        Assert.Contains("No reconstruible", partial);
        Assert.Contains("Sin información suficiente", partial);
        Assert.DoesNotContain("SnapshotJson", partial);
    }

    [Fact]
    public void Partial_FormsUsanNombresRazorReales_YNoExponenCamposFinancieros()
    {
        var partial = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Views", "Credito", "_PunitorioCuotaDetallePartial.cshtml"));

        Assert.Contains("asp-for=\"Acciones.Aplicar.Motivo\"", partial);
        Assert.Contains("asp-for=\"Acciones.Aplicar.CuotaRowVersionBase64\"", partial);
        Assert.Contains("asp-for=\"Acciones.Anulacion.Form.Motivo\"", partial);
        Assert.Contains("asp-for=\"Acciones.Anulacion.Form.PunitorioAplicadoRowVersionBase64\"", partial);
        Assert.Contains("data-punitorio-operation-form", partial);
        Assert.Contains("@Html.AntiForgeryToken()", partial);
        Assert.DoesNotContain("asp-for=\"Acciones.Aplicar.Importe", partial);
        Assert.DoesNotContain("asp-for=\"Acciones.Aplicar.Usuario", partial);
        Assert.DoesNotContain("asp-for=\"Acciones.Aplicar.Autorizado", partial);
        Assert.DoesNotContain("asp-for=\"Acciones.Aplicar.Estado", partial);
        Assert.DoesNotContain("asp-for=\"Acciones.Anulacion.Form.Importe", partial);
    }

    [Fact]
    public void PunitorioActions_CssIncluyeFocoErroresYLayoutMobile()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "credito-module.css"));

        Assert.Contains(".punitorio-operation", css);
        Assert.Contains(".punitorio-operation__field textarea:focus-visible", css);
        Assert.Contains(".punitorio-operation .field-error", css);
        Assert.Contains(".punitorio-confirmation", css);
        Assert.Contains("@media(max-width:560px)", css);
    }

    [Fact]
    public void Partial_ExponeTablasResponsivasConEncabezadosYRegionesAccesibles()
    {
        var partial = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "Views", "Credito", "_PunitorioCuotaDetallePartial.cshtml"));

        Assert.Contains("punitorio-table-wrap", partial);
        Assert.Contains("role=\"region\"", partial);
        Assert.Contains("tabindex=\"0\"", partial);
        Assert.Contains("scope=\"col\"", partial);
        Assert.Contains("aria-labelledby=\"punitorio-title-@Model.CuotaId\"", partial);
    }

    [Fact]
    public void DetailsJavaScript_CacheaEvitaDuplicadosYPermiteRetry_SinFormulaFinanciera()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "credito-details.js"));

        Assert.Contains("dataset.loaded", script);
        Assert.Contains("dataset.loading", script);
        Assert.Contains("data-punitorio-retry", script);
        Assert.Contains("data-punitorio-reload", script);
        Assert.Contains("aria-busy", script);
        Assert.Contains("container.addEventListener('click'", script);
        Assert.DoesNotContain("MontoPunitorio", script);
        Assert.DoesNotContain(".sort(", script);
        Assert.DoesNotContain("Intl.NumberFormat", script);
    }

    [Fact]
    public void DetailsJavaScript_PostEvitaDobleEnvio_InvalidaCache_YPreserva400()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "credito-details.js"));

        Assert.Contains("container.addEventListener('submit'", script);
        Assert.Contains("data-punitorio-operation-form", script);
        Assert.Contains("dataset.inFlight", script);
        Assert.Contains("new FormData(form)", script);
        Assert.Contains("delete panel.dataset.loaded", script);
        Assert.Contains("response.status === 409", script);
        Assert.Contains("loadPanel(toggle, panel, true)", script);
        Assert.Contains("focusFirstFieldError", script);
        Assert.DoesNotContain("PunitorioCalculado", script);
        Assert.DoesNotContain("Porcentaje", script);
    }

    // ── PUN-ML9-D.1 (riesgo 3): los 3 enlaces a PagarCuota conservan returnUrl ─

    [Fact]
    public void DetailsView_EnlacesAPagarCuota_PropaganReturnUrlDeVuelta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("var pagoCuotaReturnUrl = Url.Action(\"Details\", \"Credito\", new { id = cr.Id });", view);
        // Los 3 puntos de entrada a "Pagar cuota" (primera cobrable, primera en mora, fila de cuota).
        Assert.Equal(3, Regex.Matches(view, "asp-action=\"PagarCuota\"[^>]*asp-route-returnUrl=\"@pagoCuotaReturnUrl\"").Count);
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

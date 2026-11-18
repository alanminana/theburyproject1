namespace TheBuryProject.Tests.Unit;

/// <summary>
/// D-02: contratos de las superficies que muestran la regla de referencias verificadas. La decisión es siempre del backend
/// (ClienteAptitudService + ValidacionVentaService + VentaService): las vistas y los scripts solo la presentan.
/// </summary>
public class ReferenciasCreditoUiContractTests
{
    private static string Leer(params string[] partes)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            current = current.Parent;
        Assert.NotNull(current);
        return File.ReadAllText(Path.Combine(new[] { current!.FullName }.Concat(partes).ToArray()));
    }

    [Fact]
    public void ClienteDetails_MuestraVerificadasContraElMinimo_ConEstadoAccesibleYSinDecirQueSonSoloInformativas()
    {
        var vista = Leer("Views", "Cliente", "Details_tw.cshtml");

        Assert.Contains("id=\"referencias-resumen\"", vista);
        Assert.Contains("role=\"status\"", vista);
        Assert.Contains("aria-live=\"polite\"", vista);
        Assert.Contains("Referencias verificadas:", vista);
        Assert.Contains("requeridas para crédito personal", vista);
        Assert.Contains("apt?.Referencias", vista);
        Assert.DoesNotContain("Son informativas", vista);
        // El ancla que usan los enlaces desde Venta, Cotizador y la autorización sigue existiendo.
        Assert.Contains("id=\"referencias-card\"", vista);
    }

    [Fact]
    public void ListadoDeReferencias_ExponeElEstadoParaActualizarElResumenSinRecargar()
    {
        var parcial = Leer("Views", "ClienteReferencia", "_ClienteReferenciasLista.cshtml");
        var js = Leer("wwwroot", "js", "cliente-referencias.js");

        Assert.Contains("data-referencia-estado=\"@((int)r.EstadoVerificacion)\"", parcial);
        Assert.Contains("function actualizarResumen()", js);
        Assert.Contains("[data-referencia-estado=\"2\"]", js);
        // Solo presentación: el JS no decide ninguna regla de crédito.
        Assert.DoesNotContain("authorize", js);
        Assert.DoesNotContain("AutorizarVenta", js);
    }

    [Fact]
    public void ConfiguracionCreditoPersonal_OfreceElMinimoConRangoYEtiqueta()
    {
        var vista = Leer("Views", "ConfiguracionPago", "CreditoPersonal_tw.cshtml");
        var vm = Leer("ViewModels", "CreditoPersonalConfigViewModel.cs");

        Assert.Contains("ReferenciasCredito.ReferenciasVerificadasMinimas", vista);
        Assert.Contains("min=\"0\" max=\"10\"", vista);
        Assert.Contains("readonly=\"@(!puedeEditar)\"", vista);
        Assert.Contains("[Range(0, 10", vm);
    }

    [Fact]
    public void AutorizarVenta_ExplicaLaExcepcionPorReferenciasYEnlazaALaSeccionDelCliente()
    {
        var vista = Leer("Views", "Venta", "Autorizar_tw.cshtml");

        Assert.Contains("data-venta-autorizar-referencias", vista);
        Assert.Contains("TipoRazonAutorizacion.ReferenciasInsuficientes", vista);
        Assert.Contains("segundo usuario", vista);
        Assert.Contains("#referencias-card", vista);
        Assert.Contains("role=\"note\"", vista);
    }

    [Fact]
    public void AutorizarVenta_CadaRazonTieneSuPropiaConfirmacion_YLaDeReferenciasEsExplicitaYAparte()
    {
        var parcial = Leer("Views", "Venta", "_VentaAutorizarRazones.cshtml");
        var pagina = Leer("Views", "Venta", "Autorizar_tw.cshtml");
        var modal = Leer("Views", "Venta", "_VentaDecisionModal.cshtml");
        var controller = Leer("Controllers", "VentaController.cs");

        Assert.Contains("name=\"razones\"", parcial);
        Assert.Contains("<label for=\"@idCheck\"", parcial);
        Assert.Contains("Excepción por referencias insuficientes: solo exime las referencias.", parcial);
        Assert.Contains("<partial name=\"_VentaAutorizarRazones\"", pagina);
        Assert.Contains("<partial name=\"_VentaAutorizarRazones\"", modal);
        Assert.DoesNotContain("No levanta otros bloqueos de crédito.", pagina);
        Assert.Contains("int[]? razones", controller);
        Assert.Contains("Enum.IsDefined(typeof(TipoRazonAutorizacion), r)", controller);
    }

    [Fact]
    public void Wizard_YSimulador_MuestranElMotivoYElEnlaceSinReimplementarLaRegla()
    {
        var wizard = Leer("wwwroot", "js", "venta-create.js");
        var simulador = Leer("wwwroot", "js", "cotizacion-simulador.js");
        var api = Leer("Controllers", "CotizacionApiController.cs");

        Assert.Contains("m.categoria === 7", wizard);
        Assert.Contains("data-venta-referencias-enlace", wizard);
        Assert.Contains("referenciasFaltan(data)", simulador);
        Assert.Contains("data-cotizacion-referencias", simulador);
        Assert.Contains("referencias = new", api);
        // La URL viene del servidor y se acepta solo si es relativa.
        Assert.Contains("startsWith('/')", wizard);
        Assert.Contains("startsWith('/')", simulador);
    }

    [Fact]
    public void LaRegla_VivePorUnLado_EnElBackend_YLaMigracionEsUnaSolaColumnaSinArrastres()
    {
        var migracion = Leer("Migrations", "20261115035751_D02_ReferenciasVerificadasMinimasCredito.cs");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(migracion, "AddColumn<"));
        Assert.Contains("table: \"ConfiguracionesCredito\"", migracion);
        Assert.Contains("defaultValue: 1", migracion);
        Assert.DoesNotContain("CreateTable", migracion);
        Assert.DoesNotContain("DropTable", migracion);
        Assert.DoesNotContain("CreateIndex", migracion);
        Assert.DoesNotContain("AlterColumn", migracion);
    }
}

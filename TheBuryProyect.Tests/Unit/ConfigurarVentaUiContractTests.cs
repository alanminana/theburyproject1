using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class ConfigurarVentaUiContractTests
{
    // ── Tests de vista ───────────────────────────────────────────────────────

    [Fact]
    public void ConfigurarVentaView_ContieneAlertaRestriccionPorProducto()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("Model.MaxCuotasBase > Model.CuotasMaxPermitidas", view);
        Assert.Contains("MaxCuotasCreditoProducto.HasValue", view);
        Assert.Contains("data-credito-restriccion-cuotas-producto", view);
        Assert.Contains("@Model.MaxCuotasBase cuotas", view);
        Assert.Contains("@Model.MaxCuotasCreditoProducto!.Value cuotas", view);
    }

    [Fact]
    public void ConfigurarVentaView_MuestraNombreProductoRestrictivoSiExiste()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("ProductoRestrictivoNombre", view);
        Assert.Contains("data-credito-producto-restrictivo-nombre", view);
    }

    [Fact]
    public void ConfigurarVentaView_MuestraIdComoFallbackSiNombreEsNull()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("ProductoIdRestrictivo.HasValue", view);
        Assert.Contains("ProductoIdRestrictivo.Value", view);
    }

    [Fact]
    public void ConfigurarVentaView_NombreYIdSonAlternativos_NoSimultaneos()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        // Nombre se muestra en un @if, ID como @else if — mutuamente excluyentes
        Assert.Contains("string.IsNullOrWhiteSpace(Model.ProductoRestrictivoNombre)", view);
        Assert.Contains("else if (Model.ProductoIdRestrictivo.HasValue)", view);
    }

    [Fact]
    public void ConfigurarVentaView_AlertaEsCondicional()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        // La alerta debe estar dentro de un @if, no siempre visible
        Assert.Contains("@if (Model.MaxCuotasBase > Model.CuotasMaxPermitidas", view);
    }

    [Fact]
    public void ConfigurarVentaView_ConservaRangoEfectivoActual()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("CuotasMinPermitidas", view);
        Assert.Contains("CuotasMaxPermitidas", view);
    }

    [Fact]
    public void ConfigurarVentaView_TieneAtributoDataParaAutomation()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("data-credito-restriccion-cuotas-producto", view);
    }

    // ML8 — Fase 3: #plan-resumen-vacio usa class="ph hidden". .ph (credito-module.css) no está
    // dentro de ningún @layer; .hidden de Tailwind v4 sí está dentro de @layer utilities. Por CSS
    // Cascade Layers, una regla sin capa siempre gana sobre una con capa, sin importar orden de
    // carga ni especificidad — así que .ph ganaba y el placeholder podía quedar visible. El fix es
    // declarar .hidden sin capa y con !important en credito-module.css (mismo patrón ya usado en
    // venta-module.css/cliente-module.css), para que .hidden siga ganando siempre, incluida esta
    // combinación con .ph.
    [Fact]
    public void CreditoModuleCss_FuerzaHiddenSobreCualquierOtraClaseDeDisplay()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "credito-module.css"));

        Assert.Contains(".hidden {", css);
        Assert.Contains("display: none !important;", css);

        // La regla .hidden debe aparecer antes que .ph (orden de cascada defensivo dentro del
        // mismo archivo, por si algún día .hidden también quedara dentro de un @layer).
        var indiceHidden = css.IndexOf(".hidden {", StringComparison.Ordinal);
        var indicePh = css.IndexOf(".ph {", StringComparison.Ordinal);
        Assert.True(indiceHidden >= 0 && indicePh >= 0 && indiceHidden < indicePh,
            "La regla .hidden debe declararse antes que .ph en credito-module.css.");
    }

    [Fact]
    public void ConfigurarVentaJs_ConservaParametrosAjaxDeSimularPlanVenta()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("/Credito/SimularPlanVenta?", js);
        Assert.Contains("totalVenta", js);
        Assert.Contains("anticipo", js);
        Assert.Contains("cuotas", js);
        Assert.Contains("gastosAdministrativos", js);
        Assert.Contains("fechaPrimeraCuota", js);
    }

    // ML6: el JS ya no manda tasaMensual/metodoCalculo/fuenteConfiguracion a la simulación — el
    // porcentaje sale siempre del plan de cuotas resuelto por el servidor (ver
    // CreditoController.SimularPlanVenta). Sustituye a ConfigurarVentaJs_EnviaVentaIdMetodoYFuenteALaSimulacion
    // (ML7), que exigía lo contrario.
    [Fact]
    public void ConfigurarVentaJs_NoEnviaTasaMetodoNiFuenteALaSimulacion()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.DoesNotContain("params.set('tasaMensual'", js);
        Assert.DoesNotContain("params.set('metodoCalculo'", js);
        Assert.DoesNotContain("params.set('fuenteConfiguracion'", js);
    }

    [Fact]
    public void ConfigurarVentaJs_ConsumeNombresJsonDeSimularPlanVenta()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("data.cuotaEstimada", js);
        Assert.Contains("data.tasaAplicada", js);
        Assert.Contains("data.interesTotal", js);
        Assert.Contains("data.montoFinanciado", js);
        Assert.Contains("data.gastosAdministrativos", js);
        Assert.Contains("data.fechaPrimerPago", js);
        Assert.Contains("data.semaforoEstado", js);
        Assert.Contains("data.semaforoMensaje", js);
        Assert.Contains("data.mostrarMsgIngreso", js);
        Assert.Contains("data.mostrarMsgAntiguedad", js);
    }

    // ── ML7: server-authoritative real (VentaId siempre viaja a la simulación) ─────

    [Fact]
    public void ConfigurarVentaJs_EnviaVentaIdALaSimulacion()
    {
        // Bug central de ML7: la pantalla nunca mandaba ventaId al endpoint de
        // simulación, así que el preview ignoraba planes por producto y el % efectivo
        // multiproducto (siempre resolvía la tasa global lisa). Sin este parámetro,
        // CreditoSimulacionVentaService.SimularAsync no puede ser server-authoritative
        // para esta pantalla: cae siempre a la rama "sin venta". ML6 retiró el envío de
        // metodoCalculo/fuenteConfiguracion que ML7 había agregado junto con ventaId: ya
        // no son elegibles en la UI y el servidor los ignora igual si llegaran (ver
        // ConfigurarVentaJs_NoEnviaTasaMetodoNiFuenteALaSimulacion).
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("hdn-venta-id", js);
        Assert.Contains("params.set('ventaId'", js);
    }

    [Fact]
    public void ConfigurarVentaJs_ConsumeVectorDeCuotasYTotalFinanciadoAutoritativos()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("data.cuotas", js);
        Assert.Contains("data.totalAPagar", js);
        Assert.Contains("data.anticipo", js);
        Assert.Contains("data.totalVenta", js);
        Assert.Contains("data.fuentePorcentaje", js);
    }

    [Fact]
    public void ConfigurarVentaJs_NoCalculaSaldoEnElNavegador()
    {
        // El saldo a financiar se muestra como "Calculando…" hasta que llega la
        // respuesta del servidor; el JS no debe restar anticipo de monto por su cuenta.
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.DoesNotContain("recalcularMontoFinanciado", js);
        Assert.Contains("Calculando", js);
        Assert.DoesNotContain("monto - anticipo", js);
    }

    [Fact]
    public void ConfigurarVentaJs_TieneEstadosDeSimulandoYError()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("planSimulando", js);
        Assert.Contains("mostrarErrorPlan", js);
        Assert.Contains("deshabilitarConfirmar", js);
    }

    [Fact]
    public void ConfigurarVentaView_TieneContenedoresDeEstadoSimulandoYError()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("data-plan-simulando", view);
        Assert.Contains("data-plan-error", view);
        Assert.Contains("id=\"hdn-venta-id\"", view);
    }

    [Fact]
    public void ConfigurarVentaView_UsaVocabularioDeRecargoTotalNoDeTasaMensual()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("Saldo a financiar", view);
        Assert.Contains("Porcentaje de recargo total del plan", view);
        Assert.Contains("Total financiado", view);
        Assert.Contains("Importe del recargo", view);
        Assert.DoesNotContain("Tasa mensual aplicada", view);
    }

    // ML6 — test obligatorio: la UI no debe permitir elegir ni mostrar como fuente del
    // porcentaje ninguno de Global/Manual/PorCliente/Perfil/Producto. Se verifica sobre las dos
    // vistas (standalone y embebida en el wizard de Venta) y sobre el JS.
    [Theory]
    [InlineData("ConfigurarVenta_tw.cshtml")]
    [InlineData("_ConfigurarVentaEmbebida.cshtml")]
    public void ConfigurarVentaView_NoOfreceSelectorDeMetodoNiDePerfil(string archivo)
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", archivo));

        Assert.DoesNotContain("select-metodo-calculo", view);
        Assert.DoesNotContain("select-perfil-credito", view);
        Assert.DoesNotContain("Metodo de calculo", view);
        Assert.DoesNotContain("Perfil de credito", view);
        // El input de porcentaje sigue existiendo, pero ya no es editable ni se postea.
        Assert.DoesNotContain("asp-for=\"TasaMensual\"", view);
    }

    [Fact]
    public void ConfigurarVentaJs_NoTieneLogicaDeMetodoNiDePerfil()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.DoesNotContain("selectMetodo", js);
        Assert.DoesNotContain("selectPerfil", js);
        Assert.DoesNotContain("onMetodoChange", js);
        Assert.DoesNotContain("onPerfilChange", js);
    }

    // ML6.1 — test obligatorio: el badge de fuente del % pinta data.fuentePorcentaje tal cual la
    // manda el servidor (contrato: siempre "Plan"), nunca un literal hardcodeado en el JS que
    // pudiera mostrar "Producto"/"Cliente"/"Manual"/"Global" como si fueran fuentes financieras
    // legítimas. No hay forma de que la vista muestre esos valores sin que el servidor los mande.
    [Fact]
    public void ConfigurarVentaJs_BadgeFuentePintaElValorDelServidorSinLiteralesDeFuenteLegacy()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("badgeTasaFuente.textContent = data.fuentePorcentaje", js);
        Assert.DoesNotContain("badgeTasaFuente.textContent = 'Producto'", js);
        Assert.DoesNotContain("badgeTasaFuente.textContent = 'Cliente'", js);
        Assert.DoesNotContain("badgeTasaFuente.textContent = 'Manual'", js);
        Assert.DoesNotContain("badgeTasaFuente.textContent = 'Global'", js);
    }

    // ── Tests de contrato del ViewModel ─────────────────────────────────────

    [Fact]
    public void ConfigurarVentaView_LeeClienteConfigYPerfilesDesdeViewModelTipado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("Model.ClienteConfigPersonalizada", view);
        Assert.Contains("Model.PerfilesActivos", view);
        Assert.DoesNotContain("ViewBag.ClienteConfigPersonalizada", view);
        Assert.DoesNotContain("ViewBag.PerfilesActivos", view);
    }

    [Fact]
    public void ConfigurarVentaView_ConservaScriptJsonClienteConfigParaJs()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("data-credito-json=\"cliente-config\"", view);
        Assert.Contains("JsonNamingPolicy.CamelCase", view);
        Assert.Contains("JsonSerializer.Serialize(clienteConfig", view);
    }

    /// <summary>
    /// PUN-ML9-D reemplazó el selector multi-cuota (<c>Model.Cuotas</c>/<c>Model.CuotasJson</c>,
    /// que serializaba importes al navegador) por una pantalla de una sola cuota alimentada por
    /// un ViewModel tipado y una preview calculada por el servidor. El contrato que importa hoy
    /// es que la vista siga leyendo del modelo y nunca del ViewBag ni de un JSON de importes.
    /// </summary>
    [Fact]
    public void PagarCuotaView_LeeContextoTipadoYNoSerializaImportesAlNavegador()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "PagarCuota_tw.cshtml"));

        Assert.Contains("@model PagarCuotaPageViewModel", view);
        Assert.Contains("Model.Contexto", view);
        Assert.Contains("Model.Preview", view);
        Assert.DoesNotContain("ViewBag.CuotasJson", view);
        Assert.DoesNotContain("ViewBag.Cuotas", view);
        Assert.DoesNotContain("data-credito-json=\"cuotas\"", view);
        Assert.DoesNotContain("CuotasJson", view);
    }

    [Fact]
    public void IndexView_LeeFilterYClientesDesdeViewModelTipado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Index_tw.cshtml"));

        Assert.Contains("@model CreditoIndexViewModel", view);
        Assert.Contains("Model.Filter", view);
        Assert.Contains("Model.Clientes", view);
        Assert.DoesNotContain("ViewBag.Filter", view);
    }

    [Fact]
    public void ViewModel_ExponeMaxCuotasBase_DefaultMaximoLibre()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.Equal(120, vm.MaxCuotasBase);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_DetectaRestriccionPorProducto()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 36,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 42
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.True(vm.MaxCuotasCreditoProducto.HasValue);
        Assert.Equal(42, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_SinRestriccionProducto_MaxEfectivoIgualABase()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 24,
            MaxCuotasCreditoProducto = null,
            ProductoIdRestrictivo = null
        };

        Assert.False(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.Null(vm.MaxCuotasCreditoProducto);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_NuevosCampos_NoAlteranTasaNiTotales()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            Monto = 50_000m,
            MontoFinanciado = 45_000m,
            CantidadCuotas = 12,
            TasaMensual = 3.5m,
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 7
        };

        Assert.Equal(50_000m, vm.Monto);
        Assert.Equal(45_000m, vm.MontoFinanciado);
        Assert.Equal(12, vm.CantidadCuotas);
        Assert.Equal(3.5m, vm.TasaMensual);
    }

    [Fact]
    public void ViewModel_RestriccionPorProductoSinProductoIdRestrictivo_SigueMostrandoAlerta()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 6,
            MaxCuotasCreditoProducto = 6,
            ProductoIdRestrictivo = null
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.True(vm.MaxCuotasCreditoProducto.HasValue);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_ExponeProductoRestrictivoNombre_DefaultNull()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.Null(vm.ProductoRestrictivoNombre);
    }

    [Fact]
    public void ViewModel_ConNombreProductoRestrictivo_MuestraEnAlerta()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 36,
            CuotasMaxPermitidas = 6,
            MaxCuotasCreditoProducto = 6,
            ProductoIdRestrictivo = 17,
            ProductoRestrictivoNombre = "Notebook Lenovo IdeaPad"
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.Equal("Notebook Lenovo IdeaPad", vm.ProductoRestrictivoNombre);
        Assert.Equal(17, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_ConNombreVacio_FallbackAIdNumericos()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 5,
            ProductoRestrictivoNombre = null
        };

        Assert.Null(vm.ProductoRestrictivoNombre);
        Assert.Equal(5, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_NombreProducto_NoAlteraTasaNiTotales()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            Monto = 80_000m,
            MontoFinanciado = 70_000m,
            CantidadCuotas = 6,
            TasaMensual = 4.5m,
            ProductoRestrictivoNombre = "TV Samsung 55"
        };

        Assert.Equal(80_000m, vm.Monto);
        Assert.Equal(70_000m, vm.MontoFinanciado);
        Assert.Equal(6, vm.CantidadCuotas);
        Assert.Equal(4.5m, vm.TasaMensual);
    }

    [Fact]
    public void ViewModel_ExponeClienteConfigPersonalizadaTipadaConDefaultsSeguros()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.NotNull(vm.ClienteConfigPersonalizada);
        Assert.False(vm.ClienteConfigPersonalizada.TieneConfiguracionCliente);
        Assert.Equal(120, vm.ClienteConfigPersonalizada.MaxCuotasBase);
    }

    [Fact]
    public void ViewModel_ExponePerfilesActivosTipadosConListaVacia()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.NotNull(vm.PerfilesActivos);
        Assert.Empty(vm.PerfilesActivos);
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

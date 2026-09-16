using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionControllerUiTests
{
    [Fact]
    public void CotizacionController_RequiereAutorizacionYPermisoVentasView()
    {
        Assert.Contains(
            typeof(CotizacionController).GetCustomAttributes<AuthorizeAttribute>(),
            a => a.GetType() == typeof(AuthorizeAttribute));

        var permiso = typeof(CotizacionController).GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("cotizaciones", permiso.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    [Fact]
    public void Index_RetornaVistaTwSeparada()
    {
        var controller = CreateController();

        var result = controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
    }

    [Fact]
    public void Controller_NoDependeDeVentaServiceNiCajaNiStock()
    {
        var constructor = Assert.Single(typeof(CotizacionController).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ICotizacionService), parameterTypes);
        Assert.Contains(typeof(IProductoService), parameterTypes);
        Assert.Contains(typeof(IClienteService), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Venta", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Caja", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void View_ConsumeApiCotizacionYScriptPropio()
    {
        // El markup del cotizador vive en el parcial reutilizable (lo comparten la
        // vista de pantalla completa y la pestaña Cotizar de Venta/Create); la vista
        // sólo aporta layout, antiforgery y assets.
        var root = FindRepoRoot();
        var partial = File.ReadAllText(Path.Combine(root, "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        var view = File.ReadAllText(Path.Combine(root, "Views", "Cotizacion", "Index_tw.cshtml"));

        Assert.Contains("data-simular-url=\"@Url.Content(\"~/api/cotizacion/simular\")\"", partial);
        Assert.Contains("data-guardar-url=\"@Url.Content(\"~/api/cotizacion/guardar\")\"", partial);
        Assert.Contains("data-productos-url=\"@Url.Action(\"BuscarProductos\", \"Cotizacion\")\"", partial);
        Assert.Contains("data-clientes-url=\"@Url.Action(\"BuscarClientes\", \"Cotizacion\")\"", partial);
        Assert.Contains("~/js/cotizacion-simulador.js", view);

        var compuesta = view + "\n" + partial;
        Assert.DoesNotContain("venta-create.js", compuesta);
        Assert.DoesNotContain("asp-controller=\"Venta\"", compuesta);
    }

    [Fact]
    public void Script_PosteaCotizacionReadOnlyYNoDependeDeVentaCreate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("JSON.stringify(buildRequest())", script);
        Assert.Contains("opcionSeleccionada: state.opcionSeleccionada", script);
        Assert.Contains("urls.guardar", script);
        Assert.Contains("method: 'POST'", script);
        Assert.Contains("data-cotizacion-medio", script);
        Assert.Contains("productos: state.productos.map", script);
        Assert.DoesNotContain("venta-create", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/ventas/BuscarProductos", script);
        Assert.DoesNotContain("/api/ventas/BuscarClientes", script);
    }

    [Fact]
    public void Script_EnviaAnticipoYNoMezclaImportePesosConPorcentajeDeRecargo()
    {
        // ML8: costoFinancieroTotal es un importe en pesos (informativo, usado aparte en el
        // desglose de Credito personal); mezclarlo en el Math.max de recargoValor infla el %
        // mostrado en la comparativa a miles de "%" para cualquier plan de Credito personal.
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("anticipo:", script);
        Assert.Contains("els.anticipo", script);
        Assert.Contains("function recargoValor(plan)", script);
        Assert.DoesNotContain("plan.costoFinancieroTotal || 0));", script);
        Assert.Contains("formatearVectorCuotas", script);
        Assert.Contains("plan.saldoAFinanciar", script);
        Assert.Contains("plan.totalFinanciado", script);
        Assert.Contains("plan.fuentePorcentaje", script);
    }

    // COTIZACION-SIMULAR-GUARDAR-DIRECTO-01 (pedido explícito del usuario: Guardar y
    // Pasar a venta como una sola acción, sin modal de confirmación intermedio):
    // el modal "Guardar cotización" (resumen + confirmar) se retira por completo —
    // #cotizacion-guardar guarda directo y encadena a pasarAVenta() cuando hay un
    // cliente de sistema seleccionado (ver guardarYPasarAVenta en
    // cotizacion-simulador.js). Reemplaza a Modal_Guardar_MuestraOpcionSeleccionadaNoMejorOpcion,
    // que protegía copy de ese modal ya eliminado.
    [Fact]
    public void CotizadorForm_NoDeclaraModalGuardarPropio()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        Assert.DoesNotContain("id=\"modal-guardar\"", partial);
        Assert.DoesNotContain("id=\"cotizacion-guardar-confirm\"", partial);
        Assert.DoesNotContain("onclick=\"openModal('modal-guardar')\"", partial);
    }

    [Fact]
    public void Partial_HeaderNoDuplicaElAvisoDeSoloSimulacion()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        // Antes: la frase del breadcrumb + 3 pills repitiendo "No crea venta" /
        // "No toca stock" / "No registra caja". Ahora una sola representación.
        Assert.DoesNotContain("topbar-flag", partial);
        Assert.DoesNotContain("No crea venta", partial);
        Assert.DoesNotContain("No toca stock", partial);
        Assert.DoesNotContain("No registra caja", partial);
        Assert.Contains("No modifica venta, stock ni caja", partial);
    }

    [Fact]
    public void EstadoDeSimulacion_NoCuadruplicaSenales()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        var scriptUi = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador-ui.js"));

        // La pill propia del panel Resultados se retira (redundante con el banner
        // del header, que queda como único indicador ambiental).
        Assert.DoesNotContain("resultados-status-pill", partial);
        Assert.Contains("id=\"estado-banner\"", partial);
        // El hint junto al CTA arranca oculto: sólo lo muestra setQuoteState para
        // pending/error (contextual), no en idle/simulated/saved (ambiental ya
        // cubierto por el banner). COTIZACION-WORKSTATION-01: el markup ya no
        // precarga las clases de presentación del hint (setQuoteState las reescribe
        // enteras en cada cambio de estado, así que duplicarlas acá sólo las dejaba
        // desincronizadas); lo que el contrato protege es que arranque oculto.
        Assert.Contains("id=\"cotizacion-simular-estado\" class=\"hidden\"", partial);
        Assert.Contains("hint: ''", scriptUi);
    }

    [Fact]
    public void Script_RecargoCeroEsNeutralYSinEntDotHash()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.DoesNotContain("ent-dot", script);
        Assert.DoesNotContain("function entColor(", script);
        Assert.Contains("function recargoClass(", script);
        // 0% no es un éxito (verde): sólo > 0 sigue siendo ámbar.
        Assert.Contains("if (r > 0) return 'text-amber-300';", script);
    }

    // Item 30 del lote: RequiereCliente y Crédito personal dependen del cliente —
    // antes cambiarlo no invalidaba la simulación vigente (sólo lo hacían medios/
    // descuentos/anticipo/productos).
    [Fact]
    public void Script_CambioDeClienteInvalidaLaSimulacion()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        var inicio = script.IndexOf("function setCliente(", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "No se encontró la función setCliente en cotizacion-simulador.js");
        var fin = script.IndexOf("\n    async function buscarClientes", StringComparison.Ordinal);
        Assert.True(fin > inicio, "No se pudo delimitar el cuerpo de setCliente");
        var cuerpo = script[inicio..fin];

        Assert.Contains("invalidarSimulacion()", cuerpo);
    }

    // Hooks protegidos por contrato (no renombrar/eliminar): selección de fila,
    // mejor opción global y el payload real de guardado.
    [Fact]
    public void Script_HooksDeSeleccionYGuardadoIntactos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        Assert.Contains("tr.dataset.cotizacionOpcionKey = key;", script);
        Assert.Contains("classList.toggle('selected'", script);
        Assert.Contains("let bestKey = null, bestTotal = Infinity;", script);
        Assert.Contains("opcionSeleccionada: state.opcionSeleccionada", script);
        Assert.Contains("simulacion: buildRequest()", script);
        Assert.Contains("tr.parent", partial + script);
        Assert.Contains("tr.detail", partial + script);
    }

    // COTIZACION-SIMULAR-REDESIGN-VISUAL-POLISH-01 (a partir de acá): valores de
    // lectura sin apariencia de input, jerarquía de CTA por estado, toast dentro
    // del topbar, cliente sin DNI duplicado y filtros de medios con una sola señal.

    [Fact]
    public void Toast_ViveDentroDelTopbarNoComoOverlayDeWorkspace()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        var topbarInicio = partial.IndexOf("<header class=\"cotz-topbar\">", StringComparison.Ordinal);
        var topbarFin = partial.IndexOf("</header>", StringComparison.Ordinal);
        var feedbackIndex = partial.IndexOf("id=\"cotizacion-feedback\"", StringComparison.Ordinal);
        Assert.True(topbarInicio >= 0 && topbarFin > topbarInicio, "No se encontró <header class=\"cotz-topbar\">...</header>");
        Assert.True(feedbackIndex > topbarInicio && feedbackIndex < topbarFin,
            "#cotizacion-feedback debe vivir dentro de <header class=\"cotz-topbar\">, no como overlay separado de .cotz-app.");
        // aria-live se preserva: el toast sigue siendo anunciado por lectores de pantalla.
        Assert.Contains("aria-live=\"polite\"", partial);

        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "cotizacion-simulador.css"));
        Assert.Contains(".cotz-app .cotz-topbar #cotizacion-feedback:not(.hidden)", css);
        // No position:fixed al viewport ni position:absolute con offsets medidos en px/rem.
        Assert.DoesNotContain(".cotz-feedback { position: absolute", css);
    }

    [Fact]
    public void CtaSimularGuardar_SimuladaDegradaSimularASecundaria()
    {
        var scriptUi = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador-ui.js"));

        // Con simulación vigente, Simular pierde btn-primary y pasa a btn-soft;
        // en cualquier otro estado (idle/pending/error) recupera btn-primary — es
        // la única acción disponible ahí y no debe leerse como secundaria.
        Assert.Contains("const esSecundaria = state === 'simulated';", scriptUi);
        Assert.Contains("simularBtn.classList.toggle('btn-primary', !esSecundaria);", scriptUi);
        Assert.Contains("simularBtn.classList.toggle('btn-soft', esSecundaria);", scriptUi);

        // COTIZACION-WORKSTATION-01 (§10/§17 del pedido, cambio de intención explícito
        // del usuario): "Guardar" DEJA de ser la acción con peso visual fuerte. Antes
        // era .btn-success (verde, ancho completo) y además hacía dos cosas —
        // persistir Y convertir a venta. Ahora son dos botones con jerarquía clara:
        // Guardar cotización es secundaria (.btn-soft) y sólo persiste; Continuar con
        // esta opción es la única primaria (.btn-primary) del cierre. Con Simular
        // degradado a .btn-soft al simular (arriba), queda un solo primario visible
        // por estado, que es lo que el pedido exige.
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        Assert.Contains("id=\"cotizacion-guardar\" type=\"button\" class=\"btn btn-soft\"", partial);
        Assert.Contains("id=\"cotizacion-continuar\" type=\"button\" class=\"btn btn-primary\"", partial);
        Assert.DoesNotContain("btn-success", partial);
    }

    [Fact]
    public void Descuento_NeutralPorDefectoSoloVerdeConDescuentoReal()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        // Markup: sin verde hardcodeado ($0,00 en verde no comunica nada).
        Assert.Contains("id=\"cotizacion-descuento\" class=\"seg-value text-white total-display\"", partial);
        Assert.DoesNotContain("id=\"cotizacion-descuento\" class=\"seg-value text-emerald", partial);
        // JS: sólo se destaca cuando el importe es real.
        Assert.Contains("const hayDescuento = Number(data.descuentoTotal) > 0;", script);
        Assert.Contains("els.descuento.classList.toggle('text-emerald-400', hayDescuento);", script);
    }

    [Fact]
    public void ClienteSeleccionado_NombreSinDniDuplicado()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        // Antes: cliente.display (que ya trae "Apellido, Nombre - DNI: ...") se
        // usaba como nombre Y el DNI se repetía debajo — el sufijo empujaba el
        // truncamiento del nombre. Ahora arma "Apellido, Nombre" desde los campos
        // sueltos que ya vienen en el payload de BuscarClientes.
        Assert.Contains("`${cliente.apellido}, ${cliente.nombre}`", script);
        Assert.Contains("function formatDocumento(value)", script);

        // La acción de cambiar/quitar el cliente sigue disponible (no se tocó el flujo
        // ni el id, que es el hook del listener). COTIZACION-WORKSTATION-01 (§5): pasó
        // de un botón-ícono "✕" con aria-label a un botón con texto visible
        // "Cambiar" en el encabezado de Cliente — con nombre accesible propio ya no
        // necesita aria-label, y el label visible dice qué hace en vez de obligar a
        // interpretar un ícono.
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        Assert.Contains("id=\"cotizacion-limpiar-cliente\"", partial);
        var botonCambiar = partial.IndexOf("id=\"cotizacion-limpiar-cliente\"", StringComparison.Ordinal);
        Assert.Contains("Cambiar", partial[botonCambiar..(botonCambiar + 260)]);
    }

    [Fact]
    public void FiltrosDeMedios_UnaSolaSenalDeIncluido()
    {
        var partial = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "cotizacion-simulador.css"));

        // Antes: <span class="dot"> + fondo/borde del chip repetían el mismo
        // estado "incluido" con dos señales de color. El checkbox y su
        // data-cotizacion-medio (contrato con el JS) no cambian.
        Assert.DoesNotContain("<span class=\"dot\"></span>", partial);
        Assert.DoesNotContain(".medio-chip .dot", css);
        Assert.Contains("data-cotizacion-medio=\"incluirEfectivo\"", partial);
        Assert.Contains(".medio-chip:has(input:checked)", css);
    }

    // El leak se reproduce sólo embebido en Venta/Create (bajo #venta-create-page),
    // no en Cotizacion/Index standalone — cubierto en vivo con Playwright porque
    // depende de la cascada real entre dos hojas de estilo (ver evidencia del
    // entregable). Acá se fija el contrato mínimo: el override debe existir,
    // estar scopeado a .cotz-app y no editar el archivo del otro módulo.
    [Fact]
    public void TotalDisplay_NeutralizaLeakDeVentaPageWizardSinTocarloAOtroModulo()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "cotizacion-simulador.css"));

        Assert.Contains(".cotz-app .total-display {", css);
        Assert.Contains("border: none !important;", css);
        Assert.Contains("background: none !important;", css);

        var ventaWizardCss = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "venta-page-wizard.css"));
        Assert.Contains("#venta-create-page .total-display", ventaWizardCss);
    }

    [Fact]
    public void Layout_TieneAccesoSeparadoACotizacion()
    {
        var layout = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("var canViewCotizaciones = User.TienePermiso(\"cotizaciones\", \"view\")", layout);
        Assert.Contains("var canViewInventario = canViewCotizaciones", layout);
        Assert.DoesNotContain("venta-create", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetallesView_ContieneBotonConversionYScriptConversion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("cotizacion-btn-convertir", view);
        Assert.Contains("data-cotizacion-conversion", view);
        Assert.Contains("data-preview-url=", view);
        Assert.Contains("data-convertir-url=", view);
        Assert.Contains("data-clientes-url=", view);
        Assert.Contains("data-venta-edit-url=", view);
        Assert.Contains("~/js/cotizacion-conversion.js", view);
        Assert.Contains("cotizacion-conversion-modal", view);
        Assert.Contains("cotizacion-btn-confirmar-conversion", view);
        Assert.DoesNotContain("venta-create.js", view);
        Assert.DoesNotContain("asp-controller=\"Venta\"", view);
    }

    [Fact]
    public void DetallesView_BotonConvertir_VerificaPermisoConvert()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("TienePermiso(\"cotizaciones\", \"convert\")", view);
    }

    [Fact]
    public void DetallesView_MuestraBadgeParaEstadosNoConvertibles()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("ConvertidaAVenta", view);
        Assert.Contains("Cancelada", view);
        Assert.Contains("ya fue convertida a venta", view);
        Assert.Contains("cancelada y no puede convertirse", view);
    }

    [Fact]
    public void ScriptConversion_NoDependeDeVentaCreateNiApiVentas()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-conversion.js"));

        Assert.Contains("data-cotizacion-conversion", script);
        Assert.Contains("urls.preview", script);
        Assert.Contains("urls.convertir", script);
        Assert.Contains("urls.ventaEdit", script);
        Assert.Contains("clienteFaltante", script);
        Assert.Contains("usarPrecioCotizado", script);
        Assert.Contains("confirmarAdvertencias", script);
        Assert.Contains("clienteIdOverride", script);
        Assert.DoesNotContain("venta-create", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/ventas/", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VentaService", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScriptConversion_UsaTextContentParaDatosExternos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-conversion.js"));

        // Verificar que los datos del servidor se aplican via textContent (no via string interpolacion insegura)
        Assert.Contains("li.textContent = texto", script);
        Assert.Contains("clearChildren", script);
        Assert.Contains("appendItems", script);
    }

    [Fact]
    public async Task Imprimir_CotizacionExistente_DevuelveVistaImprimir_tw()
    {
        var controller = CreateController();

        var result = await controller.Imprimir(42);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Imprimir_tw", view.ViewName);
        var model = Assert.IsType<CotizacionResultado>(view.Model);
        Assert.Equal(42, model.Id);
    }

    [Fact]
    public async Task Imprimir_CotizacionInexistente_DevuelveNotFound()
    {
        var controller = new CotizacionController(
            new StubCotizacionServiceVacio(), new StubNullPdfService(), new StubProductoService(), new StubClienteService(),
            NullLogger<CotizacionController>.Instance);

        var result = await controller.Imprimir(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void ImprimirView_ContieneBotonImprimirYWindowPrint()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("window.print()", view);
        Assert.Contains("Imprimir", view);
        Assert.Contains("history.back()", view);
    }

    [Fact]
    public void ImprimirView_TieneLayoutNullYNoDependeDeLayout()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("Layout = null", view);
        Assert.DoesNotContain("_Layout", view);
    }

    [Fact]
    public void ImprimirView_NoContieneBotonesOperativos()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.DoesNotContain("btn-convertir", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("btn-cancelar", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("asp-action", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CotizacionConversion", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImprimirView_ContieneDisclaimerDeCotizacion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("sujeta a disponibilidad", view);
        Assert.Contains("Descargar PDF", view);
    }

    [Fact]
    public void ImprimirView_UsaModeloCotizacionResultado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("@model CotizacionResultado", view);
        Assert.Contains("Model.Numero", view);
        Assert.Contains("Model.Detalles", view);
        Assert.Contains("Model.TotalBase", view);
    }

    [Fact]
    public void DetallesView_ContieneEnlaceImprimir()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("Imprimir", view);
        Assert.Contains("\"Imprimir\", \"Cotizacion\"", view);
        Assert.Contains("target=\"_blank\"", view);
    }

    private static CotizacionController CreateController() =>
        new(new StubCotizacionService(), new StubNullPdfService(), new StubProductoService(), new StubClienteService(), NullLogger<CotizacionController>.Instance);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "TheBuryProyect.csproj")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo.");
    }

    private sealed class StubProductoService : IProductoService
    {
        public Task<IEnumerable<Producto>> GetAllAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto?> GetByIdAsync(int id) => Task.FromResult<Producto?>(null);
        public Task<Producto?> GetByIdParaHistorialAsync(int id) => Task.FromResult<Producto?>(null);
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto> CreateAsync(Producto producto) => Task.FromResult(producto);
        public Task<Producto> UpdateAsync(Producto producto) => Task.FromResult(producto);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task ResolverIvaVentaAsync(Producto producto) => Task.CompletedTask;
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => Task.FromResult(new List<int>());
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => Task.FromResult<IEnumerable<ProductoVentaDto>>(Array.Empty<ProductoVentaDto>());
        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId) => Task.FromResult<ProductoPrecioVentaResultado?>(null);
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => Task.FromResult(new Producto { Id = id });
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => Task.FromResult(new Producto { Id = id });
        public Task<bool> ToggleDestacadoAsync(int id) => Task.FromResult(false);
        public Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad) => Task.CompletedTask;
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => Task.FromResult(false);
    }

    private sealed class StubCotizacionServiceVacio : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado());

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(null);

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionVencimientoResultado());
    }

    private sealed class StubCotizacionService : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado { Id = 1, Numero = "COT-TEST" });

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(new CotizacionResultado { Id = id, Numero = "COT-TEST" });

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { Exitoso = true, CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionVencimientoResultado { Exitoso = true });
    }

    private sealed class StubClienteService : IClienteService
    {
        public Task<IEnumerable<Cliente>> GetAllAsync() => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<Cliente?> GetByIdAsync(int id) => Task.FromResult<Cliente?>(null);
        public Task<Cliente> CreateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<Cliente> UpdateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task<IEnumerable<Cliente>> SearchAsync(string? searchTerm = null, string? tipoDocumento = null, bool? soloActivos = null, bool? conCreditosActivos = null, decimal? puntajeMinimo = null, string? orderBy = null, string? orderDirection = null, string? nivelRiesgo = null) => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<(List<Cliente> Items, int Total, int PageNumber)> SearchPagedAsync(string? searchTerm = null, string? tipoDocumento = null, bool? soloActivos = null, bool? conCreditosActivos = null, decimal? puntajeMinimo = null, string? nivelRiesgo = null, string? orderBy = null, string? orderDirection = null, int page = 1, int pageSize = 25) => Task.FromResult((new List<Cliente>(), 0, 1));
        public Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null) => Task.FromResult(false);
        public Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento) => Task.FromResult<Cliente?>(null);
        public Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo) => Task.CompletedTask;
        public Task<bool> AsignarNivelCreditoManualAsync(int clienteId, int nivel, string motivo, string usuario) => Task.FromResult(false);
        public Task<bool> LimpiarNivelCreditoManualAsync(int clienteId, string motivo, string usuario) => Task.FromResult(false);
        public Task<List<ClientePuntajeHistorialItemViewModel>> GetHistorialPuntajeAsync(int clienteId, int top = 5) => Task.FromResult(new List<ClientePuntajeHistorialItemViewModel>());
    }

    private sealed class StubNullPdfService : ICotizacionPdfService
    {
        public byte[] Generar(CotizacionResultado cotizacion) => Array.Empty<byte>();
    }
}

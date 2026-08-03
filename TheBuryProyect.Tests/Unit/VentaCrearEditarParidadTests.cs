namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Micro-lote 7 — Paridad Crear/Editar Venta.
/// Garantiza que ambas pantallas comparten una única composición central
/// (parcial <c>_VentaWizardForm</c>), el mismo módulo JavaScript, el mismo orden
/// de pasos y el mismo contrato de resumen, para eliminar el riesgo de divergencia
/// futura entre Create_tw y Edit_tw. Las diferencias permitidas son solo las
/// inherentes al modo (seed de edición, id, breadcrumb, copy de botones).
/// </summary>
public class VentaCrearEditarParidadTests
{
    private const string Partial = "_VentaWizardForm.cshtml";

    // ── Composición compartida ──────────────────────────────────────────

    [Fact]
    public void AmbasVistas_RenderizanElMismoParcialCentral()
    {
        var create = ReadView("Create_tw.cshtml");
        var edit = ReadView("Edit_tw.cshtml");

        Assert.Contains("<partial name=\"_VentaWizardForm\" model=\"Model\" />", create);
        Assert.Contains("<partial name=\"_VentaWizardForm\" model=\"Model\" />", edit);
    }

    [Fact]
    public void VistasFinas_NoDuplicanElCuerpoDelWizard()
    {
        // El cuerpo del wizard (pasos, paneles, form) vive solo en el parcial.
        // Las vistas finas no deben contener markup del wizard duplicado.
        var create = ReadView("Create_tw.cshtml");
        var edit = ReadView("Edit_tw.cshtml");

        foreach (var body in new[] { create, edit })
        {
            Assert.DoesNotContain("id=\"step-panel-cliente\"", body);
            Assert.DoesNotContain("id=\"tbody-detalles\"", body);
            Assert.DoesNotContain("id=\"btn-confirmar\"", body);
            Assert.DoesNotContain("<form id=\"venta-form\"", body);
        }
    }

    [Fact]
    public void AmbasVistas_CarganElMismoModuloJsEnElMismoOrden()
    {
        var create = ReadView("Create_tw.cshtml");
        var edit = ReadView("Edit_tw.cshtml");

        // El wizard va ANTES que venta-create.js: parsea el seed
        // #venta-inicial-json a window.ventaInicial antes de que venta-create.js
        // hidrate la edición. Si se invierte, Editar no precarga (Micro-lote 7).
        var scripts = new[]
        {
            "~/js/horizontal-scroll-affordance.js",
            "~/js/venta-module.js",
            "~/js/venta-page-wizard.js",
            "~/js/venta-create.js"
        };

        Assert.Equal(ScriptOrden(create, scripts), scripts);
        Assert.Equal(ScriptOrden(edit, scripts), scripts);
    }

    [Fact]
    public void NingunaVista_CargaUnModuloJsExclusivoDeEdicion()
    {
        // No debe existir un venta-edit.js paralelo: el cálculo/render es compartido.
        var create = ReadView("Create_tw.cshtml");
        var edit = ReadView("Edit_tw.cshtml");

        foreach (var body in new[] { create, edit })
        {
            Assert.DoesNotContain("venta-edit.js", body);
            Assert.DoesNotContain("venta-edit-form.js", body);
            Assert.DoesNotContain("venta-modal-rework.js", body);
            Assert.DoesNotContain("venta-crear-modal.js", body);
        }

        Assert.Contains("~/js/venta-create.js", create);
        Assert.Contains("~/js/venta-create.js", edit);
    }

    // ── Wizard de 4 pasos idénticos y en el mismo orden ─────────────────

    [Fact]
    public void ParcialCompartido_TienePasoCreditoCondicionalEnOrdenCanonico()
    {
        var partial = ReadPartial();

        var pasos = new[] { "cliente", "productos", "pago", "credito", "revision" };
        var indices = pasos
            .Select(p => partial.IndexOf($"id=\"step-panel-{p}\"", StringComparison.Ordinal))
            .ToArray();

        for (var i = 0; i < pasos.Length; i++)
        {
            Assert.True(indices[i] >= 0, $"Falta step-panel-{pasos[i]} en el parcial compartido.");
            Assert.Contains($"id=\"step-btn-{pasos[i]}\"", partial);
            Assert.Contains($"aria-controls=\"step-panel-{pasos[i]}\"", partial);
        }

        // Orden estricto de los paneles.
        for (var i = 1; i < indices.Length; i++)
        {
            Assert.True(indices[i] > indices[i - 1],
                $"El paso {pasos[i]} debe ir después de {pasos[i - 1]}.");
        }

        // No hay un 5.º paso "crédito": la verificación vive dentro de "pago".
        Assert.Contains("id=\"step-panel-credito\"", partial);
        Assert.Contains("id=\"step-btn-credito\"", partial);
        var pago = partial.IndexOf("id=\"step-panel-pago\"", StringComparison.Ordinal);
        var verif = partial.IndexOf("id=\"panel-verificacion-crediticia\"", StringComparison.Ordinal);
        var revision = partial.IndexOf("id=\"step-panel-revision\"", StringComparison.Ordinal);
        Assert.True(verif > pago && verif < revision,
            "La verificación crediticia debe vivir dentro del paso de pago, antes de revisión.");
    }

    // ── Contrato de resumen y campos críticos: fuente única ─────────────

    [Fact]
    public void ContratoDeResumen_ProvieneDelParcialCompartido()
    {
        var partial = ReadPartial();

        foreach (var hook in new[]
        {
            "data-side-cliente", "data-side-items", "data-side-pago",
            "data-side-subtotal", "data-side-descuento", "data-side-iva", "data-side-total",
            "data-rev-cliente", "data-rev-fecha", "data-rev-pago", "data-rev-items",
            "data-rev-subtotal", "data-rev-descuento", "data-rev-iva", "data-rev-total",
            "data-mobile-total"
        })
        {
            Assert.Contains(hook, partial);
        }
    }

    [Fact]
    public void FuentesDeResumen_HeroPresentesEnElParcial()
    {
        // Regresión Micro-lote 7: al extraer el cuerpo del wizard al parcial
        // compartido se perdieron los elementos fuente hero-* que Edit_tw tenía.
        // venta-create.js (actualizarResumenOperacion) ESCRIBE en estos ids y
        // venta-page-wizard.js (refreshSummary + MutationObserver) los LEE para
        // espejar cliente/items/pago hacia data-rev-*/data-side-*/data-conf-*.
        // Sin ellos el resumen queda clavado en "Sin seleccionar / 0 productos /
        // Sin definir" en Crear y Editar por igual.
        var partial = ReadPartial();

        foreach (var id in new[]
        {
            "hero-cliente", "hero-cliente-detalle", "hero-detalles-count",
            "hero-tipo-pago", "hero-total"
        })
        {
            Assert.Contains($"id=\"{id}\"", partial);
        }

        // El JS canónico (no modificado) sigue dependiendo de estos ids como fuente.
        var root = FindRepoRoot();
        var create = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-create.js"));
        Assert.Contains("'#hero-cliente'", create);
        Assert.Contains("'#hero-detalles-count'", create);
        Assert.Contains("'#hero-tipo-pago'", create);

        var wizard = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-page-wizard.js"));
        Assert.Contains("'hero-cliente'", wizard);
        Assert.Contains("'hero-detalles-count'", wizard);
        Assert.Contains("'hero-tipo-pago'", wizard);
    }

    [Fact]
    public void CamposCriticos_MantienenLosMismosNombresEnElParcial()
    {
        var partial = ReadPartial();

        foreach (var id in new[]
        {
            "venta-form", "btn-confirmar", "input-buscar-cliente", "hdn-cliente-id",
            "input-buscar-producto", "hdn-producto-id", "txt-cantidad", "txt-descuento-item",
            "btn-agregar-producto", "tbody-detalles", "detalles-hidden-inputs",
            "select-tipo-pago", "total-subtotal", "total-descuento", "total-iva",
            "total-final", "hdn-subtotal", "hdn-descuento", "hdn-iva", "hdn-total",
            "panel-tarjeta", "panel-cheque", "panel-credito-personal", "panel-planes-pago",
            "panel-verificacion-crediticia", "panel-documentacion-faltante", "modal-documentacion"
        })
        {
            Assert.Contains($"id=\"{id}\"", partial);
        }

        Assert.Contains("name=\"DatosTarjeta.ConfiguracionTarjetaId\"", partial);
        Assert.Contains("name=\"DatosCheque.NumeroCheque\"", partial);
        Assert.Contains("asp-for=\"AplicarExcepcionDocumental\"", partial);
        Assert.Contains("asp-for=\"MotivoExcepcionDocumentalCreate\"", partial);
    }

    [Fact]
    public void CalculoCliente_NoReapareceDuplicadoFueraDeVentaCreateJs()
    {
        // El preview de totales lo hace solo venta-create.js contra el endpoint
        // CalcularTotalesVenta. No debe haber un segundo módulo recalculando.
        var root = FindRepoRoot();
        var wizard = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-page-wizard.js"));

        Assert.DoesNotContain("CalcularTotalesVenta", wizard);
        Assert.DoesNotContain("recalcularTotales", wizard);

        var create = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-create.js"));
        Assert.Contains("/api/ventas/CalcularTotalesVenta", create);
    }

    // ── Diferencias permitidas: solo las inherentes al modo ─────────────

    [Fact]
    public void ModoCrear_UsaRootYCopyDeCreacion_SinSeedDeEdicion()
    {
        var create = ReadView("Create_tw.cshtml");

        Assert.Contains("id=\"venta-create-page\"", create);
        Assert.DoesNotContain("id=\"venta-edit-page\"", create);
        // El seed de edición no se renderiza en creación (lo controla el parcial por Model.Id).
        Assert.DoesNotContain("id=\"venta-inicial-json\"", create);
    }

    [Fact]
    public void ModoEditar_UsaRootDeEdicion_YActivaSeedDesdeElParcial()
    {
        var edit = ReadView("Edit_tw.cshtml");
        var partial = ReadPartial();

        Assert.Contains("id=\"venta-edit-page\"", edit);
        Assert.DoesNotContain("id=\"venta-create-page\"", edit);

        // El parcial contiene el seed y el hidden Id, activados por esEdicion.
        Assert.Contains("var esEdicion = Model.Id > 0", partial);
        Assert.Contains("id=\"venta-inicial-json\"", partial);
        Assert.Contains("data-seed-target=\"window.ventaInicial\"", partial);
        Assert.Contains("asp-for=\"Id\"", partial);
    }

    [Fact]
    public void ParcialCompartido_DiferenciaLosBotonesSoloPorModo()
    {
        var partial = ReadPartial();

        // Copy dependiente del modo, resuelto en el parcial.
        Assert.Contains("esEdicion ? \"Guardar cambios\"", partial);
        Assert.Contains("Guardar Operación", partial);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string[] ScriptOrden(string body, string[] known)
    {
        return known
            .Select(s => new { s, i = body.IndexOf(s, StringComparison.Ordinal) })
            .Where(x => x.i >= 0)
            .OrderBy(x => x.i)
            .Select(x => x.s)
            .ToArray();
    }

    private static string ReadView(string viewFile)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", viewFile));

    private static string ReadPartial()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", Partial));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

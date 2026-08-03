namespace TheBuryProject.Tests.Unit;

/// <summary>
/// ML6: la sección Crédito Personal de Crear y Editar Producto (modal del catálogo y la
/// página Producto/Edit_tw huérfana) debe hablar de recargo TOTAL, nunca de tasa mensual /
/// TEA / interés compuesto / sistema francés, y el preview debe consumir el vector del
/// servidor sin reconstruir la última cuota en JavaScript (mismo contrato que ML5).
/// </summary>
public class ProductoCreditoPersonalUiContractTests
{
    private static string LeerCatalogoIndex() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Catalogo", "Index_tw.cshtml"));

    private static string LeerProductoEdit() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Producto", "Edit_tw.cshtml"));

    private static string LeerJsCompartido() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "producto-credito-personal-ui.js"));

    private static string LeerJsCrear() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "producto-crear-modal.js"));

    private static string LeerJsEditar() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "producto-editar-modal.js"));

    private static string LeerSeccionCreditoCrear()
    {
        var html = LeerCatalogoIndex();
        var inicio = html.IndexOf("<!-- Section 3: Crédito personal -->", StringComparison.Ordinal);
        var fin = html.IndexOf("<!-- Section 4: Características -->", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "No se encontró la sección Crédito personal del modal Crear.");
        return html[inicio..fin];
    }

    private static string LeerSeccionCreditoEditar()
    {
        var html = LeerCatalogoIndex();
        var inicio = html.IndexOf("<!-- Panel: Crédito personal -->", StringComparison.Ordinal);
        var fin = html.IndexOf("<!-- Panel: Características -->", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "No se encontró el panel Crédito personal del modal Editar.");
        return html[inicio..fin];
    }

    private static void AssertSeccionHablaDeRecargoTotalSinSemanticaMensual(string seccion)
    {
        Assert.Contains("Recargo total", seccion);
        Assert.Contains("saldo financiado", seccion);

        Assert.DoesNotContain("Tasa mensual", seccion);
        Assert.DoesNotContain("tasa mensual", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEA", seccion);
        Assert.DoesNotContain("interés compuesto", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("interes compuesto", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sistema francés", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sistema frances", seccion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeccionCrear_HablaDeRecargoTotalSinSemanticaMensual()
        => AssertSeccionHablaDeRecargoTotalSinSemanticaMensual(LeerSeccionCreditoCrear());

    [Fact]
    public void SeccionEditar_HablaDeRecargoTotalSinSemanticaMensual()
        => AssertSeccionHablaDeRecargoTotalSinSemanticaMensual(LeerSeccionCreditoEditar());

    [Fact]
    public void SeccionProductoEditTw_HablaDeRecargoTotalSinSemanticaMensual()
    {
        var html = LeerProductoEdit();
        var inicio = html.IndexOf("<!-- Crédito personal -->", StringComparison.Ordinal);
        var fin = html.IndexOf("<!-- Características -->", StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio, "No se encontró la sección Crédito personal de Producto/Edit_tw.");
        AssertSeccionHablaDeRecargoTotalSinSemanticaMensual(html[inicio..fin]);
    }

    // -------------------------------------------------------------------------
    // Modelo de estados tri-estado explícito (Hereda / Propio / No disponible),
    // sin valores mágicos: el radio "Modo" es la señal, no un checkbox suelto.
    // -------------------------------------------------------------------------

    [Fact]
    public void SeccionCrear_ExponeLosTresEstadosExplicitos()
    {
        var seccion = LeerSeccionCreditoCrear();
        Assert.Contains("CreditoPersonal.Modo\" value=\"HeredaGlobal\"", seccion);
        Assert.Contains("CreditoPersonal.Modo\" value=\"ConfiguracionPropia\"", seccion);
        Assert.Contains("CreditoPersonal.Modo\" value=\"NoDisponible\"", seccion);
    }

    [Fact]
    public void SeccionEditar_ExponeLosTresEstadosExplicitos()
    {
        var seccion = LeerSeccionCreditoEditar();
        Assert.Contains("CreditoPersonal.Modo\" value=\"HeredaGlobal\"", seccion);
        Assert.Contains("CreditoPersonal.Modo\" value=\"ConfiguracionPropia\"", seccion);
        Assert.Contains("CreditoPersonal.Modo\" value=\"NoDisponible\"", seccion);
    }

    [Fact]
    public void SeccionCrear_YEditar_CierranI9_ElModalCrearYaConfiguraCreditoPersonal()
    {
        // Antes de ML6, producto-crear-modal.js no tenía ninguna referencia a crédito y
        // CreateAjax nacía heredando la global en silencio. Ahora Crear expone el mismo
        // contrato que Editar.
        Assert.Contains("cargarCreditoPersonalCandidatos", LeerJsCrear());
        Assert.Contains("CreditoPersonalCandidatosJson", LeerJsCrear());
    }

    // -------------------------------------------------------------------------
    // Preview: consume el vector del servidor, no reconstruye la última cuota (contrato
    // idéntico a ML5 — ver CreditoPersonalConfigUiContractTests).
    // -------------------------------------------------------------------------

    [Fact]
    public void JsCompartido_PreviewConsumeElVectorDelServidor_SinFormulaPropia()
    {
        var script = LeerJsCompartido();

        Assert.Contains("data.cuotas", script);
        Assert.Contains("vector[0].total", script);
        Assert.Contains("vector[vector.length - 1].total", script);

        Assert.DoesNotContain("Math.pow", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Math.floor", script);
        Assert.DoesNotContain("Math.ceil", script);
        Assert.DoesNotContain("% cuotas", script);
        Assert.DoesNotContain("totalFinanciado -", script);
        Assert.DoesNotContain("totalFinanciado /", script);
    }

    [Fact]
    public void JsEditar_YJsCrear_NoDuplicanLaLogicaDePreview_UsanElModuloCompartido()
    {
        Assert.Contains("ProductoCreditoPersonalUI.renderCards", LeerJsEditar());
        Assert.Contains("ProductoCreditoPersonalUI.renderCards", LeerJsCrear());
        Assert.DoesNotContain("Math.pow", LeerJsEditar(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Math.pow", LeerJsCrear(), StringComparison.OrdinalIgnoreCase);
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

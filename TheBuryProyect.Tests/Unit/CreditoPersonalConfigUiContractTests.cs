namespace TheBuryProject.Tests.Unit;

public class CreditoPersonalConfigUiContractTests
{
    private static string LeerVista() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "ConfiguracionPago", "CreditoPersonal_tw.cshtml"));

    [Fact]
    public void Vista_ExponeLimitesCanonicosPorPuntaje()
    {
        var view = LeerVista();

        Assert.Contains("LimitesPorPuntaje", view);
        Assert.Contains("PuntajeCliente 0-5", view);
        Assert.Contains("PuntajesCreditoLimite", view);
        Assert.Contains("Limites reales por puntaje", view);
    }

    [Fact]
    public void Vista_NoExponeConfiguracionLegacyDeMontos0A10()
    {
        var view = LeerVista();

        Assert.DoesNotContain("MontosPorPuntaje", view);
        Assert.DoesNotContain("MontoPorPuntajeCreditoViewModel", view);
        Assert.DoesNotContain("0-10", view);
        Assert.DoesNotContain("Monto disponible por puntaje", view);
    }

    [Fact]
    public void Vista_NoExponeScoringLegacyDeAprobacion()
    {
        var view = LeerVista();

        Assert.DoesNotContain("ScoringThresholds", view);
        Assert.DoesNotContain("PuntajeMinimoParaAprobacion", view);
        Assert.DoesNotContain("PuntajeMinimoParaAnalisis", view);
        Assert.DoesNotContain("MontoRequiereGarante", view);
        Assert.DoesNotContain("Configuracion avanzada del motor de scoring", view);
    }

    [Fact]
    public void Vista_NoUsaHandlersInline()
    {
        var view = LeerVista();

        Assert.DoesNotContain("onclick=", view);
        Assert.DoesNotContain("openCreditoModal", view);
        Assert.DoesNotContain("m-historial", view);
    }

    // -------------------------------------------------------------------------
    // Micro-lote 5: la sección #s2 (config financiera + planes por cuota) y su modal
    // "nueva cuota" deben hablar de recargo TOTAL, nunca de tasa mensual / TEA / interés
    // compuesto / sistema francés. #s4 Perfiles no se toca: PerfilCredito.TasaMensual es un
    // concepto distinto (solo delimita rango de cuotas, nunca aporta el %) y sigue llamándose
    // así deliberadamente. ML10: #s1 (Resumen operativo) quedó fuera de aquel lote por
    // descuido, no por decisión — su KPI mostraba "Tasa mensual" para el mismo valor de
    // recargo total que #s2 ya rotuló bien; se corrigió y queda cubierto acá también.
    // -------------------------------------------------------------------------

    [Fact]
    public void SeccionS1_NoMencionaTasaMensualParaElRecargoGlobal()
    {
        var seccion = LeerSeccionS1();

        Assert.DoesNotContain("Tasa mensual", seccion);
        Assert.Contains("Recargo total", seccion);
    }

    [Fact]
    public void SeccionS2_NoMencionaSemanticaMensualOCompuesta()
    {
        var seccion = LeerSeccionS2ConModalCuota();

        Assert.DoesNotContain("tasa mensual", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEA", seccion);
        Assert.DoesNotContain("interés compuesto", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("interes compuesto", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sistema francés", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sistema frances", seccion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updateTea", seccion);
    }

    [Fact]
    public void SeccionS2_HablaExplicitamenteDeRecargoTotal()
    {
        var seccion = LeerSeccionS2ConModalCuota();

        Assert.Contains("Recargo total", seccion);
        Assert.Contains("recargo", seccion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeccionS2_PreviewUsaElEndpointCanonico_NoFormulaPropia()
    {
        var script = LeerScriptDeLaVista();

        Assert.Contains("PreviewRecargoCreditoPersonal", script);
        Assert.DoesNotContain("Math.pow", script, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Corrección post-ML5: el preview de cuotas (>1) debe leer el vector exacto
    // que devuelve el endpoint (misma fuente que persiste el guardado real), nunca
    // reconstruir la última cuota multiplicando o restando en JavaScript — esa
    // reconstrucción puede no cerrar contra el total financiado cuando el residuo
    // de redondeo cae en la última cuota (caso reportado: 100.000/12/0%).
    // -------------------------------------------------------------------------

    [Fact]
    public void SeccionS2_PreviewConsumeElVectorDeCuotas_SinReconstruirElResiduo()
    {
        var script = LeerScriptDeLaVista();

        Assert.Contains("data.cuotas", script);
        Assert.Contains("vector[0].total", script);
        Assert.Contains("vector[vector.length - 1].total", script);

        // Nada de aritmética propia para derivar la última cuota a partir del total
        // y la cantidad: el vector ya viene resuelto, solo se lee.
        Assert.DoesNotContain("Math.floor", script);
        Assert.DoesNotContain("Math.ceil", script);
        Assert.DoesNotContain("% cuotas", script);
        Assert.DoesNotContain("totalFinanciado -", script);
        Assert.DoesNotContain("totalFinanciado /", script);
    }

    private static string LeerScriptDeLaVista()
    {
        var view = LeerVista();

        var inicio = view.IndexOf("@section Scripts", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "No se encontró la sección Scripts.");

        return view[inicio..];
    }

    private static string LeerSeccionS1()
    {
        var view = LeerVista();

        var inicioSeccion = view.IndexOf("<section id=\"s1\"", StringComparison.Ordinal);
        var finSeccion = view.IndexOf("<section id=\"s2\"", StringComparison.Ordinal);
        Assert.True(inicioSeccion >= 0 && finSeccion > inicioSeccion, "No se encontró la sección #s1.");

        return view[inicioSeccion..finSeccion];
    }

    private static string LeerSeccionS2ConModalCuota()
    {
        var view = LeerVista();

        var inicioSeccion = view.IndexOf("<section id=\"s2\"", StringComparison.Ordinal);
        var finSeccion = view.IndexOf("<section id=\"s3\"", StringComparison.Ordinal);
        Assert.True(inicioSeccion >= 0 && finSeccion > inicioSeccion, "No se encontró la sección #s2.");
        var seccionS2 = view[inicioSeccion..finSeccion];

        var inicioModal = view.IndexOf("id=\"m-cuota\"", StringComparison.Ordinal);
        var finModal = view.IndexOf("@section Scripts", StringComparison.Ordinal);
        Assert.True(inicioModal >= 0 && finModal > inicioModal, "No se encontró el modal #m-cuota.");
        var modalCuota = view[inicioModal..finModal];

        return seccionS2 + modalCuota;
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

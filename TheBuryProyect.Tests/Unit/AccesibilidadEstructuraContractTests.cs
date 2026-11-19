namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Regresión de los hallazgos de axe-core (WCAG 2.x A/AA + buenas prácticas) del recorrido de accesibilidad de todos los roles
/// (<c>scripts/qa/a11y-responsive/crawl.js</c>): roles ARIA válidos, landmarks únicos, orden de encabezados y un h1 por pantalla.
/// No sustituyen una prueba con lector de pantalla real (NVDA/JAWS: no verificada).
/// </summary>
public class AccesibilidadEstructuraContractTests
{
    private static string Leer(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray()));
    }

    [Fact]
    public void ElLayout_EtiquetaSusLandmarksParaQueSeanUnicos()
    {
        var layout = Leer("Views", "Shared", "_Layout.cshtml");

        Assert.Contains("<aside id=\"sidebar\" aria-label=\"Barra lateral\"", layout);
        Assert.Contains("<nav aria-label=\"Menú principal\"", layout);
    }

    [Fact]
    public void LosFiltrosDeCajas_SonUnGrupoDeBotones_NoUnTablist()
    {
        var vista = Leer("Views", "Caja", "Index_tw.cshtml");

        Assert.Contains("role=\"group\" aria-label=\"Filtrar cajas\"", vista);
        Assert.DoesNotContain("role=\"tablist\" aria-label=\"Filtrar cajas\"", vista);
    }

    [Fact]
    public void LasPestanasDeCreditos_UsanAriaSelected_SinAriaPressed()
    {
        Assert.DoesNotContain("aria-pressed", Leer("Views", "Credito", "Index_tw.cshtml"));
        Assert.DoesNotContain("aria-pressed", Leer("wwwroot", "js", "credito-index.js"));
    }

    [Fact]
    public void LasTarjetasMovilesDeClientes_NoUsanListitemSobreArticle()
    {
        var vista = Leer("Views", "Cliente", "Index_tw.cshtml");

        Assert.DoesNotContain("cliente-mobile-card\" role=\"listitem\"", vista);
        Assert.DoesNotContain("cliente-mobile-list\" role=\"list\"", vista);
    }

    [Theory]
    [InlineData("Views/Cliente/Create_tw.cshtml", "aria-level=\"2\">Recordá")]
    [InlineData("Views/Credito/Index_tw.cshtml", "<h3 aria-level=\"2\">Sin resultados")]
    [InlineData("Views/Caja/Historial_tw.cshtml", "<h3 aria-level=\"2\">Sin cierres")]
    [InlineData("Views/Caja/RegistrarMovimiento_tw.cshtml", "aria-level=\"2\">Impacto en la caja")]
    [InlineData("Views/Caja/RegistrarMovimiento_tw.cshtml", "aria-level=\"2\">Buenas prácticas")]
    public void LosEncabezadosSecundarios_NoSaltanDeNivel(string ruta, string esperado)
    {
        Assert.Contains(esperado, Leer(ruta.Split('/')));
    }

    [Fact]
    public void AccesoDenegado_TieneUnH1_YTextoSecundarioConContrasteSuficiente()
    {
        var vista = Leer("Areas", "Identity", "Pages", "Account", "AccessDenied.cshtml");

        Assert.Contains("<h1 class=\"text-5xl", vista);
        Assert.DoesNotContain("<h2 class=\"text-5xl", vista);
        Assert.DoesNotContain("var(--pal-neutral-350) 45%", vista); // 2,9:1 sobre el fondo: insuficiente
        Assert.DoesNotContain("Restricted Area", vista); // texto decorativo con opacidad 0,18: no cumple contraste
    }

    [Fact]
    public void ElChipDeBancoYBilletera_TieneEstiloEnElCssDeCaja()
    {
        Assert.Contains(".chip-violet", Leer("wwwroot", "css", "caja-module.css"));
    }

    [Fact]
    public void ElBuscadorDeProductosDeCompras_DeclaraRolCombobox_ParaAdmitirAriaExpanded()
    {
        var vista = Leer("Views", "OrdenCompra", "Create_tw.cshtml");

        Assert.Contains("id=\"inp-buscar-producto\" role=\"combobox\"", vista);
        Assert.Contains("aria-expanded=\"false\"", vista);
    }

    [Theory]
    [InlineData("Caja", "Historial_tw.cshtml")]
    [InlineData("Reporte", "ComisionesVendedores_tw.cshtml")]
    [InlineData("OrdenCompra", "Create_tw.cshtml")]
    public void LasColumnasDeAccionesSinTitulo_TienenTextoParaLectoresDePantalla(string carpeta, string archivo)
    {
        var vista = Leer("Views", carpeta, archivo);

        Assert.DoesNotContain("<th></th>", vista);
        Assert.DoesNotContain("w-8\"></th>", vista);
        Assert.Contains("<span class=\"sr-only\">", vista);
    }

    [Theory]
    [InlineData("Reporte", "Margenes_tw.cshtml")]
    [InlineData("Reporte", "Iva_tw.cshtml")]
    [InlineData("Reporte", "ComisionesVendedores_tw.cshtml")]
    [InlineData("Venta", "Details_tw.cshtml")]
    public void LasTablasConDesplazamientoHorizontal_SonAlcanzablesConElTeclado(string carpeta, string archivo)
    {
        var vista = Leer("Views", carpeta, archivo);

        Assert.DoesNotContain("<div class=\"overflow-x-auto\">", vista);
        Assert.Contains("overflow-x-auto", vista);
        Assert.Matches("overflow-x-auto[^\"]*\" tabindex=\"0\"", vista);
    }

    [Fact]
    public void LaBarraDeMediosDePago_NoEsUnLandmarkComplementarioDentroDeMain()
    {
        var vista = Leer("Views", "ConfiguracionPago", "MediosPago_tw.cshtml");

        Assert.DoesNotContain("<aside class=\"methods-sidebar\"", vista);
        Assert.Contains("class=\"methods-sidebar\" role=\"group\"", vista);
    }

    [Fact]
    public void ElEnlaceAlClienteEnElCredito_SeDistingueDelTextoPorSuSubrayado()
    {
        var vista = Leer("Views", "Credito", "Details_tw.cshtml");

        Assert.DoesNotContain("hover:text-white\" style=\"text-decoration:none\">@cliente.NombreCompleto", vista);
        Assert.Contains("underline decoration-slate-500", vista);
    }
}

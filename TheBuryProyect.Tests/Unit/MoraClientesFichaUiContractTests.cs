using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI de Views/Mora/ClientesMora.cshtml y Views/Mora/FichaCliente.cshtml (QA-MORA-UI, micro-lote 3).
/// Antes de este lote ambas acciones devolvían View(modelo) sin vista y respondían 500.
/// Fallan si las vistas desaparecen, si el formulario de filtros deja de coincidir con los
/// parámetros de MoraController.ClientesMora, o si enlazan a pantallas todavía sin vista
/// (RegistrarContacto, RegistrarPromesa, CrearAcuerdo, Configuracion).
/// </summary>
public class MoraClientesFichaUiContractTests
{
    // GET de Mora a los que estas vistas pueden enlazar. Index/Alertas/Logs llegan por otra rama (micro-lotes 1-2).
    private static readonly string[] AccionesPermitidasComoEnlace = { "Index", "Alertas", "Logs", "ClientesMora", "FichaCliente", "RegistrarContacto", "RegistrarPromesa", "CrearAcuerdo", "MarcarPromesaIncumplida", "CambiarEstadoAcuerdo" };

    private static readonly string[] AccionesSinVista = { "Configuracion" };

    private static string RutaVista(string nombre) =>
        Path.Combine(FindRepoRoot(), "Views", "Mora", nombre + ".cshtml");

    private static string LeerVista(string nombre) => File.ReadAllText(RutaVista(nombre));

    private static MethodInfo Accion(string nombre) =>
        typeof(MoraController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == nombre);

    [Theory]
    [InlineData("ClientesMora")]
    [InlineData("FichaCliente")]
    public void Vista_ExisteFisicamente(string accion)
    {
        Assert.True(File.Exists(RutaVista(accion)),
            $"MoraController.{accion} devuelve View(...): sin Views/Mora/{accion}.cshtml la ruta responde 500.");
    }

    [Fact]
    public void ClientesMora_UsaElModeloQueElControllerEnvia()
    {
        var view = LeerVista("ClientesMora");

        Assert.Contains("@model TheBuryProject.ViewModels.Mora.BandejaClientesMoraViewModel", view);
        Assert.Contains("ViewBag.ConteoPrioridad", view);
    }

    [Fact]
    public void FichaCliente_UsaElModeloQueElControllerEnvia()
    {
        Assert.Contains("@model TheBuryProject.ViewModels.Mora.FichaMoraViewModel", LeerVista("FichaCliente"));
    }

    [Theory]
    [InlineData("ClientesMora")]
    [InlineData("FichaCliente")]
    public void Controller_EsGetConPermisoMoraViewYCobranzasViewarrears(string accion)
    {
        Assert.Null(Accion(accion).GetCustomAttribute<HttpPostAttribute>());

        var permiso = Accion(accion).GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("mora", permiso!.Modulo);
        Assert.Equal("view", permiso.Accion);

        // El gate de clase (cobranzas.viewarrears) sigue vigente.
        var deClase = typeof(MoraController).GetCustomAttributes<PermisoRequeridoAttribute>().ToList();
        Assert.Contains(deClase, p => p.Modulo == "cobranzas" && p.Accion == "viewarrears");
    }

    [Fact]
    public void ClientesMora_FiltrosCoincidenConLosParametrosDelController()
    {
        var view = LeerVista("ClientesMora");

        Assert.Contains("asp-action=\"ClientesMora\" method=\"get\"", view);

        var parametros = Accion("ClientesMora").GetParameters().Select(p => p.Name!).ToHashSet();
        var campos = Regex.Matches(view, "<(?:input|select)[^>]*\\sname=\"(?<n>\\w+)\"")
            .Select(m => m.Groups["n"].Value)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[]
        {
            "busqueda", "conAcuerdoActivo", "conPromesaActiva", "diasMaxAtraso", "diasMinAtraso",
            "diasSinContacto", "estadoGestion", "montoMaxVencido", "montoMinVencido",
            "ordenamiento", "prioridad", "sinContactoReciente"
        }, campos);
        foreach (var campo in campos)
            Assert.Contains(campo, parametros);

        // Valores de "ordenamiento" que reconoce MoraService.AplicarOrdenamiento.
        foreach (var orden in new[] { "PrioridadDesc", "PrioridadAsc", "DiasAtrasoDesc", "DiasAtrasoAsc", "MontoDesc", "MontoAsc", "NombreAsc", "NombreDesc" })
            Assert.Contains($"(\"{orden}\",", view);
    }

    [Theory]
    [InlineData("ClientesMora")]
    [InlineData("FichaCliente")]
    public void Enlaces_SoloApuntanAPantallasMoraDisponibles(string vista)
    {
        var view = LeerVista(vista);
        var enlaces = Regex.Matches(view, "asp-controller=\"Mora\"\\s+asp-action=\"(?<a>\\w+)\"")
            .Select(m => m.Groups["a"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(enlaces);
        foreach (var accion in enlaces)
        {
            Assert.Contains(accion, AccionesPermitidasComoEnlace);
            Assert.DoesNotContain(accion, AccionesSinVista);
        }

        foreach (var sinVista in AccionesSinVista)
            Assert.DoesNotContain($"asp-action=\"{sinVista}\"", view);
    }

    [Fact]
    public void ClientesMora_NoOfreceEscrituraNiFormulariosPost()
    {
        var view = LeerVista("ClientesMora");

        Assert.DoesNotContain("method=\"post\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AntiForgeryToken", view);
    }

    [Fact]
    public void FichaCliente_GestionTieneFormulariosPostConAntiforgeryYPermisoDeGestion()
    {
        var view = LeerVista("FichaCliente");

        foreach (var clave in new[] { "contacto", "promesa", "acuerdo" })
            Assert.Contains($"data-mora-gestion=\"{clave}\"", view);

        Assert.DoesNotContain("Próximamente", view);
        Assert.DoesNotContain("aria-disabled=\"true\"", view);
        Assert.Contains("User.TienePermiso(\"mora\", \"manage\")", view);
        Assert.Contains("User.TienePermiso(\"clientes\", \"view\")", view);

        // Todo formulario POST de la ficha lleva su token anti-falsificación.
        var forms = Regex.Matches(view, "<form[^>]*method=\"post\"", RegexOptions.IgnoreCase).Count;
        Assert.True(forms >= 3);
        Assert.Equal(forms, Regex.Matches(view, @"Html\.AntiForgeryToken\(\)").Count);
    }

    [Fact]
    public void ClientesMora_ContemplaErrorVacioSinResultadosYPaginaFueraDeRango()
    {
        var view = LeerVista("ClientesMora");

        Assert.Contains("cargaFallida", view);
        Assert.Contains("data-mora-estado=\"error\"", view);
        Assert.Contains("data-mora-estado=\"clientes-vacio\"", view);
        Assert.Contains("data-mora-estado=\"sin-resultados\"", view);
        Assert.Contains("data-mora-estado=\"pagina-fuera-de-rango\"", view);
    }

    [Fact]
    public void FichaCliente_ContemplaEstadosVaciosPorSeccion()
    {
        var view = LeerVista("FichaCliente");

        foreach (var estado in new[] { "sin-mora", "creditos-vacio", "alertas-vacio", "contactos-vacio", "promesas-vacio", "acuerdos-vacio" })
            Assert.Contains($"data-mora-estado=\"{estado}\"", view);
    }

    [Theory]
    [InlineData("ClientesMora")]
    [InlineData("FichaCliente")]
    public void Vista_CargaSuCssYUsaTarjetasEnTelefono(string vista)
    {
        var view = LeerVista(vista);

        Assert.Contains("~/css/mora-clientes.css", view);
        Assert.Contains("<table data-erp-cards", view);
        Assert.Contains("overflow-x-auto", view);
    }

    [Theory]
    [InlineData("ClientesMora")]
    [InlineData("FichaCliente")]
    public void Vista_QuitaElDocumentoDuplicadoDelNombre(string vista)
    {
        Assert.Contains("\" - DNI: \"", LeerVista(vista));
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
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}

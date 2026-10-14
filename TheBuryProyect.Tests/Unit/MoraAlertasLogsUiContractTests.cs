using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI de Views/Mora/Alertas.cshtml y Views/Mora/Logs.cshtml (MORA-VISTAS, micro-lote 2).
/// Antes de este lote ambas acciones devolvían View() sin vista y el Index enlazaba a dos 500.
/// Fallan si las vistas desaparecen, si el formulario de filtros deja de coincidir con los
/// parámetros de MoraController.Alertas, si enlazan a pantallas sin vista o si las acciones
/// de escritura pierden el gate de permiso, el POST o el antiforgery.
/// </summary>
public class MoraAlertasLogsUiContractTests
{
    // Pantallas GET de Mora que ya tienen vista: las únicas a las que estas vistas pueden enlazar.
    private static readonly string[] PantallasConVista = { "Index", "Alertas", "Logs" };

    private static string RutaVista(string nombre) =>
        Path.Combine(FindRepoRoot(), "Views", "Mora", nombre + ".cshtml");

    private static string LeerVista(string nombre) => File.ReadAllText(RutaVista(nombre));

    private static IEnumerable<MethodInfo> Acciones(string nombre) =>
        typeof(MoraController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == nombre);

    private static MethodInfo AccionPost(string nombre) =>
        Acciones(nombre).Single(m => m.GetCustomAttribute<HttpPostAttribute>() != null);

    [Theory]
    [InlineData("Alertas")]
    [InlineData("Logs")]
    public void Vista_ExisteFisicamente(string accion)
    {
        Assert.True(File.Exists(RutaVista(accion)),
            $"MoraController.{accion} devuelve View(...): sin Views/Mora/{accion}.cshtml la ruta responde 500.");
    }

    [Fact]
    public void Alertas_UsaElModeloQueElControllerEnvia()
    {
        var view = LeerVista("Alertas");

        // El controller devuelve List<AlertaCobranzaViewModel> o Enumerable.Empty<...>() en el catch.
        Assert.Contains("@model IEnumerable<TheBuryProject.ViewModels.AlertaCobranzaViewModel>", view);
        Assert.Contains("ViewBag.ClienteFiltro", view);
    }

    [Fact]
    public void Logs_UsaElModeloQueElControllerEnvia()
    {
        var view = LeerVista("Logs");

        Assert.Contains("@model List<TheBuryProject.Models.Entities.LogMora>", view);
    }

    [Theory]
    [InlineData("Alertas")]
    [InlineData("Logs")]
    public void Controller_MantieneGateDeVistaYEsGet(string accion)
    {
        var metodo = Acciones(accion).Single();

        Assert.Null(metodo.GetCustomAttribute<HttpPostAttribute>());
        var permiso = metodo.GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("mora", permiso!.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    [Fact]
    public void Alertas_FiltrosCoincidenConLosParametrosDelController()
    {
        var view = LeerVista("Alertas");

        Assert.Contains("asp-action=\"Alertas\" method=\"get\"", view);

        var parametros = Acciones("Alertas").Single().GetParameters().Select(p => p.Name!).ToHashSet();
        var campos = Regex.Matches(view, "<(?:input|select)[^>]*\\sname=\"(?<n>\\w+)\"")
            .Select(m => m.Groups["n"].Value)
            .Where(n => n is not ("rowVersion" or "returnUrl"))
            .Distinct()
            .ToList();

        Assert.Equal(new[] { "cliente", "estado", "prioridad", "tipo" }, campos.OrderBy(c => c));
        foreach (var campo in campos)
            Assert.Contains(campo, parametros);

        // Valores de "estado" que reconoce el switch del controller.
        foreach (var estado in new[] { "noLeidas", "leidas", "noResueltas", "resueltas" })
            Assert.Contains($"(\"{estado}\",", view);
    }

    [Theory]
    [InlineData("Alertas")]
    [InlineData("Logs")]
    public void Enlaces_SoloApuntanAPantallasDeMoraConVista(string vista)
    {
        var view = LeerVista(vista);
        var enlaces = Regex.Matches(view, "<a\\s+asp-controller=\"Mora\"\\s+asp-action=\"(?<a>\\w+)\"")
            .Select(m => m.Groups["a"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(enlaces);
        foreach (var accion in enlaces)
        {
            Assert.True(Acciones(accion).Any(), $"{vista} enlaza a Mora/{accion}, que no existe en MoraController.");
            Assert.True(PantallasConVista.Contains(accion) && File.Exists(RutaVista(accion)),
                $"{vista} enlaza a Mora/{accion}, que todavía no tiene vista (respondería 500).");
        }
    }

    [Fact]
    public void Index_EnlazaAAlertasYLogs_QueAhoraTienenVista()
    {
        var index = LeerVista("Index");

        Assert.Contains("asp-controller=\"Mora\" asp-action=\"Alertas\"", index);
        Assert.Contains("asp-controller=\"Mora\" asp-action=\"Logs\"", index);
        Assert.True(File.Exists(RutaVista("Alertas")));
        Assert.True(File.Exists(RutaVista("Logs")));
    }

    [Theory]
    [InlineData("MarcarLeida")]
    [InlineData("Resolver")]
    public void Alertas_AccionesDeEscrituraSonPostConAntiforgeryRowVersionYPermiso(string accion)
    {
        var view = LeerVista("Alertas");

        // Gate de UI.
        Assert.Contains("User.TienePermiso(\"mora\", \"manage\")", view);
        Assert.Contains("@if (puedeGestionar)", view);

        // Formulario POST con antiforgery, concurrencia optimista y retorno a la misma pantalla.
        var form = Regex.Match(view,
            $"<form asp-controller=\"Mora\" asp-action=\"{accion}\"[^>]*method=\"post\"[\\s\\S]*?</form>");
        Assert.True(form.Success, $"No se encontró el formulario POST de {accion}.");
        Assert.Contains("@Html.AntiForgeryToken()", form.Value);
        Assert.Contains("name=\"rowVersion\"", form.Value);
        Assert.Contains("name=\"returnUrl\"", form.Value);

        // El servidor sigue exigiendo POST, antiforgery y mora.manage, independientemente de la vista.
        var post = AccionPost(accion);
        Assert.NotNull(post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal("manage", post.GetCustomAttribute<PermisoRequeridoAttribute>()!.Accion);
        Assert.Contains(post.GetParameters(), p => p.Name == "rowVersion");
    }

    [Fact]
    public void Logs_NoOfreceAccionesDeEscritura()
    {
        var view = LeerVista("Logs");

        Assert.DoesNotContain("method=\"post\"", view);
        Assert.DoesNotContain("EjecutarJob", view);
    }

    [Fact]
    public void Alertas_ContemplaErrorVacioYSinResultados()
    {
        var view = LeerVista("Alertas");

        Assert.Contains("cargaFallida", view);
        Assert.Contains("data-mora-estado=\"error\"", view);
        Assert.Contains("data-mora-estado=\"alertas-vacio\"", view);
        Assert.Contains("data-mora-estado=\"sin-resultados\"", view);
    }

    [Fact]
    public void Logs_ContemplaErrorYVacio()
    {
        var view = LeerVista("Logs");

        Assert.Contains("cargaFallida", view);
        Assert.Contains("data-mora-estado=\"error\"", view);
        Assert.Contains("data-mora-estado=\"logs-vacio\"", view);
    }

    [Theory]
    [InlineData("Alertas")]
    [InlineData("Logs")]
    public void Vista_CargaElCssDeModuloYUsaTarjetasEnTelefono(string vista)
    {
        var view = LeerVista(vista);

        Assert.Contains("~/css/mora-module.css", view);
        Assert.Single(Regex.Matches(view, "<table data-erp-cards"));
        Assert.Contains("overflow-x-auto", view);
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

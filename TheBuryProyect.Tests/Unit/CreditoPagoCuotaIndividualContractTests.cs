using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.ViewModels.PagoCuota;

namespace TheBuryProject.Tests.Unit;

public class CreditoPagoCuotaIndividualContractTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TheBuryProyect.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(segments).ToArray()));

    [Fact]
    public void PagarCuota_GetPostYPreview_ExigenPayInstallmentAdemasDeCreditoView()
    {
        var controllerPermission = Assert.Single(
            typeof(CreditoController).GetCustomAttributes<PermisoRequeridoAttribute>());
        Assert.Equal("creditos", controllerPermission.Modulo);
        Assert.Equal("view", controllerPermission.Accion);

        var endpoints = typeof(CreditoController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name is "PagarCuota" or "PrevisualizarPagoCuota")
            .Where(method => method.GetCustomAttribute<HttpGetAttribute>() is not null ||
                             method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToList();

        Assert.Equal(3, endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var permission = Assert.Single(
                endpoint.GetCustomAttributes<PermisoRequeridoAttribute>());
            Assert.Equal("cobranzas", permission.Modulo);
            Assert.Equal("payinstallment", permission.Accion);
        }
    }

    [Fact]
    public void Formulario_PosteaSoloIntencionYNoExponeFechaEditableNiFinancierosDerivados()
    {
        var view = Read("Views", "Credito", "PagarCuota_tw.cshtml");

        Assert.Contains("Input.MontoIngresado", view);
        Assert.Contains("Input.MedioPago", view);
        Assert.Contains("Input.Comprobante", view);
        Assert.Contains("Input.Observaciones", view);
        Assert.Contains("Input.CuotaRowVersionBase64", view);

        foreach (var forbidden in new[]
                 {
                     "asp-for=\"CreditoId\"",
                     "asp-for=\"CuotaId\"",
                     "asp-for=\"MontoCuota\"",
                     "asp-for=\"MontoPunitorio\"",
                     "asp-for=\"TotalAPagar\"",
                     "asp-for=\"FechaPago\"",
                     "name=\"AplicadoPunitorio\"",
                     "name=\"AplicadoCuota\"",
                     "name=\"Recargo\""
                 })
        {
            Assert.DoesNotContain(forbidden, view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreviewFinanciera_NoSeCalculaEnRazorNiJavaScript()
    {
        var view = Read("Views", "Credito", "PagarCuota_tw.cshtml");
        var script = Read("wwwroot", "js", "credito-pagar-cuota.js");
        var browserCode = view + Environment.NewLine + script;

        Assert.Contains("data-pago-preview-url", browserCode);
        Assert.DoesNotContain("credito-recargos-medio-data", browserCode);
        Assert.DoesNotContain("Math.round", browserCode);
        Assert.DoesNotContain("recargos[", browserCode);
        Assert.DoesNotContain("hdn-punitorio", browserCode);
        Assert.DoesNotContain("hdn-monto-cuota", browserCode);
    }

    [Fact]
    public void LinksDePago_SeOcultanSinPermisoYUsanElIdDeCuotaEnLaRuta()
    {
        var details = Read("Views", "Credito", "Details_tw.cshtml");
        var vencidas = Read("Views", "Credito", "CuotasVencidas_tw.cshtml");

        Assert.Contains("User.TienePermiso(\"cobranzas\", \"payinstallment\")", details);
        Assert.Contains("asp-route-id=\"@cuota.Id\"", details);
        Assert.Contains("User.TienePermiso(\"cobranzas\", \"payinstallment\")", vencidas);
        Assert.Contains("asp-route-id=\"@cuota.Id\"", vencidas);
    }

    /// <summary>
    /// El servidor corre con cultura es-AR (separador decimal ","). <see cref="RangeAttribute"/>
    /// parsea sus límites string con CurrentCulture salvo que se pida lo contrario, y el tag
    /// helper de input los evalúa al renderizar los atributos de validación cliente: un límite
    /// no parseable tira HTTP 500 en el GET completo, no un error de validación.
    /// </summary>
    [Theory]
    [InlineData("es-AR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void MontoIngresado_LimitesDelRango_SeParsenBajoCualquierCultura(string cultura)
    {
        var rango = typeof(PagarCuotaInputModel)
            .GetProperty(nameof(PagarCuotaInputModel.MontoIngresado))!
            .GetCustomAttribute<RangeAttribute>();
        Assert.NotNull(rango);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultura);
            Assert.False(rango!.IsValid(0m));
            Assert.True(rango.IsValid(0.01m));
            Assert.True(rango.IsValid(1_000m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Seeder_Cajero_RecibeCreditoViewYConservaPayInstallmentSinPermisosDePunitorio()
    {
        var seeder = Read("Data", "Seeds", "RolesPermisosSeeder.cs");
        var cajeroStart = seeder.IndexOf("var cajeroRole", StringComparison.Ordinal);
        var cajeroEnd = seeder.IndexOf("var repositorRole", cajeroStart, StringComparison.Ordinal);
        Assert.True(cajeroStart >= 0 && cajeroEnd > cajeroStart);

        var cajeroBlock = seeder[cajeroStart..cajeroEnd];
        Assert.Contains("{ \"creditos\", new[] { \"view\" } }", cajeroBlock);
        Assert.Contains("\"payinstallment\"", cajeroBlock);
        Assert.DoesNotContain("\"applyfine\"", cajeroBlock);
        Assert.DoesNotContain("\"revertfine\"", cajeroBlock);
        Assert.DoesNotContain("\"configuracion\"", cajeroBlock);
        Assert.DoesNotContain("\"scoring\"", cajeroBlock);
    }
}

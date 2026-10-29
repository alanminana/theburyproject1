using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TheBuryProject.Filters;
using Xunit;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H2-B2 P5: generaliza <see cref="AccionesMutantesPermisoContractTests"/> a TODOS los controllers. Una acción que
/// modifica estado (POST/PUT/PATCH/DELETE) debe exigir un permiso que no sea de lectura (<c>view*</c>), salvo las
/// excepciones listadas con su motivo. Los filtros de clase y de método se acumulan.
/// </summary>
public class AccionesMutantesTodosLosControllersContractTests
{
    private static readonly Dictionary<string, string> ExcepcionesPorDiseno = new()
    {
        ["CotizacionApiController.Simular"] = "cálculo de simulación, no persiste",
        ["VentaApiController.CalcularTotalesVenta"] = "cálculo puro de totales, no persiste",
        ["MercadoLibreController.ProbarConexion"] = "sonda de una cuenta ya vinculada",
        ["ReporteController.Ventas"] = "POST de filtros de un reporte de lectura (el GET exige el mismo permiso)",
        ["CreditoController.Create"] = "alta manual retirada: solo redirige",
        ["CreditoController.Edit"] = "edición manual retirada: solo redirige",
        ["TicketController.CambiarEstado"] = "exige changestatus/resolve dentro del handler según el estado destino",
        ["NotaRapidaApiController.*"] = "notas personales: solo el dueño ve y modifica las suyas",
        ["MercadoLibreWebhookController.Recibir"] = "webhook externo anónimo (firma pendiente, diferido por el usuario)",
        ["DiagnosticoController.*"] = "solo SuperAdmin y solo Development",
    };

    private static bool EsExcepcion(Type t, MethodInfo m) =>
        ExcepcionesPorDiseno.ContainsKey($"{t.Name}.{m.Name}") || ExcepcionesPorDiseno.ContainsKey($"{t.Name}.*");

    private static IEnumerable<Type> Controllers() =>
        typeof(PermisoRequeridoAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);

    private static IEnumerable<(Type Tipo, MethodInfo Metodo)> Mutantes() =>
        Controllers().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true)
                .Any(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE")))
            .Select(m => (t, m)));

    [Fact]
    public void TodaAccionMutante_ExigeUnPermisoQueNoSeaDeLectura()
    {
        var sinPermisoDeEscritura = Mutantes()
            .Where(x => !EsExcepcion(x.Tipo, x.Metodo))
            .Where(x => !x.Tipo.GetCustomAttributes<PermisoRequeridoAttribute>(true)
                .Concat(x.Metodo.GetCustomAttributes<PermisoRequeridoAttribute>(true))
                .Any(p => !string.IsNullOrWhiteSpace(p.Modulo) && !p.Accion.Trim().StartsWith("view", StringComparison.OrdinalIgnoreCase))
                && !x.Tipo.GetCustomAttributes<AuthorizeAttribute>(true).Concat(x.Metodo.GetCustomAttributes<AuthorizeAttribute>(true))
                    .Any(a => a is not PermisoRequeridoAttribute && !string.IsNullOrWhiteSpace(a.Roles)))
            .Select(x => $"{x.Tipo.Name}.{x.Metodo.Name}")
            .Distinct().ToList();

        Assert.True(sinPermisoDeEscritura.Count == 0,
            "Acciones mutantes sin permiso de escritura (solo view*): " + string.Join(", ", sinPermisoDeEscritura));
    }

    [Fact]
    public void LasExcepciones_NoTienenEntradasObsoletas()
    {
        var existentes = Mutantes().Select(x => $"{x.Tipo.Name}.{x.Metodo.Name}").ToHashSet();
        var clases = Controllers().Select(t => t.Name).ToHashSet();
        Assert.All(ExcepcionesPorDiseno.Keys, k =>
        {
            if (k.EndsWith(".*", StringComparison.Ordinal)) Assert.Contains(k[..^2], clases);
            else Assert.Contains(k, existentes);
        });
    }

    [Fact]
    public void SoloLosEndpointsDeclaradosPuedenCarecerDeAntiforgery()
    {
        // Mutantes sin ValidateAntiForgeryToken/AutoValidateAntiforgeryToken en método o clase: solo webhook externo,
        // utilitarios de Diagnostico (dev).
        var permitidos = new HashSet<string>
        {
            "MercadoLibreWebhookController.Recibir", "DiagnosticoController.CrearUsuarioPrueba",
            "DiagnosticoController.ResetPassword", "DiagnosticoController.TestLogin",
        };
        var sinAf = Mutantes()
            .Where(x => !x.Metodo.GetCustomAttributes(true).Concat(x.Tipo.GetCustomAttributes(true))
                .Any(a => a is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute))
            .Select(x => $"{x.Tipo.Name}.{x.Metodo.Name}").Distinct().Where(n => !permitidos.Contains(n)).ToList();
        Assert.True(sinAf.Count == 0, "Mutantes sin antiforgery: " + string.Join(", ", sinAf));
    }

    [Fact]
    public void ConfiguracionMora_SaveConfiguracionRetirado()
    {
        // H2-D: endpoint JSON huerfano (sin llamadores) retirado; la configuracion vigente es Mora/GuardarConfiguracionExpandida.
        Assert.Null(typeof(TheBuryProject.Controllers.ConfiguracionMoraController).GetMethod("SaveConfiguracion"));
    }

    [Fact]
    public void Reimprimir_EsPostConAntiforgery()
    {
        // SEG-BAJA: la reimpresión cuenta y audita; no puede colgar de un GET.
        var m = typeof(TheBuryProject.Controllers.DocumentoController).GetMethod("Reimprimir")!;
        Assert.NotNull(m.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(m.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}

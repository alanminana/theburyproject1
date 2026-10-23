using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PROMPT 57-B: la causa raíz de FIN-S-001 fue un <c>[PermisoRequerido]</c> que quedó dentro de un comentario <c>///</c>:
/// la acción solo heredaba el permiso <c>view</c> de la clase. Este contrato falla si una acción que modifica estado
/// (POST/PUT/PATCH/DELETE) en Caja, Venta o MercadoLibre no declara a nivel de método un permiso distinto
/// de <c>view</c>, o si queda anónima. No cambia reglas: fija el estado actual y detecta regresiones por comentario/borrado.
/// </summary>
public class AccionesMutantesPermisoContractTests
{
    private readonly ITestOutputHelper _out;
    public AccionesMutantesPermisoContractTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Controladores() => new[]
    {
        typeof(CajaController), typeof(VentaController), typeof(VentaApiController),
        typeof(MercadoLibreController)
    }.Select(t => new object[] { t });

    /// <summary>
    /// Acciones mutantes que hoy heredan/declaran solo <c>view</c> a propósito. Cada una con su motivo; agregar una
    /// acción nueva acá exige una decisión explícita, no un olvido.
    /// </summary>
    private static readonly Dictionary<string, string> SoloViewPorDiseno = new()
    {
        ["VentaApiController.CalcularTotalesVenta"] = "cálculo puro de totales, no persiste nada",
        ["MercadoLibreController.ProbarConexion"] = "sonda de conexión de una cuenta ya vinculada; no crea ni elimina vínculos",
    };

    private static bool EsMutante(MethodInfo m) =>
        m.GetCustomAttributes<HttpMethodAttribute>(true)
            .Any(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE"));

    private static IEnumerable<MethodInfo> AccionesMutantes(Type controlador) =>
        controlador.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && EsMutante(m));

    [Theory]
    [MemberData(nameof(Controladores))]
    public void CadaAccionMutante_DeclaraUnPermisoDistintoDeViewANivelDeMetodo(Type controlador)
    {
        var acciones = AccionesMutantes(controlador).ToList();
        Assert.NotEmpty(acciones);
        _out.WriteLine($"{controlador.Name}: {acciones.Count} acciones mutantes");

        var sinPermiso = acciones
            .Where(m => !SoloViewPorDiseno.ContainsKey($"{controlador.Name}.{m.Name}"))
            .Where(m => !m.GetCustomAttributes<PermisoRequeridoAttribute>(false)
                .Any(p => !string.IsNullOrWhiteSpace(p.Modulo) && !string.IsNullOrWhiteSpace(p.Accion)
                          && !string.Equals(p.Accion, "view", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(sinPermiso.Count == 0,
            $"{controlador.Name}: acciones mutantes sin permiso propio (solo heredan 'view'): {string.Join(", ", sinPermiso)}");
    }

    [Fact]
    public void LaListaBlanca_NoTieneEntradasObsoletas()
    {
        var existentes = Controladores().Select(o => (Type)o[0])
            .SelectMany(t => AccionesMutantes(t).Select(m => $"{t.Name}.{m.Name}")).ToHashSet();
        Assert.All(SoloViewPorDiseno.Keys, k => Assert.Contains(k, existentes));
    }

    [Theory]
    [MemberData(nameof(Controladores))]
    public void NingunaAccionMutante_EsAnonima(Type controlador)
    {
        var anonimas = AccionesMutantes(controlador)
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any())
            .Select(m => m.Name).ToList();
        Assert.Empty(anonimas);
        Assert.Empty(controlador.GetCustomAttributes<AllowAnonymousAttribute>(true));
    }
}

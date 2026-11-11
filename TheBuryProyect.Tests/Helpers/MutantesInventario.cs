using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Helpers;

/// <summary>
/// F3.1 (2026-11-11): inventario de TODAS las acciones MVC que modifican estado (POST/PUT/PATCH/DELETE), construido desde los
/// descriptores del runtime (respeta <c>[ActionName]</c> y plantillas). Es la fuente única del sondeo en vivo
/// <c>scripts/qa/seguridad/sondeo-post-autorizacion.js</c> (que lee el JSON versionado) y de los tests de autorización sin efectos.
/// </summary>
public static class MutantesInventario
{
    /// <summary>Controllers cuyo control de acceso es por diseño propio (autenticado / dueño del recurso / firma externa).</summary>
    public static readonly HashSet<string> ExcluidosPorDiseno = new() { "NotaRapidaApi", "Home", "MercadoLibreWebhook" };

    public sealed record Parametro(string Nombre, string Tipo, string Fuente);

    public sealed record Accion(
        string Id,
        string Controller,
        string Accion_,
        string Verbo,
        string Ruta,
        string[] Permisos,
        string[][] RolesRequeridos,
        string ContentTypes,
        Parametro[] Parametros,
        bool ExcluidaPorDiseno);

    private static readonly HashSet<string> Mutantes = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public static List<Accion> Descubrir(IServiceProvider services)
    {
        var provider = services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var lista = new List<Accion>();
        foreach (var d in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (d.ControllerTypeInfo.Namespace?.StartsWith("TheBuryProject.Controllers", StringComparison.Ordinal) != true) continue;
            var verbos = d.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).ToList() ?? new List<string>();
            var verbo = verbos.FirstOrDefault(v => Mutantes.Contains(v));
            if (verbo == null) continue;

            var ruta = "/" + (d.AttributeRouteInfo?.Template ?? $"{d.ControllerName}/{d.ActionName}").TrimStart('/');
            var permisos = d.ControllerTypeInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true))
                .Select(p => $"{PermissionAliasHelper.Canon(p.Modulo)}.{PermissionAliasHelper.Canon(p.Accion)}")
                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var roles = d.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(true))
                .Where(a => a is not PermisoRequeridoAttribute && !string.IsNullOrWhiteSpace(a.Roles))
                .Select(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var consumes = d.MethodInfo.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ConsumesAttribute>(true)
                .SelectMany(c => c.ContentTypes.Select(t => t.ToString())).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var parametros = d.Parameters
                .Select(p => new Parametro(p.Name, NombreDeTipo(p.ParameterType), p.BindingInfo?.BindingSource?.Id ?? "Auto"))
                .OrderBy(p => p.Nombre, StringComparer.Ordinal).ToArray();

            lista.Add(new Accion($"{d.ControllerName}.{d.ActionName}:{verbo}", d.ControllerName, d.ActionName, verbo, ruta,
                permisos, roles, string.Join(",", consumes), parametros, ExcluidosPorDiseno.Contains(d.ControllerName)));
        }
        return lista.OrderBy(a => a.Id, StringComparer.Ordinal).ThenBy(a => a.Ruta, StringComparer.Ordinal).ToList();
    }

    private static string NombreDeTipo(Type t)
    {
        var inner = Nullable.GetUnderlyingType(t);
        if (inner != null) return inner.Name + "?";
        return t.IsGenericType ? t.Name.Split('`')[0] + "<>" : t.Name;
    }

    /// <summary>Misma regla que el filtro: faltan permisos (con alias) o no está en un rol exigido por <c>[Authorize(Roles)]</c>.</summary>
    public static bool DebeDenegar(Accion a, IEnumerable<string> claims, string rol)
    {
        var lista = claims.ToList();
        return a.RolesRequeridos.Any(req => !req.Contains(rol, StringComparer.OrdinalIgnoreCase))
               || a.Permisos.Any(p =>
               {
                   var partes = p.Split('.', 2);
                   return !lista.Any(c => PermissionAliasHelper.MatchesPermissionClaim(c, partes[0], partes[1]));
               });
    }

    public static string ASerializado(IEnumerable<Accion> acciones)
    {
        var opciones = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var proyeccion = acciones.Select(a => new
        {
            id = a.Id,
            controller = a.Controller,
            accion = a.Accion_,
            verbo = a.Verbo,
            ruta = a.Ruta,
            permisos = a.Permisos,
            rolesRequeridos = a.RolesRequeridos,
            consumes = a.ContentTypes,
            parametros = a.Parametros.Select(p => new { nombre = p.Nombre, tipo = p.Tipo, fuente = p.Fuente }),
            excluidaPorDiseno = a.ExcluidaPorDiseno,
        });
        return JsonSerializer.Serialize(proyeccion, opciones).Replace("\r\n", "\n") + "\n";
    }

    public static string RaizDelRepo()
    {
        var env = Environment.GetEnvironmentVariable("BURY_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (TheBuryProyect.slnx); definir BURY_REPO_ROOT.");
    }
}

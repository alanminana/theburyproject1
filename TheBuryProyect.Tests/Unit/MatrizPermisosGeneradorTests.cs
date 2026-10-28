using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TheBuryProject.Filters;
using Xunit;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H2-B2 P5: inventario por reflexión de TODAS las acciones MVC. Con la variable de entorno
/// <c>BURY_MATRIZ_PERMISOS_OUT</c> escribe la matriz (TSV); sin ella solo verifica que se puede construir.
/// </summary>
public static class MatrizPermisos
{
    public record Fila(string Controller, string Accion, string Http, string Permisos, string Auth, bool Anonimo, bool Mutante, bool Antiforgery);

    public static List<Fila> Construir()
    {
        var asm = typeof(PermisoRequeridoAttribute).Assembly;
        var filas = new List<Fila>();
        foreach (var t in asm.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic))
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName || m.GetCustomAttribute<NonActionAttribute>() != null) continue;
                var http = m.GetCustomAttributes<HttpMethodAttribute>(true).SelectMany(a => a.HttpMethods).Distinct().ToList();
                var httpTxt = http.Count == 0 ? "GET*" : string.Join("/", http);
                // Los filtros de clase y de método se ACUMULAN (AND): no es "método o clase".
                var permisos = t.GetCustomAttributes<PermisoRequeridoAttribute>(true).Select(p => ("C", p))
                    .Concat(m.GetCustomAttributes<PermisoRequeridoAttribute>(true).Select(p => ("M", p))).ToList();
                var perm = permisos.Count == 0 ? "-" : string.Join(" & ", permisos.Select(x => $"{x.Item1}:{x.Item2.Modulo}.{x.Item2.Accion}"));
                var auths = t.GetCustomAttributes<AuthorizeAttribute>(true).Concat(m.GetCustomAttributes<AuthorizeAttribute>(true))
                    .Where(a => a is not PermisoRequeridoAttribute).Select(a => $"Authorize({a.Roles}{a.Policy})").Distinct().ToList();
                var anon = m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any() || t.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();
                var mut = http.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE");
                var af = m.GetCustomAttributes(true).Concat(t.GetCustomAttributes(true)).Any(a => a is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute);
                filas.Add(new Fila(t.Name, m.Name, httpTxt, perm, string.Join(",", auths), anon, mut, af));
            }
        }
        return filas.OrderBy(f => f.Controller).ThenBy(f => f.Accion).ToList();
    }
}

public class MatrizPermisosGeneradorTests
{
    [Fact]
    public void GeneraMatriz()
    {
        var filas = MatrizPermisos.Construir();
        Assert.NotEmpty(filas);
        var path = Environment.GetEnvironmentVariable("BURY_MATRIZ_PERMISOS_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder();
        sb.AppendLine("Controller\tAccion\tHttp\tPermisos\tAuth\tAnonimo\tMutante\tAntiforgery");
        foreach (var f in filas) sb.AppendLine(string.Join("\t", f.Controller, f.Accion, f.Http, f.Permisos, f.Auth, f.Anonimo, f.Mutante, f.Antiforgery));
        File.WriteAllText(path, sb.ToString());
    }
}

[Collection("HttpIntegration")]
public class MatrizPermisosCatalogoGeneradorTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public MatrizPermisosCatalogoGeneradorTests(CustomWebApplicationFactory f) => _factory = f;

    [Fact]
    public async Task GeneraCatalogoYGrantsPorRol()
    {
        var path = Environment.GetEnvironmentVariable("BURY_MATRIZ_PERMISOS_ROLES_OUT");
        if (string.IsNullOrEmpty(path)) return;
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TheBuryProject.Data.AppDbContext>();
        await TheBuryProject.Data.Seeds.RolesPermisosSeeder.SeedAsync(context,
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>());
        var catalogo = await context.AccionesModulo.Include(a => a.Modulo)
            .Select(a => a.Modulo.Clave + "." + a.Clave).OrderBy(x => x).ToListAsync();
        var grants = await context.RolPermisos.Where(r => !r.IsDeleted)
            .Join(context.Roles, rp => rp.RoleId, r => r.Id, (rp, r) => new { r.Name, rp.ClaimValue }).ToListAsync();
        var sb = new StringBuilder();
        foreach (var c in catalogo) sb.AppendLine("CATALOGO\t" + c);
        foreach (var g in grants.OrderBy(g => g.Name).ThenBy(g => g.ClaimValue)) sb.AppendLine("GRANT\t" + g.Name + "\t" + g.ClaimValue);
        File.WriteAllText(path, sb.ToString());
    }
}

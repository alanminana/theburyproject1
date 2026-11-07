using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regla aprobada: autorizar ventas (<c>ventas.authorize</c>) se reserva a Gerente y superiores; el rol Vendedor no lo tiene.
/// Cubre (1) lo que entrega el sembrador, (2) que un Vendedor con el permiso heredado de una base antigua queda denegado
/// en la SOLICITUD SIGUIENTE a la baja del permiso (sesión ya abierta) aunque la venta sea de otro usuario, y (3) el
/// contrato del script de datos <c>scripts/sql/vendedor-sin-ventas-authorize.sql</c> (solo Vendedor + ventas.authorize).
/// </summary>
[Collection("HttpIntegration")]
public class VendedorSinAutorizarVentasHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public VendedorSinAutorizarVentasHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        await RolesPermisosSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HashSet<string>> PermisosVigentesAsync(string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleId = await context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
        return (await context.RolPermisos.Where(p => p.RoleId == roleId && !p.IsDeleted).Select(p => p.ClaimValue).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElSembrador_NoEntregaVentasAuthorizeAlVendedor_PeroSiAGerenteAdministradorYSuperAdmin()
    {
        Assert.DoesNotContain("ventas.authorize", await PermisosVigentesAsync(Roles.Vendedor));
        foreach (var rol in new[] { Roles.Gerente, Roles.Administrador, Roles.SuperAdmin })
            Assert.Contains("ventas.authorize", await PermisosVigentesAsync(rol));
    }

    [Fact]
    public async Task VendedorConElPermisoHeredado_QuedaDenegadoEnLaSolicitudSiguienteALaBaja_TambienEnVentasAjenas()
    {
        // Estado de una base antigua: la fila de ventas.authorize del Vendedor existe y está vigente.
        string userId;
        int rolPermisoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleId = await context.Roles.Where(r => r.Name == Roles.Vendedor).Select(r => r.Id).SingleAsync();
            var accion = await context.AccionesModulo.Include(a => a.Modulo)
                .SingleAsync(a => a.Modulo.Clave == "ventas" && a.Clave == "authorize");
            var fila = await context.RolPermisos.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.RoleId == roleId && p.AccionId == accion.Id);
            if (fila == null)
            {
                fila = new RolPermiso { RoleId = roleId, ModuloId = accion.ModuloId, AccionId = accion.Id, ClaimValue = "ventas.authorize", RowVersion = new byte[8] };
                context.RolPermisos.Add(fila);
            }
            fila.IsDeleted = false;
            await context.SaveChangesAsync();
            rolPermisoId = fila.Id;

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = $"vend_{Guid.NewGuid():N}"[..16], Email = $"{Guid.NewGuid():N}@test.com", EmailConfirmed = true, Activo = true };
            Assert.True((await userManager.CreateAsync(user, "Test123!")).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, Roles.Vendedor)).Succeeded);
            userId = user.Id;
        }

        var client = _factory.CreateClientWithUserId(userId);
        const int ventaDeOtroUsuario = 987654; // el filtro de permiso responde antes de buscar la venta

        // Con el permiso vigente el filtro NO deniega (la venta inexistente se resuelve después, con un redirect normal).
        Assert.False(await AutorizarDenegadoAsync(client, ventaDeOtroUsuario));

        // Se da de baja el permiso (lo que hace el script de datos) con la sesión ya abierta.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fila = await context.RolPermisos.IgnoreQueryFilters().SingleAsync(p => p.Id == rolPermisoId);
            fila.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        // Mismo cliente, siguiente solicitud: denegado en GET y en POST.
        Assert.True(await AutorizarDenegadoAsync(client, ventaDeOtroUsuario));
    }

    private static async Task<bool> AutorizarDenegadoAsync(HttpClient client, int ventaId)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var token = WebUtility.HtmlDecode(match.Groups[1].Value);

        using var get = await client.GetAsync($"/Venta/Autorizar/{ventaId}");
        using var post = new HttpRequestMessage(HttpMethod.Post, $"/Venta/Autorizar/{ventaId}");
        post.Headers.Add("RequestVerificationToken", token);
        post.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("id", ventaId.ToString()),
            new KeyValuePair<string, string>("motivo", "QA"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });
        using var postResp = await client.SendAsync(post);

        static bool Denegado(HttpResponseMessage r) =>
            r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || ((int)r.StatusCode is >= 300 and < 400
                && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

        var getDenegado = Denegado(get) || (get.RequestMessage?.RequestUri?.AbsolutePath.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false);
        var postDenegado = Denegado(postResp);
        Assert.Equal(getDenegado, postDenegado);
        return postDenegado;
    }

    [Fact]
    public void ElScriptDeDatos_SoloToca_ElPermisoVentasAuthorize_DelRolVendedor_Y_ExigeDryRunYBaseEsperada()
    {
        var sql = File.ReadAllText(Path.Combine(Raiz(), "scripts", "sql", "vendedor-sin-ventas-authorize.sql"));

        Assert.Contains("$(DryRun)", sql);
        Assert.Contains("$(BaseEsperada)", sql);
        Assert.Contains("DB_NAME() <> @BaseEsperada", sql);
        Assert.Contains("N'Vendedor'", sql);
        // Fuera del bloque de comentario inicial no se nombra ningún otro rol.
        var codigo = sql[(sql.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        Assert.DoesNotMatch(@"vendedor2|vendedor pobre|Gerente|Administrador|SuperAdmin", codigo);
        // Toda condición de escritura está acotada al rol y al permiso.
        Assert.Matches(@"UPDATE dbo\.RolPermisos SET IsDeleted = 1[\s\S]*?WHERE RoleId = @RoleId AND ClaimValue = N'ventas\.authorize' AND IsDeleted = 0", sql);
        Assert.Matches(@"DELETE FROM dbo\.AspNetRoleClaims[\s\S]*?WHERE RoleId = @RoleId AND ClaimType = N'Permission' AND ClaimValue = N'ventas\.authorize'", sql);
        Assert.DoesNotContain("DELETE FROM dbo.RolPermisos", sql);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        // Solo hay un UPDATE y un DELETE.
        Assert.Single(Regex.Matches(sql, @"^UPDATE ", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(sql, @"^DELETE FROM ", RegexOptions.Multiline));
    }

    private static string Raiz()
    {
        var actual = new DirectoryInfo(AppContext.BaseDirectory);
        while (actual != null)
        {
            if (File.Exists(Path.Combine(actual.FullName, "TheBuryProyect.csproj")))
                return actual.FullName;
            actual = actual.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}

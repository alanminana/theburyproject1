using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// SEC-S-005: bloquear a un usuario (LockoutEnd) no cerraba su sesión viva; solo se rechazaba el login nuevo. El validador de
/// la cookie ahora rechaza la sesión de un usuario bloqueado en el siguiente request.
/// </summary>
public class ActiveUserCookieValidatorTests : IDisposable
{
    private const string Stamp = "stamp-1";
    private readonly SqliteConnection _connection = new($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private readonly ServiceProvider _provider;
    private readonly FakeAuth _auth = new();

    public ActiveUserCookieValidatorTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IOptions<IdentityOptions>>(Options.Create(new IdentityOptions()));
        services.AddSingleton<IAuthenticationService>(_auth);
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task<string> SembrarUsuarioAsync(bool activo = true, bool lockoutEnabled = true, DateTimeOffset? lockoutEnd = null)
    {
        using var scope = _provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid().ToString("N");
        ctx.Users.Add(new ApplicationUser
        {
            Id = id, UserName = "u" + id[..6], NormalizedUserName = "U" + id[..6].ToUpperInvariant(),
            Activo = activo, SecurityStamp = Stamp, LockoutEnabled = lockoutEnabled, LockoutEnd = lockoutEnd
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task<bool> SesionVigenteAsync(string userId)
    {
        var http = new DefaultHttpContext { RequestServices = _provider.CreateScope().ServiceProvider };
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, Stamp)
        }, "Cookies");
        var scheme = new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Cookies");
        var context = new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket);

        await ActiveUserCookieValidator.ValidateAsync(context);

        return context.Principal != null;
    }

    [Fact]
    public async Task UsuarioActivoSinBloqueo_ConservaLaSesion()
        => Assert.True(await SesionVigenteAsync(await SembrarUsuarioAsync()));

    [Fact]
    public async Task UsuarioBloqueado_PierdeLaSesionViva()
    {
        var id = await SembrarUsuarioAsync(lockoutEnd: DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(await SesionVigenteAsync(id));
        Assert.True(_auth.SignOutLlamado);
    }

    [Fact]
    public async Task BloqueoVencido_ConservaLaSesion()
        => Assert.True(await SesionVigenteAsync(await SembrarUsuarioAsync(lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(-5))));

    [Fact]
    public async Task UsuarioInactivo_PierdeLaSesion()
        => Assert.False(await SesionVigenteAsync(await SembrarUsuarioAsync(activo: false)));

    private sealed class FakeAuth : IAuthenticationService
    {
        public bool SignOutLlamado { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { SignOutLlamado = true; return Task.CompletedTask; }
    }
}

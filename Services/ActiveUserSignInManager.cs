using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace TheBuryProject.Services;

/// <summary>
/// SignInManager que además de las reglas estándar de Identity exige <see cref="ApplicationUser.Activo"/>.
/// <see cref="CanSignInAsync"/> es el punto por el que pasan PasswordSignInAsync y
/// CheckPasswordSignInAsync (vía PreSignInCheck), así que un usuario desactivado recibe
/// <see cref="SignInResult.NotAllowed"/> antes de validar la contraseña (no consume intentos fallidos
/// ni cambia el estado de bloqueo). Es independiente del bloqueo (LockoutEnd) y de los permisos.
/// </summary>
public sealed class ActiveUserSignInManager : SignInManager<ApplicationUser>
{
    public ActiveUserSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!user.Activo)
        {
            Logger.LogWarning("Inicio de sesión rechazado: usuario {UserId} desactivado.", user.Id);
            return false;
        }

        return await base.CanSignInAsync(user);
    }
}

/// <summary>
/// Revoca en el siguiente request protegido las sesiones (cookies) de usuarios desactivados o
/// eliminados, o cuyo SecurityStamp rotó (p. ej. al desactivarlos, o al resetear su contraseña).
/// Se consulta la base en CADA request, sin caché: una caché haría que una sesión revocada siguiera
/// funcionando durante el TTL. El SecurityStampValidator estándar sólo revalida cada 30 minutos,
/// por eso no alcanza para esta regla. Una sola proyección por PK (Activo + SecurityStamp).
/// </summary>
public static class ActiveUserCookieValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var vigente = false;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var services = context.HttpContext.RequestServices;
            var db = services.GetRequiredService<AppDbContext>();
            var stampClaimType = services.GetRequiredService<IOptions<IdentityOptions>>()
                .Value.ClaimsIdentity.SecurityStampClaimType;
            var stampDeLaSesion = context.Principal!.FindFirstValue(stampClaimType);

            var actual = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Activo, u.SecurityStamp, u.LockoutEnabled, u.LockoutEnd })
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            // SEC-S-005: un usuario bloqueado (LockoutEnd vigente) pierde la sesión viva en el siguiente request, no solo el login.
            var bloqueado = actual is { LockoutEnabled: true, LockoutEnd: not null }
                && actual.LockoutEnd > DateTimeOffset.UtcNow;

            vigente = actual is { Activo: true }
                && !bloqueado
                && !string.IsNullOrEmpty(stampDeLaSesion)
                && string.Equals(actual.SecurityStamp, stampDeLaSesion, StringComparison.Ordinal);
        }

        if (!vigente)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}

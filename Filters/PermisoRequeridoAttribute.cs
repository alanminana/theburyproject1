using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TheBuryProject.Helpers;
using RoleConstants = TheBuryProject.Models.Constants.Roles;

namespace TheBuryProject.Filters;

/// <summary>
/// Attribute para requerir un permiso específico (claims-based)
/// Uso: [PermisoRequerido(Modulo = "ventas", Accion = "create")]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class PermisoRequeridoAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    /// <summary>
    /// Clave del módulo (ventas, productos, clientes, etc.)
    /// </summary>
    public string Modulo { get; set; } = string.Empty;

    /// <summary>
    /// Clave de la acción (view, create, update, delete, authorize, etc.)
    /// </summary>
    public string Accion { get; set; } = string.Empty;

    /// <summary>
    /// Si es true, permite acceso a <see cref="RoleConstants.SuperAdmin"/> sin verificar el permiso específico.
    /// </summary>
    public bool AllowSuperAdmin { get; set; } = true;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Primera validación: el usuario debe estar autenticado antes de cualquier lógica de permisos
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var services = httpContext.RequestServices;
        var env = services.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
        var logger = services.GetService(typeof(ILogger<PermisoRequeridoAttribute>)) as ILogger<PermisoRequeridoAttribute>;
        var configuration = services.GetService(typeof(IConfiguration)) as IConfiguration;
        var requestPath = httpContext.Request.Path;

        // Permitir omitir permisos solo cuando la configuración lo habilite explícitamente en desarrollo
        var skipPermissionsInDevelopment =
            env?.IsDevelopment() is true &&
            configuration?.GetValue<bool>("Seguridad:OmitirPermisosEnDev") is true;

        if (skipPermissionsInDevelopment)
        {
            logger?.LogWarning(
                "Permisos omitidos en Development porque Seguridad:OmitirPermisosEnDev=true para {Username} al acceder a {Path}",
                user.Identity?.Name ?? "Desconocido",
                requestPath);

            return; // Permitir acceso en desarrollo
        }

        // Bypass de SuperAdmin (evita colisión con AuthorizeAttribute.Roles)
        if (AllowSuperAdmin && user.IsInRole(RoleConstants.SuperAdmin))
        {
            return;
        }

        // Normalizar valores de módulo y acción
        var normalizedModulo = (Modulo ?? string.Empty).Trim();
        var normalizedAccion = (Accion ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedModulo) || string.IsNullOrWhiteSpace(normalizedAccion))
        {
            logger?.LogError("PermisoRequeridoAttribute configurado sin Modulo o Accion en {Path}", requestPath);
            context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            return;
        }

        var hasPermission = PermissionAliasHelper.HasPermissionClaim(user, normalizedModulo, normalizedAccion);

        if (!hasPermission)
        {
            context.Result = new ForbidResult();
            return;
        }
    }
}

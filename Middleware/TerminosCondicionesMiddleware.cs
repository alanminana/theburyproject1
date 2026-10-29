using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Middleware
{
    /// <summary>
    /// Fuerza la aceptación de los Términos y Condiciones vigentes en cualquier
    /// navegación HTML autenticada, no solo cuando el usuario pasa por la pantalla
    /// de login. Sin esto, una sesión ya autenticada (cookie persistente, "recordarme",
    /// o simplemente no haber cerrado sesión) entra directo a cualquier URL interna sin
    /// que el chequeo de Login.cshtml.cs llegue a ejecutarse nunca.
    /// Navegación HTML (GET con Accept: text/html): redirige al login, que muestra la aceptación.
    /// SEG-BAJA: con <see cref="ClaveBloqueoTotal"/> activo (por defecto) además bloquea con 403 cualquier otro
    /// request autenticado (POST/PUT/DELETE y APIs/JSON) hasta aceptar. Quedan exentos login/logout/lockout/acceso
    /// denegado (necesarios para aceptar o salir) y /health; los estáticos se sirven antes en el pipeline.
    /// </summary>
    public class TerminosCondicionesMiddleware
    {
        /// <summary>Configuración: bloquea también métodos no-GET y APIs (default true). Solo la suite de tests la apaga.</summary>
        public const string ClaveBloqueoTotal = "Seguridad:TerminosBloqueoTotal";

        private static readonly string[] RutasExentas =
        {
            "/Identity/Account/Login", "/Identity/Account/Logout", "/Identity/Account/Lockout",
            "/Identity/Account/AccessDenied", "/Identity/Account/ExternalLogin", "/health"
        };

        private readonly RequestDelegate _next;

        public TerminosCondicionesMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var bloqueoTotal = context.RequestServices.GetService<IConfiguration>()?.GetValue(ClaveBloqueoTotal, true) ?? true;

            if (DebeVerificar(context, bloqueoTotal))
            {
                var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var terminosService = context.RequestServices.GetRequiredService<ITerminosCondicionesService>();

                var userId = userManager.GetUserId(context.User);
                if (!string.IsNullOrEmpty(userId) && !await terminosService.UsuarioAceptoVersionActualAsync(userId))
                {
                    if (EsNavegacionHtml(context))
                    {
                        var returnUrl = context.Request.Path + context.Request.QueryString;
                        context.Response.Redirect("/Identity/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"error\":\"terminos_pendientes\",\"mensaje\":\"Debe aceptar los Términos y Condiciones vigentes para continuar.\"}");
                    return;
                }
            }

            await _next(context);
        }

        private static bool EsNavegacionHtml(HttpContext context) =>
            context.Request.Method == HttpMethods.Get
            && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

        private static bool DebeVerificar(HttpContext context, bool bloqueoTotal)
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return false;

            if (bloqueoTotal)
            {
                var path = context.Request.Path;
                return !RutasExentas.Any(r => path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase));
            }

            // Modo legado: solo navegación HTML GET fuera de /Identity.
            if (EsRutaIdentity(context)) return false;
            return EsNavegacionHtml(context);
        }

        private static bool EsRutaIdentity(HttpContext context) =>
            context.Request.Path.StartsWithSegments("/Identity");
    }
}

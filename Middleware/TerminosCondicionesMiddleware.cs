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
    /// Se limita a GET con Accept: text/html para no interferir con llamadas AJAX/API
    /// existentes ni con el área Identity (evita loops con el propio login/logout).
    /// </summary>
    public class TerminosCondicionesMiddleware
    {
        private readonly RequestDelegate _next;

        public TerminosCondicionesMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (DebeVerificar(context))
            {
                var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var terminosService = context.RequestServices.GetRequiredService<ITerminosCondicionesService>();

                var userId = userManager.GetUserId(context.User);
                if (!string.IsNullOrEmpty(userId) && !await terminosService.UsuarioAceptoVersionActualAsync(userId))
                {
                    var returnUrl = context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect("/Identity/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
                    return;
                }
            }

            await _next(context);
        }

        private static bool DebeVerificar(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return false;

            if (context.Request.Method != HttpMethods.Get)
                return false;

            if (context.Request.Path.StartsWithSegments("/Identity"))
                return false;

            var accept = context.Request.Headers.Accept.ToString();
            return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}

#nullable disable

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Cierre de sesión que REVOCA todas las sesiones del usuario (incluidas las cookies copiadas antes del logout).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reemplaza la página por defecto de Identity.UI, que solo borra la cookie del navegador: una copia de esa cookie seguía
    /// siendo válida hasta expirar. Aquí se rota el <c>SecurityStamp</c> ANTES de cerrar la cookie. Como
    /// <see cref="TheBuryProject.Services.ActiveUserCookieValidator"/> compara el stamp contra la base en CADA solicitud (sin caché y antes
    /// del <c>SecurityStampValidator</c> estándar de 30 min), toda cookie emitida antes del logout se rechaza en su siguiente solicitud,
    /// en cualquier navegador o dispositivo del mismo usuario. Un login posterior emite un stamp nuevo y funciona normalmente.
    /// </para>
    /// <para>
    /// No se hace en <c>CookieAuthenticationEvents.OnSigningOut</c>: ese evento también se dispara cuando el validador rechaza una cookie
    /// vieja, y rotar el stamp ahí revocaría la sesión legítima y vigente del usuario.
    /// </para>
    /// <para>
    /// Costo: el logout agrega 1 SELECT (usuario) y 1 UPDATE (stamp). El camino de cada solicitud no cambia (el validador ya consultaba
    /// el stamp por PK).
    /// </para>
    /// </remarks>
    // Explícito: Program.cs define un FallbackPolicy que exige autenticación; un logout sin sesión (cookie ya vencida) debe poder completarse.
    // Antiforgery: se valida a mano (IgnoreAntiforgeryToken desactiva el filtro automático) para distinguir dos casos que el filtro trata igual:
    //  - CSRF contra una sesión VIVA  → 400 (un tercero no puede forzar la revocación de las sesiones de otro).
    //  - doble clic / reintento: la primera solicitud ya revocó la sesión y la segunda llega con la cookie ya inválida (anónima) y un token
    //    emitido para el usuario autenticado → no hay nada que cerrar ni que proteger: se lleva al login en vez de mostrar un 400 como respuesta final.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LogoutModel> _logger;
        private readonly ISeguridadAuditoriaService _auditoria;
        private readonly IAntiforgery _antiforgery;
        private readonly AppDbContext _db;

        public LogoutModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LogoutModel> logger,
            IAntiforgery antiforgery,
            AppDbContext db,
            ISeguridadAuditoriaService auditoria = null)
        {
            _antiforgery = antiforgery;
            _db = db;
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _auditoria = auditoria;
        }

        // Un GET nunca cierra sesión (evita cierres por enlaces/imágenes de terceros): lleva al login.
        public IActionResult OnGet() => RedirectToPage("./Login");

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            if (!await _antiforgery.IsRequestValidAsync(HttpContext))
            {
                if (User.Identity?.IsAuthenticated == true)
                    return BadRequest(); // token ausente/inválido con una sesión viva: CSRF
                return RedirectToPage("./Login"); // sin sesión vigente: nada que cerrar
            }

            string userId = null;
            var revocadas = false;
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    userId = user.Id;
                    var resultado = await _userManager.UpdateSecurityStampAsync(user);
                    revocadas = resultado.Succeeded;
                    if (!revocadas)
                    {
                        // La actualización fallida queda pendiente en el DbContext de la solicitud y haría fallar el INSERT de auditoría de abajo.
                        _db.ChangeTracker.Clear();
                        // Típico: otro logout simultáneo del mismo usuario ya rotó el stamp (concurrencia optimista) → ya está revocado.
                        var colision = resultado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
                        revocadas = colision;
                        _logger.Log(colision ? LogLevel.Information : LogLevel.Warning,
                            "Logout de {UserId}: no se pudo rotar el SecurityStamp ({Errores}).", userId,
                            string.Join(", ", resultado.Errors.Select(e => e.Code)));
                    }
                }
            }
            catch (Exception ex)
            {
                // El cierre de la sesión del propio navegador NO depende de la revocación global: se registra y se continúa.
                _logger.LogError(ex, "Logout de {UserId}: falló la revocación de sesiones; se cierra solo la sesión actual.", userId);
            }

            await _signInManager.SignOutAsync();

            if (userId != null && _auditoria != null)
            {
                await _auditoria.RegistrarEventoAsync("Seguridad", "Logout", $"Usuario:{userId}",
                    revocadas ? "Cierre de sesión: todas las sesiones previas del usuario quedaron invalidadas." : "Cierre de sesión (la revocación global NO se pudo confirmar: revisar el log).");
            }

            if (returnUrl != null && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToPage("./Login");
        }
    }
}

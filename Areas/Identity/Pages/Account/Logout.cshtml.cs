#nullable disable

using Microsoft.AspNetCore.Antiforgery;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
    /// Costo: el logout agrega 1 UPDATE (stamp) y 1 INSERT (auditoría); no lee el usuario (el id sale de la cookie validada). El camino de
    /// cada solicitud no cambia (el validador ya consultaba el stamp por PK).
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
        private readonly ILogger<LogoutModel> _logger;
        private readonly ISeguridadAuditoriaService _auditoria;
        private readonly IAntiforgery _antiforgery;
        private readonly AppDbContext _db;

        public LogoutModel(
            SignInManager<ApplicationUser> signInManager,
            ILogger<LogoutModel> logger,
            IAntiforgery antiforgery,
            AppDbContext db,
            ISeguridadAuditoriaService auditoria = null)
        {
            _antiforgery = antiforgery;
            _db = db;
            _signInManager = signInManager;
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

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var revocadas = false;
            if (!string.IsNullOrEmpty(userId))
            {
                try
                {
                    // Rotación DIRECTA en una sola sentencia (sin cargar la entidad ni pasar por UserManager.UpdateAsync):
                    //  - no depende de los tokens de concurrencia de la fila (RowVersion / ConcurrencyStamp): un login simultáneo (que
                    //    actualiza UltimoAcceso) o una edición del usuario por un administrador no pueden hacerla fallar;
                    //  - no ejecuta los validadores de Identity (unicidad de email, caracteres del nombre): un usuario legado con datos
                    //    "inválidos" también se revoca;
                    //  - cambia el ConcurrencyStamp, así que una escritura de Identity que ya tenía cargado el usuario con el stamp
                    //    anterior falla en vez de volver a escribir el SecurityStamp viejo (y RowVersion cambia solo en SQL Server).
                    var nuevoSecurityStamp = Guid.NewGuid().ToString();
                    var nuevoConcurrencyStamp = Guid.NewGuid().ToString();
                    var filas = await _db.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(u => u.SecurityStamp, nuevoSecurityStamp)
                            .SetProperty(u => u.ConcurrencyStamp, nuevoConcurrencyStamp));
                    revocadas = filas == 1;
                    if (!revocadas)
                        _logger.LogWarning("Logout de {UserId}: el usuario no existe; no hay sesiones que revocar.", userId);
                }
                catch (Exception ex)
                {
                    // El cierre de la sesión del propio navegador NO depende de la revocación global, pero un fallo aquí deja
                    // sesiones previas vivas: se registra como ERROR y la auditoría lo dice sin ambigüedad.
                    _logger.LogError(ex, "Logout de {UserId}: FALLÓ la revocación de sesiones; solo se cierra la sesión actual.", userId);
                }
            }

            await _signInManager.SignOutAsync();

            if (!string.IsNullOrEmpty(userId) && _auditoria != null)
            {
                await _auditoria.RegistrarEventoAsync("Seguridad", "Logout", $"Usuario:{userId}",
                    revocadas ? "Cierre de sesión: todas las sesiones previas del usuario quedaron invalidadas." : "Cierre de sesión: la revocación global NO se pudo confirmar (usuario inexistente o error de base): revisar el log.");
            }

            if (returnUrl != null && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToPage("./Login");
        }
    }
}

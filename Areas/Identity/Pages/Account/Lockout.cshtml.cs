using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // Reemplaza la página Lockout por defecto de Identity UI (en inglés y sin estilos).
    // Ver comentario en LoginModel: el FallbackPolicy global exige [AllowAnonymous]
    // explícito para que esta página siga siendo alcanzable sin sesión.
    [AllowAnonymous]
    public class LockoutModel : PageModel
    {
        private readonly IOptions<IdentityOptions> _identityOptions;

        public LockoutModel(IOptions<IdentityOptions> identityOptions)
        {
            _identityOptions = identityOptions;
        }

        /// <summary>Duración configurada del bloqueo (Program.cs), para no hardcodear el copy.</summary>
        public int MinutosBloqueo =>
            Math.Max(1, (int)Math.Ceiling(_identityOptions.Value.Lockout.DefaultLockoutTimeSpan.TotalMinutes));

        public void OnGet()
        {
        }
    }
}

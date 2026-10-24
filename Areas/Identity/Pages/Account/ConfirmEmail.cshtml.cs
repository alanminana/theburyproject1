using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // SEC-S-003: el ERP no tiene IEmailSender real ni recuperacion de contrasena por email (la pantalla de login indica
    // "Contacta al administrador"). Las paginas de recuperacion/confirmacion de Identity UI quedaban accesibles de forma
    // anonima sin funcion legitima y permitian distinguir cuentas existentes (ResetPassword con token invalido). Se
    // reemplazan con el mismo mecanismo 404; ver PaginaIdentityDeshabilitadaAttribute.
    [PaginaIdentityDeshabilitada]
    [IgnoreAntiforgeryToken]
    public class ConfirmEmailModel : PageModel
    {
    }
}

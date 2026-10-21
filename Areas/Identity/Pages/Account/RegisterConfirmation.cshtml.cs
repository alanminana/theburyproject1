using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // SEC-S-003: pertenece al flujo de autorregistro/confirmacion por email, que el ERP no usa
    // (sin IEmailSender real y sin altas publicas). Se reemplaza la pagina de Identity UI para que
    // no quede accesible de forma anonima; ver PaginaIdentityDeshabilitadaAttribute.
    [PaginaIdentityDeshabilitada]
    [IgnoreAntiforgeryToken]
    public class RegisterConfirmationModel : PageModel
    {
    }
}

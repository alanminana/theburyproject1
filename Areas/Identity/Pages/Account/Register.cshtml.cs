using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // SEC-S-003: el ERP no admite autorregistro. Las cuentas nuevas se crean solo desde
    // Usuarios/Create (permiso usuarios.create). Esta página reemplaza a la Register de Identity UI
    // y responde 404 a cualquier verbo; ver PaginaIdentityDeshabilitadaAttribute.
    [PaginaIdentityDeshabilitada]
    [IgnoreAntiforgeryToken]
    public class RegisterModel : PageModel
    {
    }
}

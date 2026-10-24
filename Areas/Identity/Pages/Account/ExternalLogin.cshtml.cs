using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // SEC-S-003: no hay proveedores externos configurados. La ExternalLogin de Identity UI también
    // puede crear usuarios (handler Confirmation), por lo que se reemplaza para que, si algún día se
    // agrega un proveedor, no reabra el autorregistro de forma silenciosa.
    [PaginaIdentityDeshabilitada]
    [IgnoreAntiforgeryToken]
    public class ExternalLoginModel : PageModel
    {
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    // Ver comentario en LoginModel: el FallbackPolicy global exige [AllowAnonymous]
    // explícito para que esta página siga siendo alcanzable sin sesión.
    [AllowAnonymous]
    public class AccessDeniedModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}

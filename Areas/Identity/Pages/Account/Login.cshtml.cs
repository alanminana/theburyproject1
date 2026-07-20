// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ITerminosCondicionesService _terminosCondicionesService;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ITerminosCondicionesService terminosCondicionesService,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _terminosCondicionesService = terminosCondicionesService;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

        public string ReturnUrl { get; set; }

        /// <summary>
        /// Cuando es true, esta misma página muestra el paso de aceptación de Términos y
        /// Condiciones en lugar del formulario de usuario/contraseña (las credenciales ya
        /// se validaron; no se navega a otra URL).
        /// </summary>
        public bool MostrarTerminos { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Display(Name = "Usuario")]
            public string UserName { get; set; }

            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Recordarme")]
            public bool RememberMe { get; set; }

            [Display(Name = "Nombre completo")]
            public string NombreCompleto { get; set; }

            public bool AceptaTerminos { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;

            if (User.Identity?.IsAuthenticated == true)
            {
                var authUser = await _userManager.GetUserAsync(User);
                if (authUser == null)
                {
                    return Page();
                }

                if (!await _terminosCondicionesService.UsuarioAceptoVersionActualAsync(authUser.Id))
                {
                    MostrarTerminos = true;
                    Input.NombreCompleto = authUser.NombreCompleto;
                    return Page();
                }

                return LocalRedirect(returnUrl);
            }

            // Clear the existing external cookie to ensure a clean login process.
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;

            // Fase 2: las credenciales ya se validaron en el POST anterior (cookie activa);
            // esta vez se está confirmando la aceptación de los Términos y Condiciones,
            // en la misma pantalla de login.
            if (User.Identity?.IsAuthenticated == true)
            {
                MostrarTerminos = true;

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Challenge();
                }

                if (await _terminosCondicionesService.UsuarioAceptoVersionActualAsync(user.Id))
                {
                    return LocalRedirect(returnUrl);
                }

                if (string.IsNullOrWhiteSpace(Input.NombreCompleto) || Input.NombreCompleto.Trim().Length < 3)
                {
                    ModelState.AddModelError("Input.NombreCompleto", "Debés escribir tu nombre completo (mínimo 3 caracteres) para confirmar la aceptación.");
                }

                if (!Input.AceptaTerminos)
                {
                    ModelState.AddModelError("Input.AceptaTerminos", "Tenés que aceptar los Términos y Condiciones para continuar.");
                }

                if (!ModelState.IsValid)
                {
                    return Page();
                }

                await _terminosCondicionesService.RegistrarAceptacionAsync(user.Id, user.UserName, Input.NombreCompleto);
                _logger.LogInformation("Términos y condiciones aceptados por {UserName}", user.UserName);

                return LocalRedirect(returnUrl);
            }

            // Fase 1: usuario y contraseña.
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (string.IsNullOrWhiteSpace(Input.UserName))
            {
                ModelState.AddModelError("Input.UserName", "El usuario es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(Input.Password))
            {
                ModelState.AddModelError("Input.Password", "La contraseña es obligatoria.");
            }

            if (ModelState.IsValid)
            {
                var userName = Input.UserName?.Trim();
                var result = await _signInManager.PasswordSignInAsync(
                    userName,
                    Input.Password,
                    Input.RememberMe,
                    lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    _logger.LogInformation("Usuario autenticado: {UserName}", userName);

                    var user = await _userManager.FindByNameAsync(userName);
                    if (user != null && !await _terminosCondicionesService.UsuarioAceptoVersionActualAsync(user.Id))
                    {
                        MostrarTerminos = true;
                        Input.NombreCompleto = user.NombreCompleto;
                        Input.Password = null;
                        return Page();
                    }

                    return LocalRedirect(returnUrl);
                }

                if (result.IsLockedOut)
                {
                    _logger.LogWarning("Cuenta bloqueada: {UserName}", userName);
                    return RedirectToPage("./Lockout");
                }

                ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            }

            return Page();
        }

        /// <summary>
        /// Vía alternativa ("desafiar a los dioses"): activa el flag de bypass para el
        /// usuario autenticado en sesión. El id nunca se toma del request, solo de User.
        /// </summary>
        public async Task<IActionResult> OnPostDesafiarAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;
            MostrarTerminos = true;

            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Input.NombreCompleto = user.NombreCompleto;

            var activado = await _terminosCondicionesService.ActivarDesafioALosDiosesAsync(user.Id, user.UserName);
            if (!activado)
            {
                ModelState.AddModelError(string.Empty, "No se pudo procesar la solicitud. Intentá nuevamente.");
                return Page();
            }

            _logger.LogInformation("Usuario {UserName} desafió a los dioses", user.UserName);
            return LocalRedirect(returnUrl);
        }
    }
}

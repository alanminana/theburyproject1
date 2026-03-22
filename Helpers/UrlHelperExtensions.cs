using Microsoft.AspNetCore.Mvc;

namespace TheBuryProject.Helpers
{
    public static class UrlHelperExtensions
    {
        public static string? GetSafeReturnUrl(this IUrlHelper url, string? returnUrl)
            => !string.IsNullOrWhiteSpace(returnUrl) && url.IsLocalUrl(returnUrl) ? returnUrl : null;

        /// <summary>
        /// Redirige a returnUrl (si es local) o a la acción Index del controller.
        /// </summary>
        public static IActionResult RedirectToReturnUrlOrIndex(this Controller controller, string? returnUrl)
        {
            var safeUrl = controller.Url.GetSafeReturnUrl(returnUrl);
            return safeUrl != null ? controller.LocalRedirect(safeUrl) : controller.RedirectToAction("Index");
        }
    }
}

using Microsoft.AspNetCore.Mvc;

namespace TheBuryProject.Helpers
{
    public static class UrlHelperExtensions
    {
        public static string? GetSafeReturnUrl(this IUrlHelper url, string? returnUrl)
            => !string.IsNullOrWhiteSpace(returnUrl) && url.IsLocalUrl(returnUrl) ? returnUrl : null;
    }
}

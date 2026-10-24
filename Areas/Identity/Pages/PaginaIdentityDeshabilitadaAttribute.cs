using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TheBuryProject.Areas.Identity.Pages
{
    /// <summary>
    /// SEC-S-003: marca una página de Identity UI como no disponible. Responde 404 a CUALQUIER verbo
    /// (GET, POST, PUT, handlers arbitrarios) antes de ejecutar el handler y sin exigir antiforgery,
    /// de modo que el comportamiento es uniforme y no depende de que exista un OnPost.
    /// Se aplica sobre una página propia que reemplaza a la de Identity UI (misma ruta, la de la app gana):
    /// no se usa AddPageRoute porque agrega rutas sin quitar las originales.
    /// IAllowAnonymous es intencional y NO debe reutilizarse en paginas que deban exigir sesion: hace que el
    /// FallbackPolicy no redirija al login antes de llegar al 404, para que anonimos y autenticados vean lo mismo.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class PaginaIdentityDeshabilitadaAttribute : Attribute, IResourceFilter, IAllowAnonymous
    {
        public void OnResourceExecuting(ResourceExecutingContext context) =>
            context.Result = new NotFoundResult();

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}

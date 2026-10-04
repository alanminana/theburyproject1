using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Documentos;

namespace TheBuryProject.Filters
{
    /// <summary>
    /// Si una acción emitió documentos y termina redirigiendo, deja el aviso para que la página siguiente los abra como PDF
    /// (ver <c>_DocumentosAbrir</c>). Cubre todos los eventos documentales (cobro, confirmación, entrega, reintentos…) en un
    /// único lugar. Las respuestas AJAX/JSON no se tocan: abren el documento por su cuenta.
    /// </summary>
    public sealed class DocumentosEmitidosFilter : IAsyncResultFilter
    {
        public const string ClaveTempData = "DocumentosAbrir";

        private readonly IDocumentosEmitidosTracker _tracker;

        public DocumentosEmitidosFilter(IDocumentosEmitidosTracker tracker)
        {
            _tracker = tracker;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (_tracker.Ids.Count > 0
                && EsRedireccion(context.Result)
                && !EsPeticionAjax(context.HttpContext.Request)
                && context.HttpContext.User.TienePermiso("documentos", "view")
                && context.Controller is Controller controlador)
            {
                controlador.TempData[ClaveTempData] = string.Join(",", _tracker.Ids.Take(20));
            }

            await next();
        }

        private static bool EsRedireccion(IActionResult? resultado) => resultado switch
        {
            // Abrir el documento ya es el destino de esta redirección: no se avisa dos veces.
            RedirectToActionResult r => !string.Equals(r.ActionName, "Ver", StringComparison.OrdinalIgnoreCase)
                                        && !string.Equals(r.ActionName, "VerVarios", StringComparison.OrdinalIgnoreCase),
            RedirectResult or RedirectToRouteResult or LocalRedirectResult => true,
            _ => false
        };

        private static bool EsPeticionAjax(HttpRequest request)
            => request.Headers.XRequestedWith == "XMLHttpRequest"
               || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}

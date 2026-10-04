using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Documentos;

namespace TheBuryProject.Filters
{
    /// <summary>
    /// Si una acción emitió documentos, hace que el navegador los abra como PDF al terminar. Cubre todos los eventos
    /// documentales (cobro, confirmación, entrega, reintentos…) y todas las formas de responder, en un único lugar:
    /// <list type="bullet">
    /// <item>Redirección o página (HTML): deja el aviso en TempData y el layout (<c>_DocumentosAbrir</c>) los abre.</item>
    /// <item>Respuesta AJAX/JSON: agrega el encabezado <c>X-Documentos-Abrir</c> y <c>documentos-abrir.js</c> (que envuelve fetch y
    /// XMLHttpRequest) los abre sin que la pantalla haga nada.</item>
    /// </list>
    /// Las descargas de archivo y las redirecciones hacia el propio PDF no se avisan dos veces.
    /// </summary>
    public sealed class DocumentosEmitidosFilter : IAsyncResultFilter
    {
        public const string ClaveTempData = "DocumentosAbrir";
        public const string EncabezadoAbrir = "X-Documentos-Abrir";

        private readonly IDocumentosEmitidosTracker _tracker;

        public DocumentosEmitidosFilter(IDocumentosEmitidosTracker tracker)
        {
            _tracker = tracker;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (_tracker.Ids.Count > 0
                && context.HttpContext.User.TienePermiso("documentos", "view")
                && !EsDescargaODestinoPdf(context.Result))
            {
                var ids = string.Join(",", _tracker.Ids.Take(20));
                var respuestaDeDatos = EsPeticionAjax(context.HttpContext.Request) || context.Result is not (ViewResult or PartialViewResult or RedirectResult or RedirectToActionResult or RedirectToRouteResult or LocalRedirectResult);

                if (respuestaDeDatos)
                    context.HttpContext.Response.Headers[EncabezadoAbrir] = ids;
                else if (context.Controller is Controller controlador)
                    controlador.TempData[ClaveTempData] = ids;
            }

            await next();
        }

        // Abrir el documento ya es el destino de la respuesta: no se avisa dos veces.
        private static bool EsDescargaODestinoPdf(IActionResult? resultado) => resultado switch
        {
            FileResult => true,
            RedirectToActionResult r => string.Equals(r.ActionName, "Ver", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(r.ActionName, "VerVarios", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        private static bool EsPeticionAjax(HttpRequest request)
            => request.Headers.XRequestedWith == "XMLHttpRequest"
               || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}

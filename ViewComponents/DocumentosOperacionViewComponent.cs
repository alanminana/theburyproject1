using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.ViewComponents
{
    public sealed class DocumentosOperacionViewModel
    {
        public List<DocumentoGenerado> Documentos { get; init; } = new();

        /// <summary>Grupo de impresión → nombre del paquete que forman sus documentos (ej. "Documentación de Crédito").</summary>
        public Dictionary<Guid, string> NombresDePaquete { get; init; } = new();
        public int? VentaId { get; init; }
        public int? CreditoId { get; init; }
        public int? PagoCuotaId { get; init; }
        public int? CotizacionId { get; init; }
        public bool Compacto { get; init; }
        public bool PuedeVer { get; init; }
        public bool PuedeReimprimir { get; init; }
        public bool PuedeFirmar { get; init; }
        public bool PuedeCancelar { get; init; }
        public bool PuedeGenerar { get; init; }

        /// <summary>Documentos que el usuario no puede abrir (contienen descuentos y falta ventas.discount): sin Ver/Imprimir.</summary>
        public IReadOnlySet<int> Restringidos { get; init; } = new HashSet<int>();

        /// <summary>Ofrece "Presupuesto" (venta a crédito con el plan ya configurado).</summary>
        public bool OfrecerPresupuesto { get; init; }
        public string ReturnUrl { get; init; } = string.Empty;
    }

    /// <summary>
    /// Panel "Documentos" de una operación (venta, crédito o cobranza): lista lo que emitió el motor
    /// documental con las acciones permitidas al usuario. No renderiza nada si el usuario no puede
    /// ver documentos.
    /// </summary>
    public class DocumentosOperacionViewComponent : ViewComponent
    {
        private readonly IDocumentoService _documentos;
        private readonly IDescuentoVisibilidadService _descuentoVisibilidad;

        public DocumentosOperacionViewComponent(IDocumentoService documentos, IDescuentoVisibilidadService descuentoVisibilidad)
        {
            _documentos = documentos;
            _descuentoVisibilidad = descuentoVisibilidad;
        }

        public async Task<IViewComponentResult> InvokeAsync(int? ventaId = null, int? creditoId = null, int? pagoCuotaId = null, int? cotizacionId = null, bool compacto = false, bool ofrecerPresupuesto = false)
        {
            if (!UserClaimsPrincipal.TienePermiso("documentos", "view"))
                return Content(string.Empty);

            List<DocumentoGenerado> docs;
            if (pagoCuotaId is int pago)
                docs = await _documentos.ObtenerPorPagoAsync(pago);
            else if (ventaId is int venta)
                docs = await _documentos.ObtenerPorVentaAsync(venta);
            else if (creditoId is int credito)
                docs = await _documentos.ObtenerPorCreditoAsync(credito);
            else if (cotizacionId is int cotizacion)
                docs = await _documentos.ObtenerPorCotizacionAsync(cotizacion);
            else
                return Content(string.Empty);

            var request = ViewContext.HttpContext.Request;
            var model = new DocumentosOperacionViewModel
            {
                Documentos = docs,
                Restringidos = await _descuentoVisibilidad.DocumentosRestringidosAsync(docs.Select(d => d.Id).ToList()),
                NombresDePaquete = await _documentos.ObtenerNombresDePaqueteAsync(docs),
                VentaId = ventaId,
                CreditoId = creditoId,
                PagoCuotaId = pagoCuotaId,
                CotizacionId = cotizacionId,
                Compacto = compacto,
                PuedeVer = true,
                PuedeReimprimir = UserClaimsPrincipal.TienePermiso("documentos", "reprint"),
                PuedeFirmar = UserClaimsPrincipal.TienePermiso("documentos", "sign"),
                PuedeCancelar = UserClaimsPrincipal.TienePermiso("documentos", "cancel"),
                PuedeGenerar = UserClaimsPrincipal.TienePermiso("documentos", "generate"),
                OfrecerPresupuesto = ofrecerPresupuesto && ventaId != null && !await _descuentoVisibilidad.DebeDenegarVentaAsync(ventaId.Value),
                ReturnUrl = $"{request.Path}{request.QueryString}"
            };

            return View(model);
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Helpers;
using TheBuryProject.Services;

namespace TheBuryProject.ViewComponents
{
    /// <summary>
    /// Panel "Costo de envío y armado" de una venta: precio cobrado, costo estándar y costo real cargable.
    /// Es información de rentabilidad: solo se muestra a quien puede actualizar ventas y si la venta tiene servicios.
    /// </summary>
    public class VentaCostoServiciosViewComponent : ViewComponent
    {
        private readonly IVentaCostoServiciosService _servicio;

        public VentaCostoServiciosViewComponent(IVentaCostoServiciosService servicio) => _servicio = servicio;

        public async Task<IViewComponentResult> InvokeAsync(int ventaId)
        {
            if (!UserClaimsPrincipal.TienePermiso("ventas", "update"))
                return Content(string.Empty);

            var info = await _servicio.ObtenerAsync(ventaId);
            if (info is not { TieneServicios: true })
                return Content(string.Empty);

            return View(info);
        }
    }
}

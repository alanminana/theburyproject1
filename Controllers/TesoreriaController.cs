using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// D-03 Tesorería administrativa, separada de la Caja de ventas: catálogo configurable de orígenes de fondos y resumen de egresos
    /// administrativos por origen. Ver = tesoreria.view; alta, edición y baja de orígenes = tesoreria.configure. Registrar pagos y
    /// anticipos sigue regido por cuentasporpagar.pay. No toca la Caja.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "tesoreria", Accion = "view")]
    public class TesoreriaController : Controller
    {
        private readonly ITesoreriaService _service;
        private readonly ICurrentUserService _currentUser;

        public TesoreriaController(ITesoreriaService service, ICurrentUserService currentUser)
        {
            _service = service;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta)
        {
            return View("Index_tw", new TesoreriaIndexViewModel
            {
                Resumen = await _service.ObtenerResumenAsync(desde, hasta),
                Origenes = await _service.ListarOrigenesAsync(),
                PuedeConfigurar = User.TienePermiso("tesoreria", "configure")
            });
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "tesoreria", Accion = "configure")]
        public IActionResult Crear() => View("Form_tw", new OrigenFondosFormViewModel { Datos = new OrigenFondosRequest { Activo = true } });

        [HttpGet]
        [PermisoRequerido(Modulo = "tesoreria", Accion = "configure")]
        public async Task<IActionResult> Editar(int id)
        {
            var o = await _service.ObtenerOrigenAsync(id);
            if (o is null)
                return NotFound();
            return View("Form_tw", new OrigenFondosFormViewModel
            {
                Movimientos = o.Movimientos,
                Datos = new OrigenFondosRequest
                {
                    Id = o.Id, Codigo = o.Codigo, Nombre = o.Nombre, Tipo = o.Tipo, Descripcion = o.Descripcion,
                    Activo = o.Activo, Orden = o.Orden, RowVersion = o.RowVersion
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "tesoreria", Accion = "configure")]
        public async Task<IActionResult> Guardar(OrigenFondosRequest datos)
        {
            var resultado = await _service.GuardarOrigenAsync(datos, _currentUser.GetUsername());
            if (!resultado.Ok)
            {
                ViewBag.Error = resultado.Error;
                var movimientos = datos.Id.HasValue ? (await _service.ObtenerOrigenAsync(datos.Id.Value))?.Movimientos ?? 0 : 0;
                return View("Form_tw", new OrigenFondosFormViewModel { Datos = datos, Movimientos = movimientos });
            }

            TempData["Success"] = datos.Id.HasValue ? "Origen de fondos actualizado." : "Origen de fondos creado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "tesoreria", Accion = "configure")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var resultado = await _service.EliminarOrigenAsync(id, _currentUser.GetUsername());
            if (resultado.Ok)
                TempData["Success"] = "Origen de fondos eliminado.";
            else
                TempData["Error"] = resultado.Error;
            return RedirectToAction(nameof(Index));
        }
    }
}

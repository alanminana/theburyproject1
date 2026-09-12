using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Gestión de marcas. La única UI real es el modal AJAX embebido en
    /// Catalogo/Index_tw (CreateAjax / EditAjax / GetJson) — no existen vistas propias
    /// (Index_tw/Create_tw/Edit_tw/Details_tw/Delete_tw) para este controller.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "marcas", Accion = "view")]
    public class MarcaController : Controller
    {
        private readonly IMarcaService _marcaService;
        private readonly ILogger<MarcaController> _logger;

        public MarcaController(IMarcaService marcaService, ILogger<MarcaController> logger)
        {
            _marcaService = marcaService;
            _logger = logger;
        }

        // GET: Marca — no tiene vista propia; la gestión real vive en Catalogo/Index.
        public IActionResult Index()
        {
            TempData["Info"] = "Las marcas se gestionan desde el catálogo.";
            return RedirectToAction("Index", "Catalogo");
        }

        // POST: Marca/Delete/5 — no tiene vista GET propia (no hay Delete_tw), pero el
        // botón "Eliminar" del modal de Catalogo/Index_tw sí postea acá (form-delete-marca
        // en marca-editar-modal.js). No es CRUD fantasma: es la única forma de eliminar.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            try
            {
                var result = await _marcaService.DeleteAsync(id);
                if (result)
                {
                    TempData["Success"] = "Marca eliminada exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se encontró la marca a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al eliminar marca {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar marca {Id}", id);
                TempData["Error"] = "Error al eliminar la marca. Intentá nuevamente.";
            }

            return this.RedirectToReturnUrlOrIndex(returnUrl);
        }

        #region AJAX — única vía real de gestión (modal del catálogo)

        /// <summary>
        /// Crea una marca vía AJAX (desde el modal del catálogo).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAjax(MarcaViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return Json(new { success = false, errors });
            }

            try
            {
                if (await _marcaService.ExistsCodigoAsync(viewModel.Codigo))
                {
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe una marca con este código" } } } });
                }

                var marca = new Marca
                {
                    Codigo = viewModel.Codigo,
                    Nombre = viewModel.Nombre,
                    Descripcion = viewModel.Descripcion,
                    ParentId = viewModel.ParentId,
                    PaisOrigen = viewModel.PaisOrigen,
                    Activo = viewModel.Activo
                };

                await _marcaService.CreateAsync(marca);
                return Json(new
                {
                    success = true,
                    message = "Marca creada exitosamente",
                    entity = new
                    {
                        id         = marca.Id,
                        codigo     = marca.Codigo,
                        nombre     = marca.Nombre,
                        paisOrigen = marca.PaisOrigen,
                        activo     = marca.Activo
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear marca vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear marca vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear la marca. Intentá nuevamente." } } } });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetJson(int id)
        {
            try
            {
                var marca = await _marcaService.GetByIdAsync(id);
                if (marca == null) return NotFound();
                return Json(new
                {
                    id = marca.Id,
                    codigo = marca.Codigo,
                    nombre = marca.Nombre,
                    descripcion = marca.Descripcion ?? string.Empty,
                    parentId = (object?)marca.ParentId,
                    paisOrigen = marca.PaisOrigen ?? string.Empty,
                    activo = marca.Activo,
                    rowVersion = marca.RowVersion != null ? Convert.ToBase64String(marca.RowVersion) : string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener JSON de marca {Id}", id);
                return StatusCode(500);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAjax(int id, MarcaViewModel viewModel)
        {
            if (id != viewModel.Id)
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "ID inválido" } } } });

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(k => k.Key, v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            try
            {
                if (viewModel.RowVersion is null || viewModel.RowVersion.Length == 0)
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "No se recibió la versión de fila. Recargá e intentá nuevamente." } } } });

                if (await _marcaService.ExistsCodigoAsync(viewModel.Codigo, id))
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe otra marca con este código" } } } });

                var marca = new Marca
                {
                    Id = viewModel.Id,
                    Codigo = viewModel.Codigo,
                    Nombre = viewModel.Nombre,
                    Descripcion = viewModel.Descripcion,
                    ParentId = viewModel.ParentId,
                    PaisOrigen = viewModel.PaisOrigen,
                    Activo = viewModel.Activo,
                    RowVersion = viewModel.RowVersion
                };

                await _marcaService.UpdateAsync(marca);
                return Json(new
                {
                    success = true,
                    message = "Marca actualizada exitosamente",
                    entity = new { id = marca.Id, codigo = viewModel.Codigo, nombre = viewModel.Nombre, paisOrigen = viewModel.PaisOrigen, activo = viewModel.Activo }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al editar marca vía AJAX {Id}", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar marca vía AJAX {Id}", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al actualizar. Intentá nuevamente." } } } });
            }
        }

        #endregion
    }
}

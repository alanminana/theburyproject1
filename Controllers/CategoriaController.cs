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
    /// Gestión de categorías. La única UI real es el modal AJAX embebido en
    /// Catalogo/Index_tw (CreateAjax / EditAjax / GetJson) — no existen vistas propias
    /// (Index_tw/Create_tw/Edit_tw/Details_tw/Delete_tw) para este controller.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "categorias", Accion = "view")]
    public class CategoriaController : Controller
    {
        private readonly ICategoriaService _categoriaService;
        private readonly ILogger<CategoriaController> _logger;

        public CategoriaController(
            ICategoriaService categoriaService,
            ILogger<CategoriaController> logger)
        {
            _categoriaService = categoriaService;
            _logger = logger;
        }

        // GET: Categoria — no tiene vista propia; la gestión real vive en Catalogo/Index.
        public IActionResult Index()
        {
            TempData["Info"] = "Las categorías se gestionan desde el catálogo.";
            return RedirectToAction("Index", "Catalogo");
        }

        // POST: Categoria/Delete/5 — no tiene vista GET propia (no hay Delete_tw), pero el
        // botón "Eliminar" del modal de Catalogo/Index_tw sí postea acá (form-delete-categoria
        // en categoria-editar-modal.js). No es CRUD fantasma: es la única forma de eliminar.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "categorias", Accion = "delete")]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            try
            {
                var result = await _categoriaService.DeleteAsync(id);
                if (result)
                {
                    TempData["Success"] = "Categoría eliminada exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se encontró la categoría a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al eliminar categoría {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar categoría {Id}", id);
                TempData["Error"] = "Error al eliminar la categoría. Intentá nuevamente.";
            }

            return this.RedirectToReturnUrlOrIndex(returnUrl);
        }

        #region AJAX — única vía real de gestión (modal del catálogo)

        /// <summary>
        /// Crea una categoría vía AJAX (desde el modal del catálogo). Cada acción exige su permiso
        /// propio (create/update/delete): el permiso de clase (view) solo habilita el listado.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "categorias", Accion = "create")]
        public async Task<IActionResult> CreateAjax(CategoriaViewModel viewModel)
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
                if (await _categoriaService.ExistsCodigoAsync(viewModel.Codigo))
                {
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe una categoría con este código" } } } });
                }

                var categoria = new Categoria
                {
                    Codigo = viewModel.Codigo,
                    Nombre = viewModel.Nombre,
                    Descripcion = viewModel.Descripcion,
                    ParentId = viewModel.ParentId,
                    ControlSerieDefault = viewModel.ControlSerieDefault,
                    AlicuotaIVAId = viewModel.AlicuotaIVAId,
                    Activo = viewModel.Activo
                };

                await _categoriaService.CreateAsync(categoria);
                // El catálogo se recarga tras el alta (única fuente de verdad del listado): el aviso
                // viaja por TempData y se muestra como toast de la página recargada.
                TempData["Success"] = $"Categoría «{categoria.Nombre}» creada exitosamente";
                return Json(new
                {
                    success = true,
                    message = "Categoría creada exitosamente",
                    entity = new
                    {
                        id     = categoria.Id,
                        codigo = categoria.Codigo,
                        nombre = categoria.Nombre,
                        activo = categoria.Activo
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear categoría vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear categoría vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear la categoría. Intentá nuevamente." } } } });
            }
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "categorias", Accion = "update")]
        public async Task<IActionResult> GetJson(int id)
        {
            try
            {
                var categoria = await _categoriaService.GetByIdAsync(id);
                if (categoria == null) return NotFound();
                return Json(new
                {
                    id = categoria.Id,
                    codigo = categoria.Codigo,
                    nombre = categoria.Nombre,
                    descripcion = categoria.Descripcion ?? string.Empty,
                    parentId = (object?)categoria.ParentId,
                    alicuotaIVAId = (object?)categoria.AlicuotaIVAId,
                    controlSerieDefault = categoria.ControlSerieDefault,
                    activo = categoria.Activo,
                    rowVersion = categoria.RowVersion != null ? Convert.ToBase64String(categoria.RowVersion) : string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener JSON de categoría {Id}", id);
                return StatusCode(500);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "categorias", Accion = "update")]
        public async Task<IActionResult> EditAjax(int id, CategoriaViewModel viewModel)
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

                if (await _categoriaService.ExistsCodigoAsync(viewModel.Codigo, id))
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe otra categoría con este código" } } } });

                var categoria = new Categoria
                {
                    Id = viewModel.Id,
                    Codigo = viewModel.Codigo,
                    Nombre = viewModel.Nombre,
                    Descripcion = viewModel.Descripcion,
                    ParentId = viewModel.ParentId,
                    ControlSerieDefault = viewModel.ControlSerieDefault,
                    AlicuotaIVAId = viewModel.AlicuotaIVAId,
                    Activo = viewModel.Activo,
                    RowVersion = viewModel.RowVersion
                };

                await _categoriaService.UpdateAsync(categoria);
                TempData["Success"] = $"Categoría «{viewModel.Nombre}» actualizada exitosamente";
                return Json(new
                {
                    success = true,
                    message = "Categoría actualizada exitosamente",
                    entity = new { id = categoria.Id, codigo = viewModel.Codigo, nombre = viewModel.Nombre, activo = viewModel.Activo }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al editar categoría vía AJAX {Id}", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar categoría vía AJAX {Id}", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al actualizar. Intentá nuevamente." } } } });
            }
        }

        #endregion
    }
}

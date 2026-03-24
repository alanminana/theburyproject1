using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models;
using TheBuryProject.Models.Entities;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "categorias", Accion = "view")]
    public class CategoriaController : Controller
    {
        private readonly ICategoriaService _categoriaService;
        private readonly ILogger<CategoriaController> _logger;
        private readonly IMapper _mapper;

        public CategoriaController(
            ICategoriaService categoriaService,
            ILogger<CategoriaController> logger,
            IMapper mapper)
        {
            _categoriaService = categoriaService;
            _logger = logger;
            _mapper = mapper;
        }
        public async Task<IActionResult> Index(
         string? searchTerm = null,
         bool soloActivos = false,
         string? orderBy = null,
         string? orderDirection = "asc",
         string? returnUrl = null)
        {
            try
            {
                ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

                // Ejecutar búsqueda con filtros
                var categorias = await _categoriaService.SearchAsync(
                    searchTerm,
                    soloActivos,
                    orderBy,
                    orderDirection
                );

                var viewModels = _mapper.Map<List<CategoriaViewModel>>(categorias);

                // Crear ViewModel de filtros
                var filterViewModel = new CategoriaFilterViewModel
                {
                    SearchTerm = searchTerm,
                    SoloActivos = soloActivos,
                    OrderBy = orderBy,
                    OrderDirection = orderDirection,
                    Categorias = viewModels,
                    TotalResultados = viewModels.Count
                };

                return View("Index_tw", filterViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener listado de categorías");
                TempData["Error"] = "Error al cargar las categorías. Intentá nuevamente.";
                return View("Index_tw", new CategoriaFilterViewModel());
            }
        }
        // GET: Categoria/Create
        public async Task<IActionResult> Create(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            await CargarCategoriasParaDropdown();
            return View("Create_tw", new CategoriaViewModel
            {
                Activo = true
            });
        }

        // POST: Categoria/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoriaViewModel viewModel, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            if (ModelState.IsValid)
            {
                try
                {
                    // Verificar que el código no exista
                    if (await _categoriaService.ExistsCodigoAsync(viewModel.Codigo))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe una categoría con este código");
                        await CargarCategoriasParaDropdown(viewModel.ParentId);
                        return View("Create_tw", viewModel);
                    }

                    var categoria = new Categoria
                    {
                        Codigo = viewModel.Codigo,
                        Nombre = viewModel.Nombre,
                        Descripcion = viewModel.Descripcion,
                        ParentId = viewModel.ParentId,
                        ControlSerieDefault = viewModel.ControlSerieDefault,
                        Activo = viewModel.Activo
                    };

                    await _categoriaService.CreateAsync(categoria);
                    TempData["Success"] = "Categoría creada exitosamente";
                    return this.RedirectToReturnUrlOrIndex(returnUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear categoría");
                    ModelState.AddModelError("", "Error al crear la categoría. Intentá nuevamente.");
                }
            }

            await CargarCategoriasParaDropdown(viewModel.ParentId);
            return View("Create_tw", viewModel);
        }

        // GET: Categoria/Details/5
        public async Task<IActionResult> Details(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var categoria = await _categoriaService.GetByIdAsync(id.Value);
                if (categoria == null)
                {
                    return NotFound();
                }

                var viewModel = new CategoriaViewModel
                {
                    Id = categoria.Id,
                    Codigo = categoria.Codigo,
                    Nombre = categoria.Nombre,
                    Descripcion = categoria.Descripcion,
                    ParentId = categoria.ParentId,
                    ParentNombre = categoria.Parent != null && !categoria.Parent.IsDeleted ? categoria.Parent.Nombre : null,
                    ControlSerieDefault = categoria.ControlSerieDefault,
                    Activo = categoria.Activo,
                    RowVersion = categoria.RowVersion
                };

                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles de categoría {Id}", id);
                TempData["Error"] = "Error al cargar los detalles. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Categoria/Edit/5
        public async Task<IActionResult> Edit(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var categoria = await _categoriaService.GetByIdAsync(id.Value);
                if (categoria == null)
                {
                    return NotFound();
                }

                var viewModel = new CategoriaViewModel
                {
                    Id = categoria.Id,
                    Codigo = categoria.Codigo,
                    Nombre = categoria.Nombre,
                    Descripcion = categoria.Descripcion,
                    ParentId = categoria.ParentId,
                    ControlSerieDefault = categoria.ControlSerieDefault,
                    Activo = categoria.Activo,
                    RowVersion = categoria.RowVersion
                };

                await CargarCategoriasParaDropdown(viewModel.ParentId, id.Value);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar categoría para editar {Id}", id);
                TempData["Error"] = "Error al cargar la categoría. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Categoria/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CategoriaViewModel viewModel, string? returnUrl = null)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            if (ModelState.IsValid)
            {
                try
                {
                    var rowVersion = viewModel.RowVersion;
                    if (rowVersion is null || rowVersion.Length == 0)
                    {
                        ModelState.AddModelError("", "No se recibió la versión de fila (RowVersion). Recargá la página e intentá nuevamente.");
                        await CargarCategoriasParaDropdown(viewModel.ParentId, id);
                        return View("Edit_tw", viewModel);
                    }

                    // Verificar que el código no exista (excluyendo el registro actual)
                    if (await _categoriaService.ExistsCodigoAsync(viewModel.Codigo, id))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe otra categoría con este código");
                        await CargarCategoriasParaDropdown(viewModel.ParentId, id);
                        return View("Edit_tw", viewModel);
                    }

                    var categoria = new Categoria
                    {
                        Id = viewModel.Id,
                        Codigo = viewModel.Codigo,
                        Nombre = viewModel.Nombre,
                        Descripcion = viewModel.Descripcion,
                        ParentId = viewModel.ParentId,
                        ControlSerieDefault = viewModel.ControlSerieDefault,
                        Activo = viewModel.Activo,
                        RowVersion = rowVersion
                    };

                    await _categoriaService.UpdateAsync(categoria);
                    TempData["Success"] = "Categoría actualizada exitosamente";
                    return this.RedirectToReturnUrlOrIndex(returnUrl);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al actualizar categoría {Id}", id);
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al actualizar categoría {Id}", id);
                    ModelState.AddModelError("", "Error al actualizar la categoría. Intentá nuevamente.");
                }
            }

            await CargarCategoriasParaDropdown(viewModel.ParentId, id);
            return View("Edit_tw", viewModel);
        }

        // GET: Categoria/Delete/5
        public async Task<IActionResult> Delete(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var categoria = await _categoriaService.GetByIdAsync(id.Value);
                if (categoria == null)
                {
                    return NotFound();
                }

                var viewModel = new CategoriaViewModel
                {
                    Id = categoria.Id,
                    Codigo = categoria.Codigo,
                    Nombre = categoria.Nombre,
                    Descripcion = categoria.Descripcion,
                    ParentId = categoria.ParentId,
                    ParentNombre = categoria.Parent != null && !categoria.Parent.IsDeleted ? categoria.Parent.Nombre : null,
                    ControlSerieDefault = categoria.ControlSerieDefault
                };

                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar categoría para eliminar {Id}", id);
                TempData["Error"] = "Error al cargar la categoría. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Categoria/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
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

        /// <summary>
        /// Carga las categorías disponibles para el dropdown de categoría padre
        /// </summary>
        private async Task CargarCategoriasParaDropdown(int? selectedId = null, int? excludeId = null)
        {
            var categorias = await _categoriaService.GetAllAsync();

            // Excluir la categoría actual (para evitar ciclos)
            if (excludeId.HasValue)
            {
                categorias = categorias.Where(c => c.Id != excludeId.Value);
            }

            ViewBag.Categorias = new SelectList(
                categorias.OrderBy(c => c.Nombre),
                "Id",
                "Nombre",
                selectedId
            );
        }

        /// <summary>
        /// Crea una categoría vía AJAX (desde el modal del catálogo).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
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
                    Activo = viewModel.Activo
                };

                await _categoriaService.CreateAsync(categoria);
                return Json(new { success = true, message = "Categoría creada exitosamente" });
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
    }
}
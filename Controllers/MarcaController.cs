using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Helpers;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "marcas", Accion = "view")]
    public class MarcaController : Controller
    {
        private readonly IMarcaService _marcaService;
        private readonly ILogger<MarcaController> _logger;
        private readonly IMapper _mapper;

        private IActionResult RedirectToReturnUrlOrIndex(string? returnUrl)
        {
            var safeReturnUrl = Url.GetSafeReturnUrl(returnUrl);
            return safeReturnUrl != null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction(nameof(Index));
        }

        public MarcaController(IMarcaService marcaService, ILogger<MarcaController> logger, IMapper mapper)
        {
            _marcaService = marcaService;
            _logger = logger;
            _mapper = mapper;
        }

        // GET: Marca
        // GET: Marca
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

                // Ejecutar b�squeda con filtros
                var marcas = await _marcaService.SearchAsync(
                    searchTerm,
                    soloActivos,
                    orderBy,
                    orderDirection
                );

                var viewModels = _mapper.Map<List<MarcaViewModel>>(marcas);

                // Crear ViewModel de filtros
                var filterViewModel = new MarcaFilterViewModel
                {
                    SearchTerm = searchTerm,
                    SoloActivos = soloActivos,
                    OrderBy = orderBy,
                    OrderDirection = orderDirection,
                    Marcas = viewModels,
                    TotalResultados = viewModels.Count
                };

                return View("Index_tw", filterViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener listado de marcas");
                TempData["Error"] = "Error al cargar las marcas. Por favor, intente nuevamente.";
                return View("Index_tw", new MarcaFilterViewModel());
            }
        }

        // GET: Marca/Details/5
        public async Task<IActionResult> Details(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var marca = await _marcaService.GetByIdAsync(id.Value);
                if (marca == null)
                {
                    return NotFound();
                }

                var viewModel = _mapper.Map<MarcaViewModel>(marca);

                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles de marca {Id}", id);
                TempData["Error"] = "Error al cargar los detalles. Por favor, intente nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Marca/Create
        public async Task<IActionResult> Create(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            await CargarMarcasParaDropdown();
            return View("Create_tw");
        }

        // POST: Marca/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MarcaViewModel viewModel, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);
            if (ModelState.IsValid)
            {
                try
                {
                    // Verificar que el c�digo no exista
                    if (await _marcaService.ExistsCodigoAsync(viewModel.Codigo))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe una marca con este c�digo");
                        await CargarMarcasParaDropdown(viewModel.ParentId);
                        return View("Create_tw", viewModel);
                    }

                    var marca = new Marca
                    {
                        Codigo = viewModel.Codigo,
                        Nombre = viewModel.Nombre,
                        Descripcion = viewModel.Descripcion,
                        ParentId = viewModel.ParentId,
                        PaisOrigen = viewModel.PaisOrigen
                    };

                    await _marcaService.CreateAsync(marca);
                    TempData["Success"] = "Marca creada exitosamente";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear marca");
                    ModelState.AddModelError("", "Error al crear la marca. Por favor, intente nuevamente.");
                }
            }

            await CargarMarcasParaDropdown(viewModel.ParentId);
            return View("Create_tw", viewModel);
        }

        // GET: Marca/Edit/5
        public async Task<IActionResult> Edit(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var marca = await _marcaService.GetByIdAsync(id.Value);
                if (marca == null)
                {
                    return NotFound();
                }

                var viewModel = _mapper.Map<MarcaViewModel>(marca);

                await CargarMarcasParaDropdown(viewModel.ParentId, id.Value);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar marca para editar {Id}", id);
                TempData["Error"] = "Error al cargar la marca. Por favor, intente nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Marca/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MarcaViewModel viewModel, string? returnUrl = null)
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
                        ModelState.AddModelError("", "No se recibió la versión de fila (RowVersion). Recargue la página e intente nuevamente.");
                        await CargarMarcasParaDropdown(viewModel.ParentId, id);
                        return View("Edit_tw", viewModel);
                    }

                    // Verificar que el c�digo no exista (excluyendo el registro actual)
                    if (await _marcaService.ExistsCodigoAsync(viewModel.Codigo, id))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe otra marca con este c�digo");
                        await CargarMarcasParaDropdown(viewModel.ParentId, id);
                        return View("Edit_tw", viewModel);
                    }

                    var marca = new Marca
                    {
                        Id = viewModel.Id,
                        Codigo = viewModel.Codigo,
                        Nombre = viewModel.Nombre,
                        Descripcion = viewModel.Descripcion,
                        ParentId = viewModel.ParentId,
                        PaisOrigen = viewModel.PaisOrigen,
                        RowVersion = rowVersion
                    };

                    await _marcaService.UpdateAsync(marca);
                    TempData["Success"] = "Marca actualizada exitosamente";
                    return RedirectToReturnUrlOrIndex(returnUrl);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validaci�n al actualizar marca {Id}", id);
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al actualizar marca {Id}", id);
                    ModelState.AddModelError("", "Error al actualizar la marca. Por favor, intente nuevamente.");
                }
            }

            await CargarMarcasParaDropdown(viewModel.ParentId, id);
            return View("Edit_tw", viewModel);
        }

        // GET: Marca/Delete/5
        public async Task<IActionResult> Delete(int? id, string? returnUrl = null)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnUrl"] = Url.GetSafeReturnUrl(returnUrl);

            try
            {
                var marca = await _marcaService.GetByIdAsync(id.Value);
                if (marca == null)
                {
                    return NotFound();
                }

                var viewModel = _mapper.Map<MarcaViewModel>(marca);

                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar marca para eliminar {Id}", id);
                TempData["Error"] = "Error al cargar la marca. Por favor, intente nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Marca/Delete/5
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
                    TempData["Error"] = "No se encontr� la marca a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validaci�n al eliminar marca {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar marca {Id}", id);
                TempData["Error"] = "Error al eliminar la marca. Por favor, intente nuevamente.";
            }

            return RedirectToReturnUrlOrIndex(returnUrl);
        }

        /// <summary>
        /// Carga las marcas disponibles para el dropdown de marca padre
        /// </summary>
        private async Task CargarMarcasParaDropdown(int? selectedId = null, int? excludeId = null)
        {
            var marcas = await _marcaService.GetAllAsync();

            // Excluir la marca actual (para evitar ciclos)
            if (excludeId.HasValue)
            {
                marcas = marcas.Where(m => m.Id != excludeId.Value);
            }

            ViewBag.Marcas = new SelectList(
                marcas.OrderBy(m => m.Nombre),
                "Id",
                "Nombre",
                selectedId
            );
        }

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
                return Json(new { success = true, message = "Marca creada exitosamente" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear marca vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear marca vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear la marca. Intente nuevamente." } } } });
            }
        }
    }
}
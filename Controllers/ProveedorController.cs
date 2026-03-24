// ProveedorController.cs
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Filters;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "proveedores", Accion = "view")]
    public class ProveedorController : Controller
    {
        private readonly IProveedorService _proveedorService;
        private readonly ILogger<ProveedorController> _logger;
        private readonly IMapper _mapper;
        private readonly ICatalogLookupService _catalogLookupService;

        public ProveedorController(
            IProveedorService proveedorService,
            ICatalogLookupService catalogLookupService,
            ILogger<ProveedorController> logger,
            IMapper mapper)
        {
            _proveedorService = proveedorService;
            _catalogLookupService = catalogLookupService;
            _logger = logger;
            _mapper = mapper;
        }

        #region CRUD

        // GET: Proveedor
        public async Task<IActionResult> Index(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            try
            {
                var proveedores = await _proveedorService.SearchAsync(
                    searchTerm,
                    soloActivos,
                    orderBy,
                    orderDirection
                );

                var viewModels = _mapper.Map<IEnumerable<ProveedorViewModel>>(proveedores);

                var (categorias, marcas, productos) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();

                var filterViewModel = new ProveedorFilterViewModel
                {
                    SearchTerm = searchTerm,
                    SoloActivos = soloActivos,
                    OrderBy = orderBy,
                    OrderDirection = orderDirection,
                    Proveedores = viewModels,
                    TotalResultados = viewModels.Count(),
                    CategoriasDisponibles = categorias.OrderBy(c => c.Nombre).Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Nombre }).ToList(),
                    MarcasDisponibles = marcas.OrderBy(m => m.Nombre).Select(m => new SelectListItem { Value = m.Id.ToString(), Text = m.Nombre }).ToList(),
                    ProductosDisponibles = productos.OrderBy(p => p.Nombre).Select(p => new SelectListItem { Value = p.Id.ToString(), Text = string.IsNullOrWhiteSpace(p.Codigo) ? p.Nombre : $"{p.Codigo} - {p.Nombre}" }).ToList()
                };

                return View("Index_tw", filterViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener listado de proveedores");
                TempData["Error"] = "Error al cargar los proveedores. Intentá nuevamente.";
                return View("Index_tw", new ProveedorFilterViewModel());
            }
        }

        // GET: Proveedor/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            try
            {
                var proveedor = await _proveedorService.GetByIdAsync(id.Value);
                if (proveedor == null) return NotFound();

                var viewModel = _mapper.Map<ProveedorViewModel>(proveedor);
                await CargarAsociacionesAsync(viewModel);
                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles del proveedor {Id}", id);
                TempData["Error"] = "Error al cargar los detalles del proveedor. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Proveedor/Create → redirige a Index (la creación es modal)
        public IActionResult Create()
        {
            return RedirectToAction(nameof(Index));
        }

        // POST: Proveedor/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProveedorViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Verificar CUIT único (mensaje amigable)
                    if (await _proveedorService.ExistsCuitAsync(viewModel.Cuit))
                    {
                        TempData["Error"] = "Ya existe un proveedor con este CUIT";
                        return RedirectToAction(nameof(Index));
                    }

                    var proveedor = _mapper.Map<Proveedor>(viewModel);
                    await _proveedorService.CreateAsync(proveedor);

                    TempData["Success"] = "Proveedor creado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al crear proveedor");
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear proveedor");
                    ModelState.AddModelError("", "Error al crear el proveedor. Intentá nuevamente.");
                }
            }

            TempData["Error"] = "Error de validación al crear el proveedor.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Proveedor/CreateAjax (AJAX modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAjax(ProveedorViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(k => k.Key, v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            try
            {
                if (await _proveedorService.ExistsCuitAsync(viewModel.Cuit))
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Cuit", new[] { "Ya existe un proveedor con este CUIT" } } } });

                var proveedor = _mapper.Map<Proveedor>(viewModel);
                await _proveedorService.CreateAsync(proveedor);
                return Json(new { success = true, message = "Proveedor creado exitosamente" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear proveedor vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear proveedor vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear el proveedor. Intentá nuevamente." } } } });
            }
        }

        // GET: Proveedor/Edit → redirige a Index (la edición es modal)
        public IActionResult Edit(int? id)
        {
            return RedirectToAction(nameof(Index));
        }

        // POST: Proveedor/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProveedorViewModel viewModel)
        {
            if (id != viewModel.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    if (await _proveedorService.ExistsCuitAsync(viewModel.Cuit, id))
                    {
                        TempData["Error"] = "Ya existe otro proveedor con este CUIT";
                        return RedirectToAction(nameof(Index));
                    }

                    var proveedor = _mapper.Map<Proveedor>(viewModel);
                    await _proveedorService.UpdateAsync(proveedor);

                    TempData["Success"] = "Proveedor actualizado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al actualizar proveedor {Id}", id);
                    TempData["Error"] = ex.Message;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al actualizar proveedor {Id}", id);
                    TempData["Error"] = "Error al actualizar el proveedor. Intentá nuevamente.";
                }
            }

            TempData["Error"] = "Error de validación al actualizar el proveedor.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Proveedor/GetEditData/5 (AJAX - devuelve JSON con los datos del proveedor)
        [HttpGet]
        public async Task<IActionResult> GetEditData(int id)
        {
            try
            {
                var proveedor = await _proveedorService.GetByIdAsync(id);
                if (proveedor == null)
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Proveedor no encontrado" } } } });

                var viewModel = _mapper.Map<ProveedorViewModel>(proveedor);
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        viewModel.Id,
                        rowVersion = viewModel.RowVersion != null ? Convert.ToBase64String(viewModel.RowVersion) : null,
                        viewModel.Cuit,
                        viewModel.RazonSocial,
                        viewModel.NombreFantasia,
                        viewModel.Email,
                        viewModel.Telefono,
                        viewModel.Contacto,
                        viewModel.Direccion,
                        viewModel.Ciudad,
                        viewModel.Provincia,
                        viewModel.CodigoPostal,
                        viewModel.Aclaraciones,
                        viewModel.Activo,
                        viewModel.CategoriasSeleccionadas,
                        viewModel.MarcasSeleccionadas,
                        viewModel.ProductosSeleccionados
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener datos del proveedor {Id} para edición", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al cargar el proveedor." } } } });
            }
        }

        // POST: Proveedor/EditAjax (AJAX modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAjax(ProveedorViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(k => k.Key, v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            try
            {
                if (await _proveedorService.ExistsCuitAsync(viewModel.Cuit, viewModel.Id))
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Cuit", new[] { "Ya existe otro proveedor con este CUIT" } } } });

                var proveedor = _mapper.Map<Proveedor>(viewModel);
                await _proveedorService.UpdateAsync(proveedor);
                return Json(new { success = true, message = "Proveedor actualizado exitosamente" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al actualizar proveedor vía AJAX {Id}", viewModel.Id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar proveedor vía AJAX {Id}", viewModel.Id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al actualizar el proveedor. Intentá nuevamente." } } } });
            }
        }

        // GET: Proveedor/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            try
            {
                var proveedor = await _proveedorService.GetByIdAsync(id.Value);
                if (proveedor == null) return NotFound();

                var viewModel = _mapper.Map<ProveedorViewModel>(proveedor);
                await CargarAsociacionesAsync(viewModel);

                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar proveedor para eliminar {Id}", id);
                TempData["Error"] = "Error al cargar el proveedor. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Proveedor/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var result = await _proveedorService.DeleteAsync(id);
                if (result)
                {
                    TempData["Success"] = "Proveedor eliminado exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se encontró el proveedor a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al eliminar proveedor {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar proveedor {Id}", id);
                TempData["Error"] = "Error al eliminar el proveedor. Intentá nuevamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region API y helpers

        // API: Obtener productos del proveedor
        [HttpGet]
        public async Task<IActionResult> GetProductos(int id)
        {
            try
            {
                var productosProveedor = await _proveedorService.GetProductosProveedorAsync(id);

                if (!productosProveedor.Any())
                {
                    return Json(new List<object>());
                }

                var productos = productosProveedor
                    .Where(pp => pp.Producto != null && pp.Producto.IsDeleted == false)
                    .Select(pp => new
                    {
                        id = pp.ProductoId,
                        codigo = pp.Producto!.Codigo,
                        nombre = pp.Producto.Nombre,
                        precio = pp.Producto.PrecioCompra,
                        stock = pp.Producto.StockActual,
                        activo = pp.Producto.Activo
                    })
                    .OrderBy(p => p.nombre)
                    .ToList();

                return Json(productos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos del proveedor {Id}", id);
                return StatusCode(500, new { error = "Error al obtener productos" });
            }
        }

        private async Task CargarAsociacionesAsync(ProveedorViewModel viewModel)
        {
            var (categorias, marcas, productos) = await _catalogLookupService.GetCategoriasMarcasYProductosAsync();

            viewModel.CategoriasDisponibles = categorias
                .OrderBy(c => c.Nombre)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Nombre,
                    Selected = viewModel.CategoriasSeleccionadas.Contains(c.Id)
                })
                .ToList();

            viewModel.MarcasDisponibles = marcas
                .OrderBy(m => m.Nombre)
                .Select(m => new SelectListItem
                {
                    Value = m.Id.ToString(),
                    Text = m.Nombre,
                    Selected = viewModel.MarcasSeleccionadas.Contains(m.Id)
                })
                .ToList();

            viewModel.ProductosDisponibles = productos
                .OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(p.Codigo) ? p.Nombre : $"{p.Codigo} - {p.Nombre}",
                    Selected = viewModel.ProductosSeleccionados.Contains(p.Id)
                })
                .ToList();
        }

        #endregion
    }
}

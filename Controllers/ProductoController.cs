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
    [PermisoRequerido(Modulo = "productos", Accion = "view")]
    public class ProductoController : Controller
    {
        private readonly IProductoService _productoService;
        private readonly ICatalogLookupService _catalogLookupService;
        private readonly ILogger<ProductoController> _logger;
        private readonly IMapper _mapper;

        public ProductoController(
            IProductoService productoService,
            ICatalogLookupService catalogLookupService,
            ILogger<ProductoController> logger,
            IMapper mapper)
        {
            _productoService = productoService;
            _catalogLookupService = catalogLookupService;
            _logger = logger;
            _mapper = mapper;
        }

        #region CRUD

        // GET: Producto
        /// <summary>
        /// Vista legacy de productos. Redirige al Catálogo unificado.
        /// </summary>
        public IActionResult Index(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            // Redirect permanente al nuevo Catálogo unificado preservando filtros
            return RedirectToActionPermanent("Index", "Catalogo", new
            {
                searchTerm,
                categoriaId,
                marcaId,
                stockBajo,
                soloActivos,
                orderBy,
                orderDirection
            });
        }

        // GET: Producto/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                {
                    return NotFound();
                }

                var viewModel = _mapper.Map<ProductoViewModel>(producto);
                return View("Details_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalles del producto {Id}", id);
                TempData["Error"] = "Error al cargar los detalles del producto. Intentá nuevamente.";
                return RedirectToAction(nameof(Index));
            }
        }
        /// <summary>
        /// Carga los dropdowns de Categorías y Marcas para los formularios y filtros
        /// </summary>
        /// <param name="categoriaSeleccionada">ID de categoría preseleccionada</param>
        /// <param name="marcaSeleccionada">ID de marca preseleccionada</param>
        /// <param name="paraFiltros">Si es true, usa ViewBag.CategoriasFiltro/MarcasFiltro; si no, ViewBag.Categorias/Marcas</param>
        private async Task CargarDropdownsAsync(int? categoriaSeleccionada = null, int? marcaSeleccionada = null, bool paraFiltros = false)
        {
            var (categorias, marcas) = await _catalogLookupService.GetCategoriasYMarcasAsync();

            var categoriasSelectList = new SelectList(
                categorias.OrderBy(c => c.Nombre),
                "Id",
                "Nombre",
                categoriaSeleccionada
            );

            var marcasSelectList = new SelectList(
                marcas.OrderBy(m => m.Nombre),
                "Id",
                "Nombre",
                marcaSeleccionada
            );

            if (paraFiltros)
            {
                ViewBag.CategoriasFiltro = categoriasSelectList;
                ViewBag.MarcasFiltro = marcasSelectList;
            }
            else
            {
                ViewBag.Categorias = categoriasSelectList;
                ViewBag.Marcas = marcasSelectList;
            }
        }
        // GET: Producto/Create
        public async Task<IActionResult> Create()
        {
            await CargarDropdownsAsync();
            return View("Create_tw");
        }

        // POST: Producto/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductoViewModel viewModel)
        {
            viewModel.Caracteristicas = NormalizarCaracteristicas(viewModel.Caracteristicas);

            if (ModelState.IsValid)
            {
                try
                {
                    // Verificar que el código no exista
                    if (await _productoService.ExistsCodigoAsync(viewModel.Codigo))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe un producto con este código");
                        await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                        return View("Create_tw", viewModel);
                    }

                    // El usuario ingresa PrecioVenta sin IVA; se calcula el precio final con IVA
                    viewModel.PrecioVenta = AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

                    var producto = _mapper.Map<Producto>(viewModel);
                    await _productoService.CreateAsync(producto);

                    TempData["Success"] = "Producto creado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al crear producto");
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear producto");
                    ModelState.AddModelError("", "Error al crear el producto. Intentá nuevamente.");
                }
            }

            await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
            return View("Create_tw", viewModel);
        }

        /// <summary>
        /// Crea un producto vía AJAX (desde el modal del catálogo).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAjax(ProductoViewModel viewModel)
        {
            viewModel.Caracteristicas = NormalizarCaracteristicas(viewModel.Caracteristicas);

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
                if (await _productoService.ExistsCodigoAsync(viewModel.Codigo))
                {
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe un producto con este código" } } } });
                }

                viewModel.PrecioVenta = AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);
                var producto = _mapper.Map<Producto>(viewModel);
                await _productoService.CreateAsync(producto);

                return Json(new { success = true, message = "Producto creado exitosamente" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al crear producto vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear producto vía AJAX");
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al crear el producto. Intentá nuevamente." } } } });
            }
        }

        // GET: Producto/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                    return NotFound();

                var viewModel = _mapper.Map<ProductoViewModel>(producto);

                // Mostrar PrecioVenta sin IVA (el almacenado incluye IVA)
                viewModel.PrecioVenta = QuitarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

                await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                return View("Edit_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar producto para editar {Id}", id);
                TempData["Error"] = "Error al cargar el producto";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Producto/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductoViewModel viewModel)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            viewModel.Caracteristicas = NormalizarCaracteristicas(viewModel.Caracteristicas);

            if (ModelState.IsValid)
            {
                try
                {
                    var rowVersion = viewModel.RowVersion;
                    if (rowVersion is null || rowVersion.Length == 0)
                    {
                        ModelState.AddModelError("", "No se recibió la versión de fila (RowVersion). Recargá la página e intentá nuevamente.");
                        await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                        return View("Edit_tw", viewModel);
                    }

                    // Verificar que el código no exista en otro producto
                    if (await _productoService.ExistsCodigoAsync(viewModel.Codigo, id))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe otro producto con este código");
                        await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                        return View("Edit_tw", viewModel);
                    }

                    // El usuario ingresa PrecioVenta sin IVA; se calcula el precio final con IVA
                    viewModel.PrecioVenta = AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

                    var producto = _mapper.Map<Producto>(viewModel);
                    producto.RowVersion = rowVersion;
                    await _productoService.UpdateAsync(producto);

                    TempData["Success"] = "Producto actualizado exitosamente";
                    return RedirectToAction(nameof(Index));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Error de validación al actualizar producto {Id}", id);
                    ModelState.AddModelError("", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al actualizar producto {Id}", id);
                    ModelState.AddModelError("", "Error al actualizar el producto. Intentá nuevamente.");
                }
            }

            await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
            return View("Edit_tw", viewModel);
        }

        // GET: Producto/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var producto = await _productoService.GetByIdAsync(id.Value);
                if (producto == null)
                    return NotFound();

                var viewModel = _mapper.Map<ProductoViewModel>(producto);
                return View("Delete_tw", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar producto para eliminar {Id}", id);
                TempData["Error"] = "Error al cargar el producto";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Producto/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var result = await _productoService.DeleteAsync(id);
                if (result)
                {
                    TempData["Success"] = "Producto eliminado exitosamente";
                }
                else
                {
                    TempData["Error"] = "No se encontr� el producto a eliminar";
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al eliminar producto {Id}", id);
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar producto {Id}", id);
                TempData["Error"] = "Error al eliminar el producto. Intentá nuevamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region API y helpers

        /// <summary>
        /// Obtiene las subcategorías (hijas) de una categoría padre para dropdown AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubcategorias(int categoriaId)
        {
            try
            {
                var subcategorias = await _catalogLookupService.GetSubcategoriasAsync(categoriaId);
                return Json(subcategorias.Select(s => new { id = s.Id, nombre = s.Nombre }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener subcategorías para categoría {CategoriaId}", categoriaId);
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Obtiene las submarcas (hijas) de una marca padre para dropdown AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubmarcas(int marcaId)
        {
            try
            {
                var submarcas = await _catalogLookupService.GetSubmarcasAsync(marcaId);
                return Json(submarcas.Select(s => new { id = s.Id, nombre = s.Nombre }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener submarcas para marca {MarcaId}", marcaId);
                return Json(new List<object>());
            }
        }

        private static decimal AplicarIVA(decimal precio, decimal porcentajeIVA)
            => precio * (1 + porcentajeIVA / 100m);

        private static decimal QuitarIVA(decimal precio, decimal porcentajeIVA)
            => porcentajeIVA > 0 ? Math.Round(precio / (1 + porcentajeIVA / 100m), 2) : precio;

        private static List<ProductoCaracteristicaViewModel> NormalizarCaracteristicas(IEnumerable<ProductoCaracteristicaViewModel>? caracteristicas)
        {
            if (caracteristicas == null)
                return new List<ProductoCaracteristicaViewModel>();

            return caracteristicas
                .Where(c => !string.IsNullOrWhiteSpace(c.Nombre) && !string.IsNullOrWhiteSpace(c.Valor))
                .Select(c => new ProductoCaracteristicaViewModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre.Trim(),
                    Valor = c.Valor.Trim()
                })
                .ToList();
        }

        #endregion
    }
}
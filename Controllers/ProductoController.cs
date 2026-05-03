using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
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
        private readonly ICatalogoService _catalogoService;
        private readonly ILogger<ProductoController> _logger;
        private readonly IMapper _mapper;

        public ProductoController(
            IProductoService productoService,
            ICatalogLookupService catalogLookupService,
            ICatalogoService catalogoService,
            ILogger<ProductoController> logger,
            IMapper mapper)
        {
            _productoService = productoService;
            _catalogLookupService = catalogLookupService;
            _catalogoService = catalogoService;
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

            await CargarAlicuotasIVAAsync();
        }

        private async Task CargarAlicuotasIVAAsync(int? alicuotaSeleccionada = null)
        {
            var alicuotas = await _catalogLookupService.ObtenerAlicuotasIVAParaFormAsync();
            ViewBag.AlicuotasIVA = new SelectList(alicuotas, "Id", "Texto", alicuotaSeleccionada);
            ViewBag.AlicuotasIVADatos = alicuotas;
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
            NormalizarComisionPorcentaje(viewModel);
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
                        await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
                        return View("Create_tw", viewModel);
                    }

                    // El usuario ingresa PrecioVenta sin IVA; se calcula el precio final con IVA
                    viewModel.PorcentajeIVA = await ResolverPorcentajeIVAAsync(viewModel);
                    viewModel.PrecioVenta = PrecioIvaCalculator.AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

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
            await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
            return View("Create_tw", viewModel);
        }

        /// <summary>
        /// Crea un producto vía AJAX (desde el modal del catálogo).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAjax(ProductoViewModel viewModel)
        {
            NormalizarComisionPorcentaje(viewModel);
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

                viewModel.PorcentajeIVA = await ResolverPorcentajeIVAAsync(viewModel);
                viewModel.PrecioVenta = PrecioIvaCalculator.AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);
                var producto = _mapper.Map<Producto>(viewModel);
                await _productoService.CreateAsync(producto);

                var productoCreado = await _productoService.GetByIdAsync(producto.Id);

                return Json(new
                {
                    success = true,
                    message = "Producto creado exitosamente",
                    entity = new
                    {
                        id                 = producto.Id,
                        codigo             = productoCreado?.Codigo             ?? producto.Codigo,
                        nombre             = productoCreado?.Nombre             ?? producto.Nombre,
                        descripcion        = productoCreado?.Descripcion        ?? producto.Descripcion,
                        categoriaNombre    = productoCreado?.Categoria?.Nombre  ?? "—",
                        marcaNombre        = productoCreado?.Marca?.Nombre      ?? "—",
                        precioVenta        = productoCreado?.PrecioVenta        ?? viewModel.PrecioVenta,
                        comisionPorcentaje = 0m
                    }
                });
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
                viewModel.PrecioVenta = PrecioIvaCalculator.QuitarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

                await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
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

            NormalizarComisionPorcentaje(viewModel);
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
                        await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
                        return View("Edit_tw", viewModel);
                    }

                    // Verificar que el código no exista en otro producto
                    if (await _productoService.ExistsCodigoAsync(viewModel.Codigo, id))
                    {
                        ModelState.AddModelError("Codigo", "Ya existe otro producto con este código");
                        await CargarDropdownsAsync(viewModel.CategoriaId, viewModel.MarcaId);
                        await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
                        return View("Edit_tw", viewModel);
                    }

                    // El usuario ingresa PrecioVenta sin IVA; se calcula el precio final con IVA
                    viewModel.PorcentajeIVA = await ResolverPorcentajeIVAAsync(viewModel);
                    viewModel.PrecioVenta = PrecioIvaCalculator.AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

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
            await CargarAlicuotasIVAAsync(viewModel.AlicuotaIVAId);
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
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
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
                    TempData["Error"] = "No se encontró el producto a eliminar";
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

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region API y helpers

        [HttpGet]
        public async Task<IActionResult> GetJson(int id)
        {
            try
            {
                var producto = await _productoService.GetByIdAsync(id);
                if (producto == null) return NotFound();

                var vm = _mapper.Map<ProductoViewModel>(producto);
                var precioSinIVA = PrecioIvaCalculator.QuitarIVA(vm.PrecioVenta, vm.PorcentajeIVA);
                var fila = await _catalogoService.ObtenerFilaAsync(id);

                return Json(new
                {
                    id = vm.Id,
                    rowVersion = vm.RowVersion != null ? Convert.ToBase64String(vm.RowVersion) : "",
                    codigo = vm.Codigo,
                    nombre = vm.Nombre,
                    descripcion = vm.Descripcion,
                    categoriaId = vm.CategoriaId,
                    categoriaNombre = vm.CategoriaNombre,
                    subcategoriaId = vm.SubcategoriaId,
                    subcategoriaNombre = vm.SubcategoriaNombre,
                    marcaId = vm.MarcaId,
                    marcaNombre = vm.MarcaNombre,
                    submarcaId = vm.SubmarcaId,
                    submarcaNombre = vm.SubmarcaNombre,
                    precioCompra = vm.PrecioCompra,
                    precioVenta = precioSinIVA,
                    porcentajeIVA = vm.PorcentajeIVA,
                    alicuotaIVAId = vm.AlicuotaIVAId,
                    comisionPorcentaje = vm.ComisionPorcentaje,
                    maxCuotasSinInteresPermitidas = vm.MaxCuotasSinInteresPermitidas,
                    stockActual = vm.StockActual,
                    stockMinimo = vm.StockMinimo,
                    activo = vm.Activo,
                    caracteristicas = vm.Caracteristicas.Select(c => new { id = c.Id, nombre = c.Nombre, valor = c.Valor }),
                    precioActual = fila?.PrecioActual ?? vm.PrecioVenta,
                    precioBase = fila?.PrecioBase ?? vm.PrecioVenta,
                    tienePrecioLista = fila?.TienePrecioLista ?? false,
                    listaPrecioActualNombre = fila?.ListaPrecioActualNombre
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener producto JSON {Id}", id);
                return StatusCode(500, new { error = "Error al cargar el producto" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAjax(int id, ProductoViewModel viewModel)
        {
            if (id != viewModel.Id)
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Id inválido." } } } });

            NormalizarComisionPorcentaje(viewModel);
            viewModel.Caracteristicas = NormalizarCaracteristicas(viewModel.Caracteristicas);

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            try
            {
                if (viewModel.RowVersion is null || viewModel.RowVersion.Length == 0)
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "No se recibió la versión de fila. Recargá la página." } } } });

                if (await _productoService.ExistsCodigoAsync(viewModel.Codigo, id))
                    return Json(new { success = false, errors = new Dictionary<string, string[]> { { "Codigo", new[] { "Ya existe otro producto con este código." } } } });

                viewModel.PorcentajeIVA = await ResolverPorcentajeIVAAsync(viewModel);
                viewModel.PrecioVenta = PrecioIvaCalculator.AplicarIVA(viewModel.PrecioVenta, viewModel.PorcentajeIVA);

                var producto = _mapper.Map<Producto>(viewModel);
                producto.RowVersion = viewModel.RowVersion;
                await _productoService.UpdateAsync(producto);

                var fila = await _catalogoService.ObtenerFilaAsync(id);

                return Json(new
                {
                    success = true,
                    message = "Producto actualizado exitosamente",
                    entity = new
                    {
                        id = fila!.ProductoId,
                        codigo = fila.Codigo,
                        nombre = fila.Nombre,
                        descripcion = fila.Descripcion,
                        categoriaNombre = fila.CategoriaNombre,
                        marcaNombre = fila.MarcaNombre,
                        precioActual = fila.PrecioActual,
                        precioBase = fila.PrecioBase,
                        tienePrecioLista = fila.TienePrecioLista,
                        listaPrecioActualNombre = fila.ListaPrecioActualNombre,
                        margenPorcentaje = fila.MargenPorcentaje,
                        comisionPorcentaje = fila.ComisionPorcentaje,
                        stockActual = fila.StockActual,
                        activo = fila.Activo
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { ex.Message } } } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar producto {Id} vía AJAX", id);
                return Json(new { success = false, errors = new Dictionary<string, string[]> { { "", new[] { "Error al actualizar el producto. Intentá nuevamente." } } } });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "productos", Accion = "edit")]
        public async Task<IActionResult> ActualizarComisionVendedor(int productoId, string porcentajeComision)
        {
            var rawNormalizado = (porcentajeComision ?? "").Trim().Replace(',', '.');
            if (!decimal.TryParse(rawNormalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out var porcentaje))
                return Json(new { success = false, message = "El porcentaje ingresado no es válido." });

            try
            {
                var producto = await _productoService.ActualizarComisionAsync(productoId, porcentaje);
                return Json(new { success = true, message = "Comisión actualizada exitosamente.", comisionPorcentaje = producto.ComisionPorcentaje });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar comisión del producto {Id}", productoId);
                return Json(new { success = false, message = "Error al actualizar la comisión. Intentá nuevamente." });
            }
        }

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

        private async Task<decimal> ResolverPorcentajeIVAAsync(ProductoViewModel viewModel)
        {
            if (viewModel.AlicuotaIVAId.HasValue)
            {
                var porcentaje = await _catalogLookupService.ObtenerPorcentajeAlicuotaAsync(viewModel.AlicuotaIVAId.Value);
                if (porcentaje.HasValue)
                    return porcentaje.Value;
            }

            return viewModel.PorcentajeIVA;
        }

        private void NormalizarComisionPorcentaje(ProductoViewModel viewModel)
        {
            var raw = Request.Form[nameof(ProductoViewModel.ComisionPorcentaje)].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(raw))
            {
                viewModel.ComisionPorcentaje = 0m;
                ModelState.Remove(nameof(ProductoViewModel.ComisionPorcentaje));
                return;
            }

            var normalizado = raw.Trim().Replace(',', '.');
            if (decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
            {
                viewModel.ComisionPorcentaje = valor;
                ModelState.Remove(nameof(ProductoViewModel.ComisionPorcentaje));
            }
        }

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
